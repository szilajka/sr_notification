using SrNotification.Data.Entities;

namespace SrNotification.Api.Endpoints;

// ---- Me -------------------------------------------------------------------------------------------

public sealed record MeResponse(int Id, string? Email, string? DisplayName, bool IsAdmin);

public sealed record NotificationSettingsResponse(EmailSettingsResponse Email, SlackSettingsResponse Slack);

/// <param name="Address">Where email notifications go now.</param>
/// <param name="SignInAddress">The sign-in account's address (the default).</param>
/// <param name="IsCustom">True when <paramref name="Address"/> is a confirmed address the user chose.</param>
/// <param name="PendingAddress">An address waiting for the user to click the confirmation link.</param>
/// <param name="EnabledForApp">False when an admin has turned email off for everyone.</param>
public sealed record EmailSettingsResponse(
    string? Address, string? SignInAddress, bool IsCustom, string? PendingAddress, bool EnabledForApp);

/// <param name="WebhookHint">Masked webhook URL, e.g. https://hooks.slack.com/services/…a1b2.</param>
public sealed record SlackSettingsResponse(bool IsConfigured, string? WebhookHint, bool EnabledForApp);

/// <param name="Address">Empty or the sign-in address: go back to the sign-in address.</param>
public sealed record UpdateEmailRequest(string? Address);

public sealed record ConfirmEmailRequest(string? Token);

public sealed record ConfirmEmailResponse(string Address);

/// <param name="WebhookUrl">Empty: remove the webhook.</param>
public sealed record UpdateSlackRequest(string? WebhookUrl);

// ---- Subscriptions --------------------------------------------------------------------------------

public sealed record SubscriptionResponse(
    int Id,
    string Url,
    string? Name,
    string? FeedTitle,
    string? SiteUrl,
    bool EmailEnabled,
    bool SlackEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSuccessAt,
    bool IsFailing);

public sealed record SubscriptionRequest(string? Url, string? Name, bool EmailEnabled, bool SlackEnabled);

// ---- Admin ----------------------------------------------------------------------------------------

public sealed record ChannelSettingResponse(
    NotificationChannel Channel, bool IsEnabled, DateTimeOffset? UpdatedAt, string? UpdatedBy);

public sealed record UpdateChannelRequest(bool IsEnabled);

public sealed record SmtpSettingsResponse(
    bool IsConfigured,
    string? Host,
    int Port,
    SmtpSecurity Security,
    string? Username,
    bool HasPassword,
    string? FromAddress,
    string? FromName,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);

/// <param name="Password">Leave empty to keep the stored password.</param>
/// <param name="ClearPassword">Remove the stored password (for servers without authentication).</param>
public sealed record UpdateSmtpRequest(
    string? Host,
    int Port,
    SmtpSecurity Security,
    string? Username,
    string? Password,
    bool ClearPassword,
    string? FromAddress,
    string? FromName);

/// <param name="To">Defaults to the admin's own address.</param>
public sealed record SendTestEmailRequest(string? To);

public sealed record AdminFeedResponse(
    int Id,
    string Url,
    string? Title,
    bool IsActive,
    int SubscriberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSuccessAt,
    int ConsecutiveFailures,
    string? LastError);

public sealed record FeedErrorLogEntry(
    long Id, int FeedId, string FeedUrl, string? FeedTitle, DateTimeOffset OccurredAt, string Message);

public sealed record NotificationErrorLogEntry(
    long Id,
    NotificationChannel Channel,
    DeliveryStatus Status,
    int Attempts,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt,
    int UserId,
    string? UserEmail,
    long ItemId,
    string ItemTitle,
    string FeedUrl);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
