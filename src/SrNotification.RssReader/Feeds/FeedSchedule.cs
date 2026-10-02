namespace SrNotification.RssReader.Feeds;

/// <summary>Decides whether a feed is due for a refresh.</summary>
public static class FeedSchedule
{
    /// <summary>
    /// Ticks never line up exactly with the refresh interval; without some slack a feed checked
    /// at 10:00:00.5 would be skipped at 10:05:00.4 and wait a whole extra poll interval.
    /// </summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(5);

    public static bool IsDue(
        DateTimeOffset? lastCheckedAt, int consecutiveFailures, DateTimeOffset now, RssReaderOptions options)
    {
        if (lastCheckedAt is null)
        {
            return true; // never fetched: new feed
        }

        return lastCheckedAt.Value + GetWait(consecutiveFailures, options) <= now + Tolerance;
    }

    /// <summary>The normal interval, doubled per consecutive failure, capped at MaxBackoff.</summary>
    public static TimeSpan GetWait(int consecutiveFailures, RssReaderOptions options)
    {
        if (consecutiveFailures <= 0)
        {
            return options.RefreshInterval;
        }

        var factor = Math.Pow(2, Math.Min(consecutiveFailures, 16));
        var wait = options.RefreshInterval * factor;
        var cap = options.MaxBackoff > options.RefreshInterval ? options.MaxBackoff : options.RefreshInterval;
        return wait < cap ? wait : cap;
    }
}
