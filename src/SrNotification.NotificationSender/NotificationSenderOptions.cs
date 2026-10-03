namespace SrNotification.NotificationSender;

public sealed class NotificationSenderOptions
{
    public const string SectionName = "NotificationSender";

    /// <summary>How often the worker looks for new items and due deliveries.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Items fanned out per database transaction.</summary>
    public int FanOutBatchSize { get; set; } = 500;

    /// <summary>
    /// Items fetched longer ago than this get no notifications (e.g. after the sender was down for days,
    /// or when the sender starts for the first time on a database full of items).
    /// </summary>
    public TimeSpan FanOutMaxAge { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Deliveries claimed per batch. One cycle keeps claiming until nothing is due.</summary>
    public int SendBatchSize { get; set; } = 200;

    /// <summary>Most items listed in one message; the rest are summarised as "and N more".</summary>
    public int MaxItemsPerMessage { get; set; } = 50;

    /// <summary>
    /// Waits between failed attempts that were stored in the database. After the last one the delivery
    /// is marked Failed, so the number of attempts is <c>RetryDelays.Count + 1</c>.
    /// Empty means <see cref="DefaultRetryDelays"/>. (The default isn't put in the list itself because
    /// the configuration binder adds configured values to an existing list instead of replacing it.)
    /// </summary>
    public List<TimeSpan> RetryDelays { get; set; } = [];

    public static IReadOnlyList<TimeSpan> DefaultRetryDelays { get; } =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
    ];

    public IReadOnlyList<TimeSpan> EffectiveRetryDelays => RetryDelays.Count > 0 ? RetryDelays : DefaultRetryDelays;

    /// <summary>How long a claimed delivery is reserved for this worker before another may take it.</summary>
    public TimeSpan ClaimDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan SentRetention { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan FailedRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>The web app's public address, for the "Manage your notifications" link. Optional.</summary>
    public string? PublicUrl { get; set; }

    public int MaxAttempts => EffectiveRetryDelays.Count + 1;
}
