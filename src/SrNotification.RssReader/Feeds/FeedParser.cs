using System.Globalization;
using System.Security.Cryptography;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using SrNotification.Data;

namespace SrNotification.RssReader.Feeds;

/// <summary>Turns an RSS 2.0 or Atom 1.0 document into a <see cref="ParsedFeed"/>.</summary>
public static class FeedParser
{
    private const int MaxSummaryLength = 20_000;

    public static ParsedFeed Parse(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore, // some feeds still carry a DOCTYPE; never resolve it
            IgnoreComments = true,
        };

        using var reader = XmlReader.Create(stream, settings);
        reader.MoveToContent();

        SyndicationFeedFormatter formatter = new Rss20FeedFormatter { DateTimeParser = TryParseDate };
        if (!formatter.CanRead(reader))
        {
            formatter = new Atom10FeedFormatter { DateTimeParser = TryParseDate };
            if (!formatter.CanRead(reader))
            {
                throw new FormatException(
                    $"The document is not an RSS 2.0 or Atom 1.0 feed (root element: <{reader.LocalName}>).");
            }
        }

        formatter.ReadFrom(reader);
        var feed = formatter.Feed;

        var items = feed.Items
            .Select(ToParsedItem)
            .DistinctBy(i => i.ExternalId) // a feed occasionally repeats an item
            .ToList();

        return new ParsedFeed(
            Title: Truncate(feed.Title?.Text?.Trim(), FieldLengths.Title),
            SiteUrl: Truncate(GetAlternateLink(feed.Links), FieldLengths.Url),
            Items: items);
    }

    private static ParsedItem ToParsedItem(SyndicationItem item)
    {
        var link = GetAlternateLink(item.Links);
        var title = item.Title?.Text?.Trim();
        var summary = item.Summary?.Text ?? (item.Content as TextSyndicationContent)?.Text;
        var author = item.Authors.Select(a => a.Name ?? a.Email).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        var published = item.PublishDate != default ? item.PublishDate
            : item.LastUpdatedTime != default ? item.LastUpdatedTime
            : (DateTimeOffset?)null;

        return new ParsedItem(
            ExternalId: BuildExternalId(item.Id, link, title, summary),
            Title: Truncate(string.IsNullOrWhiteSpace(title) ? "(untitled)" : title, FieldLengths.Title)!,
            Link: Truncate(link, FieldLengths.Url),
            Summary: Truncate(summary?.Trim(), MaxSummaryLength),
            Author: Truncate(author?.Trim(), FieldLengths.Author),
            PublishedAt: published?.ToUniversalTime());
    }

    /// <summary>
    /// guid/id → link → hash of title+summary. Over-long values are hashed so they fit the column
    /// and still identify the item.
    /// </summary>
    internal static string BuildExternalId(string? id, string? link, string? title, string? summary)
    {
        var candidate = !string.IsNullOrWhiteSpace(id) ? id.Trim()
            : !string.IsNullOrWhiteSpace(link) ? link.Trim()
            : null;

        if (candidate is null)
        {
            return "sha256:" + Sha256($"{title}\n{summary}");
        }

        return candidate.Length <= FieldLengths.ExternalId ? candidate : "sha256:" + Sha256(candidate);
    }

    private static string? GetAlternateLink(IEnumerable<SyndicationLink> links)
    {
        var link = links.FirstOrDefault(l => l.RelationshipType is null or "alternate") ?? links.FirstOrDefault();
        if (link is null)
        {
            return null;
        }

        var uri = link.GetAbsoluteUri() ?? link.Uri;
        return uri?.ToString();
    }

    /// <summary>
    /// Lenient date parsing. Real-world feeds use all kinds of date formats; the default
    /// formatter throws on anything that isn't strict RFC 822 / RFC 3339, which would make the
    /// whole feed unreadable because of one bad date. Unparseable dates become "no date".
    /// </summary>
    internal static bool TryParseDate(XmlDateTimeData data, out DateTimeOffset result)
    {
        result = default;
        var value = data.DateTimeString?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, styles, out result))
        {
            return true;
        }

        // RFC 822 with a named zone ("Fri, 02 Oct 2026 08:00:00 EST") or a numeric zone
        // without a colon ("+0200"), which DateTimeOffset.TryParse doesn't understand.
        var normalized = NormalizeRfc822(value);
        if (normalized is not null &&
            DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, styles, out result))
        {
            return true;
        }

        result = default;
        return true; // swallow: keep the item, just without a date
    }

    private static readonly Dictionary<string, string> NamedZones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UT"] = "+00:00", ["UTC"] = "+00:00", ["GMT"] = "+00:00", ["Z"] = "+00:00",
        ["EST"] = "-05:00", ["EDT"] = "-04:00",
        ["CST"] = "-06:00", ["CDT"] = "-05:00",
        ["MST"] = "-07:00", ["MDT"] = "-06:00",
        ["PST"] = "-08:00", ["PDT"] = "-07:00",
        ["CET"] = "+01:00", ["CEST"] = "+02:00",
    };

    private static string? NormalizeRfc822(string value)
    {
        // Drop the optional day name ("Fri,").
        var comma = value.IndexOf(',');
        var rest = (comma >= 0 ? value[(comma + 1)..] : value).Trim();

        var lastSpace = rest.LastIndexOf(' ');
        if (lastSpace < 0)
        {
            return null;
        }

        var zone = rest[(lastSpace + 1)..];
        var dateTime = rest[..lastSpace];

        if (NamedZones.TryGetValue(zone, out var offset))
        {
            return $"{dateTime} {offset}";
        }

        if (zone.Length == 5 && zone[0] is '+' or '-' && zone[1..].All(char.IsAsciiDigit))
        {
            return $"{dateTime} {zone[..3]}:{zone[3..]}";
        }

        return null;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
