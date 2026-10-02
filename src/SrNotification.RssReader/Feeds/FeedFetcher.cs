using System.Net;
using System.Net.Http.Headers;

namespace SrNotification.RssReader.Feeds;

public sealed record FetchResult(bool NotModified, ParsedFeed? Feed, string? ETag, DateTimeOffset? LastModified)
{
    public static FetchResult Unchanged { get; } = new(true, null, null, null);
}

/// <summary>
/// Downloads a feed with a conditional GET (If-None-Match / If-Modified-Since), so unchanged
/// feeds cost a 304 instead of a full download. Retries, timeouts and the circuit breaker come
/// from the resilience handler registered on the HttpClient.
/// </summary>
public sealed class FeedFetcher(HttpClient httpClient)
{
    public async Task<FetchResult> FetchAsync(
        string url, string? etag, DateTimeOffset? lastModified, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!string.IsNullOrEmpty(etag) && EntityTagHeaderValue.TryParse(etag, out var tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        if (lastModified is not null)
        {
            request.Headers.IfModifiedSince = lastModified;
        }

        // Default completion option buffers the body (bounded by MaxResponseContentBufferSize),
        // so the synchronous XML parsing below never blocks on the network.
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return FetchResult.Unchanged;
        }

        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var feed = FeedParser.Parse(body);

        return new FetchResult(
            NotModified: false,
            Feed: feed,
            ETag: response.Headers.ETag?.ToString(),
            LastModified: response.Content.Headers.LastModified);
    }
}
