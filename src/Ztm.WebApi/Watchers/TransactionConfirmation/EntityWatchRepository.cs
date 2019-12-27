using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Ztm.Data.Entity.Contexts;
using Ztm.Data.Entity.Contexts.Main;
using Ztm.Zcoin.Watching;

namespace Ztm.WebApi.Watchers.TransactionConfirmation
{
    public sealed class EntityWatchRepository : IWatchRepository
    {
        readonly IMainDatabaseFactory db;
        readonly JsonSerializer serializer;

        public EntityWatchRepository(IMainDatabaseFactory db, JsonSerializer serializer)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (serializer == null)
            {
                throw new ArgumentNullException(nameof(serializer));
            }

            this.db = db;
            this.serializer = serializer;
        }

        public async Task AddAsync(TransactionWatch<Rule> watch, CancellationToken cancellationToken)
        {
            if (watch == null)
            {
                throw new ArgumentNullException(nameof(watch));
            }

            if (watch.Context == null)
            {
                throw new ArgumentException("Watch does not contain context.", nameof(watch));
            }

            using (var db = this.db.CreateDbContext())
            {
                await db.TransactionConfirmationWatcherWatches.AddAsync
                (
                    new TransactionConfirmationWatcherWatch
                    {
                        Id = watch.Id,
                        RuleId = watch.Context.Id,
                        StartBlockHash = watch.StartBlock,
                        StartTime = watch.StartTime,
                        TransactionHash = watch.TransactionId,
                        Status = (int)WatchStatus.Pending,
                    }
                );

                await db.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<IEnumerable<TransactionWatch<Rule>>> ListAsync(WatchStatus status, CancellationToken cancellationToken)
        {
            var statusVal = (int)status;

            using (var db = this.db.CreateDbContext())
            {
                return await db.TransactionConfirmationWatcherWatches
                    .Include(w => w.Rule)
                    .ThenInclude(r => r.Callback)
                    .Where(w => statusVal == w.Status)
                    .Select(w => ToDomain(w))
                    .ToListAsync(cancellationToken);
            }
        }

        public async Task UpdateStatusAsync(Guid id, WatchStatus status, CancellationToken cancellationToken)
        {
            using (var db = this.db.CreateDbContext())
            using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken))
            {
                var watch = await db.TransactionConfirmationWatcherWatches
                    .Where(w => w.Id == id).FirstOrDefaultAsync(cancellationToken);

                if (watch == null)
                {
                    throw new KeyNotFoundException("Watch id is not found.");
                }

                if (watch.Status != (int)WatchStatus.Pending)
                {
                    throw new InvalidOperationException("The watch is not be able to update.");
                }

                switch (status)
                {
                    case WatchStatus.Rejected:
                    case WatchStatus.Success:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(status), "New status is not allowed to set.");
                }

                watch.Status = (int)status;
                await db.SaveChangesAsync(cancellationToken);

                tx.Commit();
            }
        }

        TransactionWatch<Rule> ToDomain(TransactionConfirmationWatcherWatch watch)
        {
            return new TransactionWatch<Rule>
            (
                EntityRuleRepository.ToDomain(this.serializer, watch.Rule),
                watch.StartBlockHash,
                watch.TransactionHash,
                watch.StartTime,
                watch.Id
            );
        }
    }
}