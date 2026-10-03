namespace SrNotification.Infrastructure.Feeds;

public sealed record ParsedFeed(string? Title, string? SiteUrl, IReadOnlyList<ParsedItem> Items);

public sealed record ParsedItem(
    string ExternalId,
    string Title,
    string? Link,
    string? Summary,
    string? Author,
    DateTimeOffset? PublishedAt);
