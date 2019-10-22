using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Xunit;
using Ztm.Data.Entity.Contexts.Main;
using Ztm.Data.Entity.Testing;

namespace Ztm.WebApi.Tests
{
    sealed class TestCallbackResult : CallbackResult
    {
        public TestCallbackResult(string status, object data)
        {
            this.status = status;
            this.data = data;
        }

        readonly string status;
        readonly object data;

        public override string Status => status;

        public override object Data => data;
    }

    public class SqlCallbackRepositoryTests : IDisposable
    {
        readonly SqlCallbackRepository subject;
        readonly TestMainDatabaseFactory dbFactory;

        readonly Uri defaultUrl;

        public SqlCallbackRepositoryTests()
        {
            this.defaultUrl = new Uri("http://zcoin.io");

            this.dbFactory = new TestMainDatabaseFactory();
            this.subject = new SqlCallbackRepository(dbFactory);
        }

        public void Dispose()
        {
            this.dbFactory.Dispose();
        }

        [Fact]
        public void ConstructSqlCallbackRepository_WithNullDatabaseFactory_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(
                "db",
                () => new SqlCallbackRepository(null)
            );
        }

        [Fact]
        public async Task AddAsync_WithValidArgs_ShouldSuccess()
        {
            // Act.
            var callback = await this.subject.AddAsync(IPAddress.Loopback, this.defaultUrl, CancellationToken.None);

            // Assert.
            Assert.NotEqual(Guid.Empty, callback.Id);
            Assert.Equal(IPAddress.Loopback, callback.RegisteredIp);
            Assert.True(DateTime.Now.Add(TimeSpan.FromSeconds(-1)).ToUniversalTime() < callback.RegisteredTime);
            Assert.False(callback.Completed);
            Assert.Equal(this.defaultUrl, callback.Url);
        }

        [Fact]
        public async Task AddAsync_WithInvalidArgs_ShouldThrow()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                "registeringIp", () => this.subject.AddAsync(null, this.defaultUrl, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(
                "url", () => this.subject.AddAsync(IPAddress.Loopback, null, CancellationToken.None));
        }

        [Fact]
        public async Task SetCompletedAsyc_WithValidId_ShouldSuccess()
        {
            // Arrange.
            var callback = await this.subject.AddAsync(IPAddress.Loopback, this.defaultUrl, CancellationToken.None);

            // Act.
            await this.subject.SetCompletedAsyc(callback.Id, CancellationToken.None);
            WebApiCallback updated;
            using (var db = this.dbFactory.CreateDbContext())
            {
                updated = await db.WebApiCallbacks.FirstAsync(CancellationToken.None);
            }

            // Assert.
            Assert.Equal(callback.Id, updated.Id);
            Assert.Equal(IPAddress.Loopback, updated.RegisteredIp);
            Assert.Equal(callback.RegisteredTime, updated.RegisteredTime);
            Assert.True(updated.Completed);
            Assert.Equal(this.defaultUrl, updated.Url);
        }

        [Fact]
        public async Task SetCompletedAsyc_WithNonexistentId_ShouldThrow()
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => this.subject.SetCompletedAsyc(Guid.NewGuid(), CancellationToken.None));
        }

        [Fact]
        public async Task GetAsync_WithExistentId_ShouldSuccess()
        {
            // Act.
            var callback = await this.subject.AddAsync(IPAddress.Loopback, this.defaultUrl, CancellationToken.None);
            var retrieved = await this.subject.GetAsync(callback.Id, CancellationToken.None);

            // Assert.
            Assert.Equal(callback.Id, retrieved.Id);
            Assert.Equal(IPAddress.Loopback, retrieved.RegisteredIp);
            Assert.Equal(callback.RegisteredTime, retrieved.RegisteredTime);
            Assert.False(retrieved.Completed);
            Assert.Equal(this.defaultUrl, retrieved.Url);
        }

        [Fact]
        public async Task GetAsync_WithNonexistentId_ShouldReturnNull()
        {
            var retrieved = await this.subject.GetAsync(Guid.NewGuid(), CancellationToken.None);
            Assert.Null(retrieved);
        }

        [Fact]
        public async Task AddHistoryAsync_WithExistentId_ShouldSuccess()
        {
            // Arrange.
            var callback = await this.subject.AddAsync(IPAddress.Loopback, this.defaultUrl, CancellationToken.None);

            var data = "txid:46bdfcc6c953ba3e9a12456e3bd75ff887c9ba50051b3c58113eebffa35d7df4";

            // Act.
            await this.subject.AddHistoryAsync(
                callback.Id, new TestCallbackResult(CallbackResult.StatusUpdate, data), CancellationToken.None);

            // Assert.
            WebApiCallbackHistory history;
            using (var db = this.dbFactory.CreateDbContext())
            {
                history = await db.WebApiCallbackHistories.FirstAsync(CancellationToken.None);
            }

            Assert.Equal(1, history.Id);
            Assert.Equal(callback.Id, history.CallbackId);
            Assert.Equal(CallbackResult.StatusUpdate, history.Status);
            Assert.True(DateTime.Now.Add(TimeSpan.FromSeconds(-1)).ToUniversalTime()
                < DateTime.SpecifyKind(history.InvokedTime, DateTimeKind.Utc));
            Assert.Equal(data, JsonConvert.DeserializeObject<string>(history.Data));
        }

        [Fact]
        public async Task AddHistoryAsync_Twice_IdShouldBeIncreased()
        {
            // Arrange.
            var callback = await this.subject.AddAsync(IPAddress.Loopback, this.defaultUrl, CancellationToken.None);
            var data = "txid:46bdfcc6c953ba3e9a12456e3bd75ff887c9ba50051b3c58113eebffa35d7df4";

            // Act.
            await this.subject.AddHistoryAsync(
                callback.Id, new TestCallbackResult(CallbackResult.StatusUpdate, data), CancellationToken.None);
            await this.subject.AddHistoryAsync(
                callback.Id, new TestCallbackResult(CallbackResult.StatusUpdate, data), CancellationToken.None);

            // Assert.
            var histories = new List<WebApiCallbackHistory>();
            using (var db = this.dbFactory.CreateDbContext())
            {
                await db.WebApiCallbackHistories.ForEachAsync(delegate(WebApiCallbackHistory history)
                {
                    histories.Add(history);
                });
            }

            Assert.Equal(2, histories.Count);
            Assert.Equal(1, histories[0].Id);
            Assert.Equal(2, histories[1].Id);
        }

        [Fact]
        public async Task AddHistoryAsync_WithNullCallbackResult_ShouldThrow()
        {
            await Assert.ThrowsAsync<ArgumentNullException>
            (
                "result",
                () => this.subject.AddHistoryAsync(Guid.NewGuid(), null, CancellationToken.None)
            );
        }

        [Fact]
        public async Task AddHistoryAsync_WithNonexistentId_ShouldThrow()
        {
            var data = "txid:46bdfcc6c953ba3e9a12456e3bd75ff887c9ba50051b3c58113eebffa35d7df4";
            await Assert.ThrowsAsync<KeyNotFoundException>
            (
                () => this.subject.AddHistoryAsync(
                    Guid.NewGuid(), new TestCallbackResult(CallbackResult.StatusUpdate, data), CancellationToken.None
                )
            );
        }
    }
}