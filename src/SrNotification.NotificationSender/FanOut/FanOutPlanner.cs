using SrNotification.Data.Entities;

namespace SrNotification.NotificationSender.FanOut;

public sealed record FanOutItem(long Id, int FeedId, DateTimeOffset FetchedAt, bool IsFromInitialFetch);

public sealed record FanOutSubscription(
    int UserId,
    int FeedId,
    bool EmailEnabled,
    bool SlackEnabled,
    DateTimeOffset FollowingSince,
    bool HasEmailAddress,
    bool HasSlackWebhook);

public sealed record PlannedDelivery(long ItemId, int UserId, NotificationChannel Channel);

/// <summary>Decides who gets notified about which item. Pure logic, so it is easy to test.</summary>
public static class FanOutPlanner
{
    public static IReadOnlyList<PlannedDelivery> Plan(
        IEnumerable<FanOutItem> items,
        IEnumerable<FanOutSubscription> subscriptions,
        IReadOnlySet<NotificationChannel> enabledChannels,
        DateTimeOffset oldestItemToNotify)
    {
        var byFeed = subscriptions.ToLookup(s => s.FeedId);
        var planned = new List<PlannedDelivery>();

        foreach (var item in items)
        {
            // A new feed's backlog, or items too old to be news (sender was down, first start).
            if (item.IsFromInitialFetch || item.FetchedAt < oldestItemToNotify)
            {
                continue;
            }

            foreach (var subscription in byFeed[item.FeedId])
            {
                // Items the feed had before the user started following it (or switched to it).
                if (item.FetchedAt < subscription.FollowingSince)
                {
                    continue;
                }

                if (subscription.EmailEnabled && subscription.HasEmailAddress &&
                    enabledChannels.Contains(NotificationChannel.Email))
                {
                    planned.Add(new PlannedDelivery(item.Id, subscription.UserId, NotificationChannel.Email));
                }

                if (subscription.SlackEnabled && subscription.HasSlackWebhook &&
                    enabledChannels.Contains(NotificationChannel.Slack))
                {
                    planned.Add(new PlannedDelivery(item.Id, subscription.UserId, NotificationChannel.Slack));
                }
            }
        }

        return planned;
    }
}
