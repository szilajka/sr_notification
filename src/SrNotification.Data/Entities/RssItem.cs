namespace SrNotification.Data.Entities;

/// <summary>
/// One item (article) read from a feed. (FeedId, ExternalId) is unique, which is how
/// the reader tells new items from ones it has already stored.
/// </summary>
public class RssItem
{
    public long Id { get; set; }

    public int FeedId { get; set; }
    public RssFeed Feed { get; set; } = null!;

    /// <summary>
    /// Stable identifier of the item inside its feed: the RSS guid / Atom id, else the link,
    /// else a hash of the title and summary.
    /// </summary>
    public required string ExternalId { get; set; }

    public required string Title { get; set; }
    public string? Link { get; set; }
    public string? Summary { get; set; }
    public string? Author { get; set; }

    /// <summary>Publish date from the feed, in UTC (null when the feed has none).</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>When the reader stored the item.</summary>
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>
    /// True for items stored by the very first successful fetch of a feed (the feed's backlog).
    /// The notification sender can skip these so a newly added feed doesn't flood users.
    /// </summary>
    public bool IsFromInitialFetch { get; set; }
}
