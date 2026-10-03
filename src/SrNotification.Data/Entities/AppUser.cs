namespace SrNotification.Data.Entities;

/// <summary>
/// A person who signed in through the identity provider (Entra External ID). There are no passwords
/// here: the row is created on first sign-in and keyed by the provider's user id.
/// </summary>
public class AppUser
{
    public int Id { get; set; }

    /// <summary>The identity provider's id for the user (Entra 'oid', or 'sub' for other providers).</summary>
    public required string ExternalId { get; set; }

    /// <summary>Email address of the sign-in account, refreshed on every sign-in. Null if the provider didn't send one.</summary>
    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>
    /// A confirmed address the user chose for notifications instead of their sign-in address.
    /// Null means "use <see cref="Email"/>".
    /// </summary>
    public string? NotificationEmail { get; set; }

    /// <summary>An address the user asked to switch to and hasn't confirmed yet.</summary>
    public string? PendingNotificationEmail { get; set; }

    /// <summary>SHA-256 (hex) of the confirmation token mailed to <see cref="PendingNotificationEmail"/>.</summary>
    public string? PendingEmailTokenHash { get; set; }

    public DateTimeOffset? PendingEmailExpiresAt { get; set; }

    /// <summary>The user's Slack incoming-webhook URL, encrypted with ASP.NET Core Data Protection.</summary>
    public string? SlackWebhookUrlProtected { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSignInAt { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = [];

    /// <summary>Where email notifications go. Not mapped to a column.</summary>
    public string? EffectiveNotificationEmail => NotificationEmail ?? Email;
}
