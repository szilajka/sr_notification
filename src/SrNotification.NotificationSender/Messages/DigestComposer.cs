using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SrNotification.Infrastructure.Email;

namespace SrNotification.NotificationSender.Messages;

/// <summary>One item in a notification message.</summary>
public sealed record DigestItem(
    string FeedName,
    string Title,
    string? Link,
    string? Summary,
    DateTimeOffset? PublishedAt,
    DateTimeOffset FetchedAt)
{
    /// <summary>The publish date from the feed, or when we found the item if the feed has none.</summary>
    public DateTimeOffset ShownTime => PublishedAt ?? FetchedAt;
}

/// <summary>
/// Builds one email or Slack message listing several new items, grouped by feed. Everything that comes
/// from a feed is untrusted: it is escaped, links must be http(s), and HTML summaries become plain text.
/// </summary>
public static partial class DigestComposer
{
    private const int SummaryLength = 280;

    // ---- Email ------------------------------------------------------------------------------------

    public static EmailMessage ComposeEmail(string to, IReadOnlyList<DigestItem> items, int maxItems, string? publicUrl)
    {
        ArgumentOutOfRangeException.ThrowIfZero(items.Count);

        var shown = items.Take(maxItems).ToList();
        var hidden = items.Count - shown.Count;
        var manageUrl = ManageUrl(publicUrl);

        var text = new StringBuilder();
        var html = new StringBuilder();
        html.Append("""<div style="font-family:Segoe UI,Helvetica,Arial,sans-serif;font-size:15px;line-height:1.5;color:#1e2a3a;max-width:640px">""");
        html.Append(CultureInfo.InvariantCulture, $"""<h1 style="font-size:20px;margin:0 0 16px">{Encode(Headline(items))}</h1>""");

        foreach (var feed in shown.GroupBy(i => i.FeedName))
        {
            text.AppendLine(feed.Key).AppendLine(new string('-', Math.Min(feed.Key.Length, 60)));
            html.Append(CultureInfo.InvariantCulture, $"""<h2 style="font-size:16px;margin:24px 0 8px;color:#5c6878">{Encode(feed.Key)}</h2>""");

            foreach (var item in feed)
            {
                var link = SafeLink(item.Link);
                var summary = ToPlainText(item.Summary, SummaryLength);
                var when = FormatUtc(item.ShownTime);

                text.AppendLine(item.Title);
                if (link is not null) text.AppendLine(link);
                text.AppendLine(CultureInfo.InvariantCulture, $"Published {when}");
                if (summary is not null) text.AppendLine(summary);
                text.AppendLine();

                html.Append("""<div style="margin:0 0 16px">""");
                html.Append(link is null
                    ? $"""<div style="font-weight:700">{Encode(item.Title)}</div>"""
                    : $"""<div style="font-weight:700"><a href="{Encode(link)}" style="color:#1e2a3a">{Encode(item.Title)}</a></div>""");
                html.Append(CultureInfo.InvariantCulture, $"""<div style="font-size:13px;color:#5c6878">Published {Encode(when)}</div>""");
                if (summary is not null)
                {
                    html.Append(CultureInfo.InvariantCulture, $"""<div style="margin-top:4px">{Encode(summary)}</div>""");
                }

                html.Append("</div>");
            }
        }

        if (hidden > 0)
        {
            var more = $"…and {hidden} more {(hidden == 1 ? "item" : "items")}.";
            text.AppendLine(more);
            html.Append(CultureInfo.InvariantCulture, $"<p>{Encode(more)}</p>");
        }

        var headers = new Dictionary<string, string>();
        if (manageUrl is not null)
        {
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"Manage your notifications: {manageUrl}");
            html.Append(CultureInfo.InvariantCulture,
                $"""<p style="font-size:13px;color:#5c6878;margin-top:32px"><a href="{Encode(manageUrl)}" style="color:#5c6878">Manage your notifications</a></p>""");
            headers["List-Unsubscribe"] = $"<{manageUrl}>";
        }

        html.Append("</div>");

        return new EmailMessage(to, Subject(items), text.ToString(), html.ToString(), headers);
    }

    /// <summary>"New: {title}" for one item, otherwise a count.</summary>
    public static string Subject(IReadOnlyList<DigestItem> items) =>
        items.Count == 1 ? $"New: {items[0].Title}" : Headline(items);

    private static string Headline(IReadOnlyList<DigestItem> items)
    {
        var feeds = items.Select(i => i.FeedName).Distinct().Count();
        var itemWord = items.Count == 1 ? "item" : "items";
        return feeds == 1
            ? $"{items.Count} new {itemWord} from {items[0].FeedName}"
            : $"{items.Count} new {itemWord} from {feeds} feeds";
    }

    // ---- Slack ------------------------------------------------------------------------------------

    /// <summary>
    /// Slack mrkdwn. Times use Slack's date formatting, so every reader sees their own time zone;
    /// the text after '|' is the fallback for clients that can't format dates.
    /// </summary>
    public static string ComposeSlack(IReadOnlyList<DigestItem> items, int maxItems, string? publicUrl)
    {
        ArgumentOutOfRangeException.ThrowIfZero(items.Count);

        var shown = items.Take(maxItems).ToList();
        var hidden = items.Count - shown.Count;
        var text = new StringBuilder();
        text.Append('*').Append(SlackEscape(Headline(items))).Append("*\n");

        foreach (var feed in shown.GroupBy(i => i.FeedName))
        {
            text.Append("\n*").Append(SlackEscape(feed.Key)).Append("*\n");
            foreach (var item in feed)
            {
                var link = SafeLink(item.Link);
                var title = SlackEscape(item.Title);
                text.Append("• ")
                    .Append(link is null ? title : $"<{SlackLink(link)}|{title}>")
                    .Append("  ·  Published ")
                    .Append(SlackDate(item.ShownTime))
                    .Append('\n');
            }
        }

        if (hidden > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n…and {hidden} more {(hidden == 1 ? "item" : "items")}.\n");
        }

        if (ManageUrl(publicUrl) is { } manageUrl)
        {
            text.Append('\n').Append('<').Append(SlackLink(manageUrl)).Append("|Manage your notifications>");
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>&lt;!date^unix^{date_short_pretty} at {time}|fallback&gt;</summary>
    internal static string SlackDate(DateTimeOffset time) =>
        $"<!date^{time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}^{{date_short_pretty}} at {{time}}|{SlackEscape(FormatUtc(time))}>";

    /// <summary>Slack requires &amp;, &lt; and &gt; to be escaped in message text.</summary>
    internal static string SlackEscape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>A URL inside &lt;url|text&gt;: '|' and '>' would end it early.</summary>
    private static string SlackLink(string url) =>
        url.Replace("|", "%7C", StringComparison.Ordinal)
           .Replace(">", "%3E", StringComparison.Ordinal)
           .Replace("<", "%3C", StringComparison.Ordinal);

    // ---- Shared -----------------------------------------------------------------------------------

    /// <summary>Only absolute http(s) links; anything else (javascript:, data:, relative) is dropped.</summary>
    internal static string? SafeLink(string? link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.AbsoluteUri
            : null;

    /// <summary>Strips tags, decodes entities, collapses whitespace and shortens.</summary>
    internal static string? ToPlainText(string? html, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = TagPattern().Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        text = WhitespacePattern().Replace(text, " ").Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "…";
    }

    private static string? ManageUrl(string? publicUrl) =>
        SafeLink(string.IsNullOrWhiteSpace(publicUrl) ? null : $"{publicUrl.TrimEnd('/')}/feeds");

    private static string FormatUtc(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("d MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
