using System.Xml;
using Microsoft.EntityFrameworkCore;
using SrNotification.Data;
using SrNotification.Data.Entities;
using SrNotification.Infrastructure.Feeds;
using SrNotification.Infrastructure.Http;

namespace SrNotification.Api.Services;

public sealed record FeedLookupResult(RssFeed? Feed, string? Error);

/// <summary>
/// Feeds are shared: every user who follows the same URL points at the same RssFeeds row.
/// This finds that row or creates it after checking the URL really is a feed.
/// </summary>
public sealed class FeedCatalog(FeedFetcher fetcher, TimeProvider timeProvider, ILogger<FeedCatalog> logger)
{
    /// <summary>
    /// Returns the feed for <paramref name="url"/> (tracked by <paramref name="db"/>, not saved yet),
    /// or a message for the user when the URL can't be read as a feed.
    /// </summary>
    public async Task<FeedLookupResult> FindOrCreateAsync(
        SrNotificationDbContext db, string url, CancellationToken cancellationToken)
    {
        var feed = await db.Feeds.FirstOrDefaultAsync(f => f.Url == url, cancellationToken);

        // Known to work: no need to download it again.
        if (feed is { LastSuccessAt: not null })
        {
            feed.IsActive = true;
            return new FeedLookupResult(feed, null);
        }

        // New, or never read successfully: check it now so the user gets an answer right away.
        ParsedFeed? parsed;
        try
        {
            var result = await fetcher.FetchAsync(url, etag: null, lastModified: null, cancellationToken);
            parsed = result.Feed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Feed check failed for {Url}", url);
            return new FeedLookupResult(null, Describe(ex));
        }

        if (feed is null)
        {
            feed = new RssFeed { Url = url, CreatedAt = timeProvider.GetUtcNow() };
            db.Feeds.Add(feed);
        }

        feed.IsActive = true;
        feed.Title ??= parsed?.Title;
        feed.SiteUrl ??= parsed?.SiteUrl;
        return new FeedLookupResult(feed, null);
    }

    /// <summary>Stops the RSS reader polling a feed once nobody follows it any more.</summary>
    public static Task DeactivateIfUnusedAsync(
        SrNotificationDbContext db, int feedId, CancellationToken cancellationToken) =>
        db.Feeds
            .Where(f => f.Id == feedId && !f.Subscriptions.Any())
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsActive, false), cancellationToken);

    private static string Describe(Exception exception) => exception switch
    {
        _ when BlockedAddressException.IsCauseOf(exception) =>
            "Feeds on local or private network addresses aren't supported.",
        FormatException => "That address doesn't point to an RSS or Atom feed.",
        XmlException => "That address didn't return a readable feed (the XML is invalid).",
        HttpRequestException { StatusCode: { } status } =>
            $"The site answered with an error ({(int)status}). Check the address.",
        HttpRequestException => "The site couldn't be reached. Check the address and try again.",
        TimeoutException or TaskCanceledException => "The site took too long to answer. Try again later.",
        _ => "No feed could be read from that address.",
    };
}
