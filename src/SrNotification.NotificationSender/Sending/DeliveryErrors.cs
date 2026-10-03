using System.Net;
using System.Security.Cryptography;
using MailKit.Net.Smtp;
using MimeKit;
using Polly.CircuitBreaker;
using SrNotification.Infrastructure.Email;
using SrNotification.Infrastructure.Http;

namespace SrNotification.NotificationSender.Sending;

/// <summary>Tells failures that will fix themselves (retry later) from ones that won't (fail now).</summary>
public static class DeliveryErrors
{
    /// <summary>Retrying can't help: the address or webhook is wrong, gone, or not allowed.</summary>
    public static bool IsPermanent(Exception exception) => exception switch
    {
        // The receiving server rejected the recipient for good (5xx), e.g. "mailbox does not exist".
        SmtpCommandException { ErrorCode: SmtpErrorCode.RecipientNotAccepted } smtp => (int)smtp.StatusCode >= 500,
        ParseException => true,                     // not a valid email address
        CryptographicException => true,             // stored secret can't be decrypted (keys lost)
        ArgumentException => true,                  // not a Slack webhook URL
        _ when BlockedAddressException.IsCauseOf(exception) => true,
        // Slack: 400 invalid_payload, 403 invalid_token / action_prohibited, 404 no_service / no_team,
        // 410 channel_is_archived. 429 and 5xx are worth retrying.
        HttpRequestException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound or HttpStatusCode.Gone } => true,
        _ => false,
    };

    /// <summary>Worth retrying right away (inside the Polly pipeline).</summary>
    public static bool IsTransient(Exception exception) =>
        exception is not OperationCanceledException &&
        exception is not BrokenCircuitException &&
        exception is not EmailNotConfiguredException &&
        !IsPermanent(exception);

    /// <summary>Short text for NotificationDeliveries.LastError (shown to admins).</summary>
    public static string Describe(Exception exception) => exception switch
    {
        BrokenCircuitException => "Paused after repeated failures on this channel; will retry.",
        SmtpCommandException smtp => $"SMTP server answered {(int)smtp.StatusCode}: {smtp.Message}",
        SmtpProtocolException smtp => $"SMTP protocol error: {smtp.Message}",
        _ => $"{exception.GetType().Name}: {exception.Message}",
    };
}
