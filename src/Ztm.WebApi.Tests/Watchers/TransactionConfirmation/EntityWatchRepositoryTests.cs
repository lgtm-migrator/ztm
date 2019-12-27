using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using Newtonsoft.Json;
using Xunit;
using Ztm.Data.Entity.Testing;
using Ztm.WebApi.Callbacks;
using Ztm.WebApi.Watchers.TransactionConfirmation;
using Ztm.Zcoin.Watching;

namespace Ztm.WebApi.Tests.Watchers.TransactionConfirmation
{
    public sealed class EntityWatchRepositoryTests : IDisposable
    {
        readonly TestMainDatabaseFactory databaseFactory;
        readonly EntityCallbackRepository callbackRepository;
        readonly EntityRuleRepository ruleRepository;
        readonly EntityWatchRepository subject;

        public EntityWatchRepositoryTests()
        {
            var serializer = JsonSerializer.Create();

            this.databaseFactory = new TestMainDatabaseFactory();
            this.callbackRepository = new EntityCallbackRepository(this.databaseFactory, JsonSerializer.Create());
            this.ruleRepository = new EntityRuleRepository(this.databaseFactory, serializer);
            this.subject = new EntityWatchRepository(this.databaseFactory, serializer);
        }

        public void Dispose()
        {
            this.databaseFactory.Dispose();
        }

        [Fact]
        public void Construct_WithNullArguments_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(
                "db",
                () => new EntityWatchRepository(null, JsonSerializer.Create())
            );

            Assert.Throws<ArgumentNullException>(
                "serializer",
                () => new EntityWatchRepository(this.databaseFactory, null)
            );
        }

        [Fact]
        public async Task AddAsync_WithNullWatch_ShouldThrow()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                "watch",
                () => this.subject.AddAsync(null, CancellationToken.None)
            );
        }

        [Fact]
        public async Task AddAsync_WithNullContext_ShouldThrow()
        {
            var watch = new TransactionWatch<Rule>(null, uint256.One, uint256.One);

            await Assert.ThrowsAsync<ArgumentException>(
                "watch",
                () => this.subject.AddAsync(watch, CancellationToken.None)
            );
        }

        [Fact]
        public async Task AddAsync_WithValidWatch_ShouldSuccess()
        {
            // Arrange.
            var rule = await GenerateRuleAsync();
            var watch = new TransactionWatch<Rule>(rule, uint256.One, uint256.One);

            // Act.
            await this.subject.AddAsync(watch, CancellationToken.None);

            // Assert.
            using (var db = this.databaseFactory.CreateDbContext())
            {
                var retrieved = await db.TransactionConfirmationWatcherWatches
                    .FirstOrDefaultAsync(w => w.Id == watch.Id, CancellationToken.None);

                Assert.Equal(rule.Id, retrieved.RuleId);
                Assert.Equal(watch.Id, retrieved.Id);
                Assert.Equal(watch.StartBlock, retrieved.StartBlockHash);
                Assert.Equal(watch.StartTime, DateTime.SpecifyKind(retrieved.StartTime, DateTimeKind.Utc));
            }
        }

        [Fact]
        public async Task ListAsync_EmptyWatch_ShouldReturnEmpty()
        {
            var watches = await this.subject.ListAsync(
                WatchStatus.Rejected,
                CancellationToken.None
            );

            Assert.Empty(watches);
        }

        [Fact]
        public async Task ListAsync_AndNotEmpty_ShouldSuccess()
        {
            // Arrange.
            var rule = await GenerateRuleAsync();
            var watch = new TransactionWatch<Rule>(rule, uint256.One, uint256.One);

            await this.subject.AddAsync(watch, CancellationToken.None);

            // Act.
            var watches = await this.subject.ListAsync(WatchStatus.Pending, CancellationToken.None);

            // Assert.
            Assert.Single(watches);
        }

        [Fact]
        public async Task ListAsync_ShouldGetOnlySpecificStatus()
        {
            // Arrange.
            var rule = await GenerateRuleAsync();
            var watch = new TransactionWatch<Rule>(rule, uint256.One, uint256.One);
            await this.subject.AddAsync(watch, CancellationToken.None);

            var rule2 = await GenerateRuleAsync();
            var watch2 = new TransactionWatch<Rule>(rule2, uint256.One, uint256.One);
            await this.subject.AddAsync(watch2, CancellationToken.None);

            await this.subject.UpdateStatusAsync(watch2.Id, WatchStatus.Rejected, CancellationToken.None);

            // Act.
            var rejectedWatches = await this.subject.ListAsync(WatchStatus.Rejected, CancellationToken.None);
            var pendingWatches = await this.subject.ListAsync(WatchStatus.Pending, CancellationToken.None);

            // Assert.
            Assert.Single(rejectedWatches);
            Assert.Equal(watch2.Id, rejectedWatches.First().Id);

            Assert.Single(pendingWatches);
            Assert.Equal(watch.Id, pendingWatches.First().Id);
        }

        [Fact]
        public async Task UpdateStatusAsync_WithNonExistId_ShouldThrow()
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => this.subject.UpdateStatusAsync(Guid.NewGuid(), WatchStatus.Success, CancellationToken.None));
        }

        [Fact]
        public async Task UpdateStatusAsync_ExistWatch_ShouldSuccess()
        {
            // Arrange.
            var rule = await GenerateRuleAsync();
            var watch = new TransactionWatch<Rule>(rule, uint256.One, uint256.One);

            await this.subject.AddAsync(watch, CancellationToken.None);

            // Act.
            await this.subject.UpdateStatusAsync(watch.Id, WatchStatus.Rejected, CancellationToken.None);

            // Assert.
            var watches = await this.subject.ListAsync(WatchStatus.Rejected, CancellationToken.None);
            Assert.Single(watches);

            var updated = watches.First();
            Assert.Equal(watch.Id, updated.Id);
        }

        [Fact]
        public async Task UpdateStatusAsync_WithInvalidStatus_ShouldThrow()
        {
            // Arrange.
            var rule = await GenerateRuleAsync();
            var watch = new TransactionWatch<Rule>(rule, uint256.One, uint256.One);

            await this.subject.AddAsync(watch, CancellationToken.None);

            // Act & Assert.
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => this.subject.UpdateStatusAsync(watch.Id, WatchStatus.Pending, CancellationToken.None));
        }

        [Fact]
        public async Task UpdateStatusAsync_FinalWatchObject_ShouldThrow()
        {
            // Arrange.
            var rule = await GenerateRuleAsync();
            var watch = new TransactionWatch<Rule>(rule, uint256.One, uint256.One);

            await this.subject.AddAsync(watch, CancellationToken.None);
            await this.subject.UpdateStatusAsync(watch.Id, WatchStatus.Rejected, CancellationToken.None);

            // Act & Assert.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => this.subject.UpdateStatusAsync(watch.Id, WatchStatus.Success, CancellationToken.None));
        }

        async Task<Rule> GenerateRuleAsync()
        {
            var url = new Uri("https://zcoin.io");
            var success = new CallbackResult("success", "");
            var fail = new CallbackResult("fail", "");
            var callback = await this.callbackRepository.AddAsync(IPAddress.Loopback, url, CancellationToken.None);
            return await this.ruleRepository.AddAsync(uint256.One, 10, TimeSpan.FromHours(1), success, fail, callback, CancellationToken.None);
        }
    }
}