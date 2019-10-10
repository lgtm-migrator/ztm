using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using NSubstitute;
using Xunit;
using Ztm.Zcoin.NBitcoin;
using Ztm.Zcoin.Synchronization.Watchers;

namespace Ztm.Zcoin.Synchronization.Tests.Watchers
{
    public sealed class WatcherTests : IDisposable
    {
        readonly IWatcherStorage<Watch> storage;
        readonly TestWatcher subject;

        public WatcherTests()
        {
            this.storage = Substitute.For<IWatcherStorage<Watch>>();
            this.subject = new TestWatcher(this.storage);

            try
            {
                this.subject.CreateWatches = Substitute.For<Func<ZcoinBlock, int, CancellationToken, IEnumerable<Watch>>>();
                this.subject.ExecuteMatchedWatch = Substitute.For<Func<Watch, ZcoinBlock, int, BlockEventType, CancellationToken, bool>>();
                this.subject.GetWatches = Substitute.For<Func<ZcoinBlock, int, CancellationToken, IEnumerable<Watch>>>();
            }
            catch
            {
                this.subject.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            this.subject.Dispose();
        }

        [Fact]
        public void Constructor_WithNullStorage_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>("storage", () => new TestWatcher(null));
        }

        [Fact]
        public async Task StartAsync_WhenSuccess_ShouldStartStorage()
        {
            using (var cancellationSource = new CancellationTokenSource())
            {
                await this.subject.StartAsync(cancellationSource.Token);

                _ = this.storage.Received(1).StartAsync(cancellationSource.Token);
            }
        }

        [Fact]
        public async Task StopAsync_WhenSuccess_ShouldStopStorage()
        {
            // Arrange.
            await this.subject.StartAsync(CancellationToken.None);

            // Act.
            using (var cancellationSource = new CancellationTokenSource())
            {
                await this.storage.StopAsync(cancellationSource.Token);

                // Assert.
                _ = this.storage.Received(1).StopAsync(cancellationSource.Token);
            }
        }

        [Fact]
        public async Task BlockAddedAsync_WhenInvoke_ShouldInvokeCreateWatchesAsync()
        {
            // Arrange.
            var block = (ZcoinBlock)ZcoinNetworks.Instance.Regtest.GetGenesis();

            this.subject.CreateWatches(Arg.Any<ZcoinBlock>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<Watch>());
            this.subject.GetWatches(Arg.Any<ZcoinBlock>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<Watch>());

            // Act.
            using (var cancellationSource = new CancellationTokenSource())
            {
                await this.subject.BlockAddedAsync(block, 0, cancellationSource.Token);

                // Assert.
                this.subject.CreateWatches.Received(1)(block, 0, cancellationSource.Token);
            }
        }

        [Fact]
        public async Task BlockAddedAsync_CreateWatchesAsyncReturnEmptyList_ShouldNotAddToStorage()
        {
            // Arrange.
            var block = (ZcoinBlock)ZcoinNetworks.Instance.Regtest.GetGenesis();

            this.subject.CreateWatches(block, 0, Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<Watch>());
            this.subject.GetWatches(Arg.Any<ZcoinBlock>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<Watch>());

            // Act.
            await this.subject.BlockAddedAsync(block, 0, CancellationToken.None);

            // Assert.
            _ = this.storage.Received(0).AddWatchesAsync(Arg.Any<IEnumerable<Watch>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task BlockAddedAsync_CreateWatchesAsyncReturnNonEmptyList_ShouldAddToStorage()
        {
            // Arrange.
            var block = (ZcoinBlock)ZcoinNetworks.Instance.Regtest.GetGenesis();
            var watch = new Watch(block.GetHash());

            this.subject.CreateWatches(block, 0, Arg.Any<CancellationToken>()).Returns(new[] { watch });
            this.subject.GetWatches(Arg.Any<ZcoinBlock>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<Watch>());

            // Act.
            using (var cancellationSource = new CancellationTokenSource())
            {
                await this.subject.BlockAddedAsync(block, 0, cancellationSource.Token);

                // Assert.
                _ = this.storage.Received(1).AddWatchesAsync(
                    Arg.Is<IEnumerable<Watch>>(l => l.SequenceEqual(new[] { watch })),
                    CancellationToken.None
                );
            }
        }

        [Fact]
        public async Task BlockAddedAsync_GetWatchesAsyncReturnEmptyList_ShouldNotCallExecuteMatchedWatchAsync()
        {
            // Arrange.
            var block = (ZcoinBlock)ZcoinNetworks.Instance.Regtest.GetGenesis();

            this.subject.CreateWatches(Arg.Any<ZcoinBlock>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<Watch>());
            this.subject.GetWatches(Arg.Any<ZcoinBlock>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<Watch>());

            // Act.
            using (var cancellationSource = new CancellationTokenSource())
            {
                await this.subject.BlockAddedAsync(block, 0, cancellationSource.Token);

                // Assert.
                this.subject.GetWatches.Received(1)(block, 0, cancellationSource.Token);
                this.subject.ExecuteMatchedWatch.Received(0)(
                    Arg.Any<Watch>(),
                    Arg.Any<ZcoinBlock>(),
                    Arg.Any<int>(),
                    Arg.Any<BlockEventType>(),
                    Arg.Any<CancellationToken>()
                );
            }
        }

        [Fact]
        public async Task BlockAddedAsync_ExecuteMatchedWatchAsyncReturnTrue_ShouldRemoveThatWatch()
        {
            // Arrange.
            var block = (ZcoinBlock)ZcoinNetworks.Instance.Regtest.GetGenesis();
            var watch = new Watch(block.GetHash());

            this.subject.CreateWatches(Arg.Any<ZcoinBlock>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<Watch>());
            this.subject.GetWatches(block, 0, Arg.Any<CancellationToken>()).Returns(new[] { watch });
            this.subject.ExecuteMatchedWatch(watch, block, 0, BlockEventType.Added, Arg.Any<CancellationToken>()).Returns(true);

            // Act.
            using (var cancellationSource = new CancellationTokenSource())
            {
                await this.subject.BlockAddedAsync(block, 0, cancellationSource.Token);

                // Assert.
                this.subject.GetWatches.Received(1)(block, 0, cancellationSource.Token);
                this.subject.ExecuteMatchedWatch.Received(1)(watch, block, 0, BlockEventType.Added, CancellationToken.None);

                _ = this.storage.Received(1).RemoveWatchesAsync(
                    Arg.Is<IEnumerable<WatchToRemove<Watch>>>(l => l.SequenceEqual(new[]
                    {
                        new WatchToRemove<Watch>(watch, WatchRemoveReason.Completed)
                    })),
                    CancellationToken.None
                );
            }
        }

        [Fact]
        public async Task BlockAddedAsync_ExecuteMatchedWatchAsyncReturnFalse_ShouldKeepThatWatch()
        {
            // Arrange.
            var block = (ZcoinBlock)ZcoinNetworks.Instance.Regtest.GetGenesis();
            var watch = new Watch(block.GetHash());

            this.subject.CreateWatches(Arg.Any<ZcoinBlock>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<Watch>());
            this.subject.GetWatches(block, 0, Arg.Any<CancellationToken>()).Returns(new[] { watch });
            this.subject.ExecuteMatchedWatch(watch, block, 0, BlockEventType.Added, Arg.Any<CancellationToken>()).Returns(false);

            // Act.
            using (var cancellationSource = new CancellationTokenSource())
            {
                await this.subject.BlockAddedAsync(block, 0, cancellationSource.Token);

                // Assert.
                this.subject.GetWatches.Received(1)(block, 0, cancellationSource.Token);
                this.subject.ExecuteMatchedWatch.Received(1)(watch, block, 0, BlockEventType.Added, CancellationToken.None);

                _ = this.storage.Received(0).RemoveWatchesAsync(
                    Arg.Any<IEnumerable<WatchToRemove<Watch>>>(),
                    Arg.Any<CancellationToken>()
                );
            }
        }

        [Fact]
        public async Task BlockRemovingAsync_GetWatchesAsyncReturnEmptyList_ShouldNotCallExecuteMatchedWatchAsync()
        {
            // Arrange.
            var block = (ZcoinBlock)ZcoinNetworks.Instance.Regtest.GetGenesis();

            this.subject.GetWatches(block, 0, Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<Watch>());

            // Act.
            using (var cancellationSource = new CancellationTokenSource())
            {
                await this.subject.BlockRemovingAsync(block, 0, cancellationSource.Token);

                // Assert.
                this.subject.GetWatches.Received(1)(block, 0, cancellationSource.Token);
                this.subject.ExecuteMatchedWatch.Received(0)(
                    Arg.Any<Watch>(),
                    Arg.Any<ZcoinBlock>(),
                    Arg.Any<int>(),
                    Arg.Any<BlockEventType>(),
                    Arg.Any<CancellationToken>()
                );

                _ = this.storage.Received(0).RemoveWatchesAsync(
                    Arg.Any<IEnumerable<WatchToRemove<Watch>>>(),
                    Arg.Any<CancellationToken>()
                );
            }
        }

        [Fact]
        public async Task BlockRemovingAsync_ExecuteMatchedWatchAsyncReturnTrue_ShouldRemoveThatWatch()
        {
            // Arrange.
            var block = (ZcoinBlock)ZcoinNetworks.Instance.Regtest.GetGenesis();
            var watch = new Watch(uint256.One);

            this.subject.GetWatches(block, 1, Arg.Any<CancellationToken>()).Returns(new[] { watch });
            this.subject.ExecuteMatchedWatch(watch, block, 1, BlockEventType.Removing, Arg.Any<CancellationToken>()).Returns(true);

            // Act.
            using (var cancellationSource = new CancellationTokenSource())
            {
                await this.subject.BlockRemovingAsync(block, 1, cancellationSource.Token);

                // Assert.
                this.subject.GetWatches.Received(1)(block, 1, cancellationSource.Token);
                this.subject.ExecuteMatchedWatch.Received(1)(watch, block, 1, BlockEventType.Removing, CancellationToken.None);

                _ = this.storage.Received(1).RemoveWatchesAsync(
                    Arg.Is<IEnumerable<WatchToRemove<Watch>>>(l => l.SequenceEqual(new[]
                    {
                        new WatchToRemove<Watch>(watch, WatchRemoveReason.Completed)
                    })),
                    CancellationToken.None
                );
            }
        }

        [Fact]
        public async Task BlockRemovingAsync_ExecuteMatchedWatchAsyncReturnFalse_ShouldKeepThatWatch()
        {
            // Arrange.
            var block = (ZcoinBlock)ZcoinNetworks.Instance.Regtest.GetGenesis();
            var watch = new Watch(uint256.One);

            this.subject.GetWatches(block, 1, Arg.Any<CancellationToken>()).Returns(new[] { watch });
            this.subject.ExecuteMatchedWatch(watch, block, 1, BlockEventType.Removing, Arg.Any<CancellationToken>()).Returns(false);

            // Act.
            using (var cancellationSource = new CancellationTokenSource())
            {
                await this.subject.BlockRemovingAsync(block, 1, cancellationSource.Token);

                // Assert.
                this.subject.GetWatches.Received(1)(block, 1, cancellationSource.Token);
                this.subject.ExecuteMatchedWatch.Received(1)(watch, block, 1, BlockEventType.Removing, CancellationToken.None);

                _ = this.storage.Received(0).RemoveWatchesAsync(
                    Arg.Any<IEnumerable<WatchToRemove<Watch>>>(),
                    Arg.Any<CancellationToken>()
                );
            }
        }

        [Fact]
        public async Task BlockRemovingAsync_WatchingOnRemovingBlock_ShouldRemoveThatWatch()
        {
            // Arrange.
            var block = (ZcoinBlock)ZcoinNetworks.Instance.Regtest.GetGenesis();
            var watch = new Watch(block.GetHash());

            this.subject.GetWatches(block, 0, Arg.Any<CancellationToken>()).Returns(new[] { watch });
            this.subject.ExecuteMatchedWatch(Arg.Any<Watch>(), Arg.Any<ZcoinBlock>(), Arg.Any<int>(), Arg.Any<BlockEventType>(), Arg.Any<CancellationToken>()).Returns(false);

            // Act.
            using (var cancellationSource = new CancellationTokenSource())
            {
                await this.subject.BlockRemovingAsync(block, 0, cancellationSource.Token);

                // Assert.
                this.subject.GetWatches.Received(1)(block, 0, cancellationSource.Token);
                this.subject.ExecuteMatchedWatch.Received(1)(watch, block, 0, BlockEventType.Removing, CancellationToken.None);

                _ = this.storage.Received(1).RemoveWatchesAsync(
                    Arg.Is<IEnumerable<WatchToRemove<Watch>>>(l => l.SequenceEqual(new[]
                    {
                        new WatchToRemove<Watch>(watch, WatchRemoveReason.BlockRemoved)
                    })),
                    CancellationToken.None
                );
            }
        }
    }
}
