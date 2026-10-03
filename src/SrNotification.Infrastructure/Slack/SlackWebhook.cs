using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace SrNotification.Infrastructure.Slack;

/// <summary>Rules for Slack incoming-webhook URLs.</summary>
public static partial class SlackWebhook
{
    // https://hooks.slack.com/services/T000/B000/XXXX
    [GeneratedRegex(@"^https://hooks\.slack\.com/services/[A-Za-z0-9]+/[A-Za-z0-9]+/[A-Za-z0-9]+$")]
    private static partial Regex WebhookPattern();

    public static bool IsValid(string? url) => url is not null && WebhookPattern().IsMatch(url);

    /// <summary>A recognisable but harmless form of the URL to show in the UI: the last 4 characters.</summary>
    public static string Mask(string url) =>
        url.Length <= 4 ? "…" : $"https://hooks.slack.com/services/…{url[^4..]}";
}

/// <summary>Posts messages to a Slack incoming webhook.</summary>
public sealed class SlackWebhookClient(HttpClient httpClient)
{
    public async Task SendAsync(string webhookUrl, string text, CancellationToken cancellationToken)
    {
        if (!SlackWebhook.IsValid(webhookUrl))
        {
            throw new ArgumentException("Not a Slack incoming-webhook URL.", nameof(webhookUrl));
        }

        using var response = await httpClient.PostAsJsonAsync(webhookUrl, new { text }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Slack answers with short plain-text reasons such as "no_service" or "channel_is_archived".
            var reason = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Slack rejected the message ({(int)response.StatusCode}): {Truncate(reason, 200)}",
                inner: null,
                statusCode: response.StatusCode);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
