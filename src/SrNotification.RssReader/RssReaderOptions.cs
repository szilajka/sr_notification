namespace SrNotification.RssReader;

public sealed class RssReaderOptions
{
    public const string SectionName = "RssReader";

    /// <summary>How often the worker wakes up to look for feeds that are due.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How long after its last check a feed is refreshed again.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Upper bound of the back-off applied to feeds that keep failing.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How many feeds are fetched at the same time.</summary>
    public int MaxParallelFeeds { get; set; } = 4;

    /// <summary>Feeds larger than this are rejected.</summary>
    public long MaxFeedSizeBytes { get; set; } = 10 * 1024 * 1024;

    public string UserAgent { get; set; } = "SrNotification-RssReader/1.0";

    /// <summary>
    /// Feed URLs inserted on startup if missing. Meant for local development until the Web UI
    /// (which normally adds feeds) exists.
    /// </summary>
    public List<string> SeedFeeds { get; set; } = [];
}
