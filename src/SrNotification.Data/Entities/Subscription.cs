namespace SrNotification.Data.Entities;

/// <summary>A user following a feed, with that feed's notification switches.</summary>
public class Subscription
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public int FeedId { get; set; }
    public RssFeed Feed { get; set; } = null!;

    /// <summary>Optional name the user gave the feed; the UI falls back to the feed's own title.</summary>
    public string? Name { get; set; }

    public bool EmailEnabled { get; set; }

    public bool SlackEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the subscription started following its current feed (reset when the user changes the URL).
    /// The notification sender only sends items fetched after this moment, so a user never gets a
    /// feed's older items.
    /// </summary>
    public DateTimeOffset FollowingSince { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
