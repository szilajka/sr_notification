namespace SrNotification.Data.Entities;

public enum DeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,

    /// <summary>Not sent on purpose: the user or an admin switched the channel off before it went out.</summary>
    Skipped = 3,
}

/// <summary>
/// One notification of one item to one user over one channel. Filled by the notification sender
/// (part 3); failed rows are the admin's notification error log. (RssItemId, UserId, Channel) is
/// unique, so an item can never be sent twice to the same user on the same channel.
/// </summary>
public class NotificationDelivery
{
    public long Id { get; set; }

    public long RssItemId { get; set; }
    public RssItem RssItem { get; set; } = null!;

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public NotificationChannel Channel { get; set; }

    public DeliveryStatus Status { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>
    /// When the sender may pick the row up (again). Set to now on creation, pushed back by the retry
    /// schedule after a failure, and briefly into the future while a sender works on the row.
    /// </summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
