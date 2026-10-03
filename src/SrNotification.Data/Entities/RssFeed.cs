namespace SrNotification.Data.Entities;

/// <summary>
/// An RSS/Atom feed URL that the RSS reader polls.
/// Rows are created by the Web API when a user follows a URL nobody followed before, and are shared
/// by every user who follows that URL. The reader only updates the fetch bookkeeping columns.
/// </summary>
public class RssFeed
{
    public int Id { get; set; }

    /// <summary>The feed URL. Unique across the table.</summary>
    public required string Url { get; set; }

    /// <summary>Title taken from the feed itself (filled in on first successful fetch).</summary>
    public string? Title { get; set; }

    /// <summary>The website the feed belongs to (the feed's alternate link).</summary>
    public string? SiteUrl { get; set; }

    /// <summary>Inactive feeds are skipped by the reader.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the reader last tried to fetch the feed (successfully or not).</summary>
    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>When the reader last fetched the feed successfully.</summary>
    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>Error message of the last failed fetch; null after a successful one.</summary>
    public string? LastError { get; set; }

    /// <summary>Number of failed fetches in a row; drives the back-off.</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>ETag returned by the server, sent back as If-None-Match.</summary>
    public string? ETag { get; set; }

    /// <summary>Last-Modified returned by the server, sent back as If-Modified-Since.</summary>
    public DateTimeOffset? LastModified { get; set; }

    public ICollection<RssItem> Items { get; set; } = [];

    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
