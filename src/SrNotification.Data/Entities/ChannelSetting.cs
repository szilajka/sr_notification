namespace SrNotification.Data.Entities;

/// <summary>App-wide switch for a notification channel, controlled by admins.</summary>
public class ChannelSetting
{
    public NotificationChannel Channel { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Email of the admin who changed it last.</summary>
    public string? UpdatedBy { get; set; }
}
