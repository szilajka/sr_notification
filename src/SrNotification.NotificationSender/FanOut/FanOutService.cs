using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SrNotification.Data;
using SrNotification.Data.Entities;

namespace SrNotification.NotificationSender.FanOut;

/// <summary>
/// Step 1 of a cycle: turns new items into Pending deliveries. Each batch is one transaction that
/// inserts the deliveries and marks the items as fanned out, so a crash can't lose or duplicate them.
/// </summary>
public sealed class FanOutService(
    IDbContextFactory<SrNotificationDbContext> dbContextFactory,
    IOptions<NotificationSenderOptions> options,
    TimeProvider timeProvider,
    ILogger<FanOutService> logger)
{
    // Arbitrary constant: only one sender instance fans out at a time.
    private const long AdvisoryLockKey = 0x5352_4E46_414E; // "SRNFAN"

    private readonly NotificationSenderOptions _options = options.Value;

    /// <returns>Number of deliveries created.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var (itemCount, created) = await FanOutBatchAsync(cancellationToken);
            total += created;
            if (itemCount < _options.FanOutBatchSize)
            {
                break;
            }
        }

        return total;
    }

    private async Task<(int Items, int Deliveries)> FanOutBatchAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // EnableRetryOnFailure requires explicit transactions to run inside the execution strategy.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var gotLock = await db.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({AdvisoryLockKey}) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!gotLock)
            {
                return (0, 0); // another instance is fanning out right now
            }

            var items = await db.Items
                .Where(i => i.FannedOutAt == null)
                .OrderBy(i => i.Id)
                .Take(_options.FanOutBatchSize)
                .Select(i => new FanOutItem(i.Id, i.FeedId, i.FetchedAt, i.IsFromInitialFetch))
                .ToListAsync(cancellationToken);

            if (items.Count == 0)
            {
                return (0, 0);
            }

            var now = timeProvider.GetUtcNow();
            var enabledChannels = (await db.ChannelSettings
                    .Where(c => c.IsEnabled)
                    .Select(c => c.Channel)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            var feedIds = items.Select(i => i.FeedId).Distinct().ToList();
            var subscriptions = await db.Subscriptions
                .Where(s => feedIds.Contains(s.FeedId) && (s.EmailEnabled || s.SlackEnabled))
                .Select(s => new FanOutSubscription(
                    s.UserId,
                    s.FeedId,
                    s.EmailEnabled,
                    s.SlackEnabled,
                    s.FollowingSince,
                    (s.User.NotificationEmail ?? s.User.Email) != null,
                    s.User.SlackWebhookUrlProtected != null))
                .ToListAsync(cancellationToken);

            var planned = FanOutPlanner.Plan(items, subscriptions, enabledChannels, now - _options.FanOutMaxAge);

            db.NotificationDeliveries.AddRange(planned.Select(p => new NotificationDelivery
            {
                RssItemId = p.ItemId,
                UserId = p.UserId,
                Channel = p.Channel,
                Status = DeliveryStatus.Pending,
                CreatedAt = now,
                NextAttemptAt = now,
            }));
            await db.SaveChangesAsync(cancellationToken);

            var itemIds = items.Select(i => i.Id).ToList();
            await db.Items
                .Where(i => itemIds.Contains(i.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.FannedOutAt, now), cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            if (planned.Count > 0)
            {
                logger.LogInformation("Fanned out {Items} item(s) into {Deliveries} delivery(ies)", items.Count, planned.Count);
            }

            return (items.Count, planned.Count);
        });
    }
}
