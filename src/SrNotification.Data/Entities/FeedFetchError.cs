namespace SrNotification.Data.Entities;

/// <summary>One failed attempt of the RSS reader to fetch a feed. Shown in the admin error log.</summary>
public class FeedFetchError
{
    public long Id { get; set; }

    public int FeedId { get; set; }
    public RssFeed Feed { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; }

    public required string Message { get; set; }
}
