using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SrNotification.Data;
using SrNotification.Data.Entities;

namespace SrNotification.RssReader.Feeds;

public sealed record RefreshSummary(int FeedsDue, int FeedsSucceeded, int FeedsFailed, int NewItems);

/// <summary>
/// One refresh cycle: find the active feeds that are due, fetch them in parallel and store the
/// items that aren't in the database yet. Resolved from a fresh DI scope every cycle.
/// </summary>
public sealed class FeedRefresher(
    IDbContextFactory<SrNotificationDbContext> dbContextFactory,
    FeedFetcher fetcher,
    IOptions<RssReaderOptions> options,
    TimeProvider timeProvider,
    ILogger<FeedRefresher> logger)
{
    private readonly RssReaderOptions _options = options.Value;

    private sealed record FeedState(
        int Id, string Url, string? ETag, DateTimeOffset? LastModified,
        DateTimeOffset? LastCheckedAt, DateTimeOffset? LastSuccessAt, int ConsecutiveFailures);

    public async Task<RefreshSummary> RefreshDueFeedsAsync(CancellationToken cancellationToken)
    {
        List<FeedState> feeds;
        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            feeds = await db.Feeds
                .AsNoTracking()
                .Where(f => f.IsActive)
                .Select(f => new FeedState(
                    f.Id, f.Url, f.ETag, f.LastModified, f.LastCheckedAt, f.LastSuccessAt, f.ConsecutiveFailures))
                .ToListAsync(cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var due = feeds
            .Where(f => FeedSchedule.IsDue(f.LastCheckedAt, f.ConsecutiveFailures, now, _options))
            .ToList();

        if (due.Count == 0)
        {
            return new RefreshSummary(0, 0, 0, 0);
        }

        int succeeded = 0, failed = 0, newItems = 0;

        await Parallel.ForEachAsync(
            due,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, _options.MaxParallelFeeds),
                CancellationToken = cancellationToken,
            },
            async (feed, ct) =>
            {
                var added = await RefreshFeedAsync(feed, ct);
                if (added is null)
                {
                    Interlocked.Increment(ref failed);
                }
                else
                {
                    Interlocked.Increment(ref succeeded);
                    Interlocked.Add(ref newItems, added.Value);
                }
            });

        return new RefreshSummary(due.Count, succeeded, failed, newItems);
    }

    /// <returns>Number of new items stored, or null if the feed failed.</returns>
    private async Task<int?> RefreshFeedAsync(FeedState feed, CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();

        try
        {
            var result = await fetcher.FetchAsync(feed.Url, feed.ETag, feed.LastModified, cancellationToken);

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entity = await db.Feeds.FirstOrDefaultAsync(f => f.Id == feed.Id, cancellationToken);
            if (entity is null)
            {
                return 0; // deleted while we were fetching it
            }

            entity.LastCheckedAt = startedAt;
            entity.LastSuccessAt = startedAt;
            entity.LastError = null;
            entity.ConsecutiveFailures = 0;

            var added = 0;
            if (result.Feed is { } parsed)
            {
                entity.ETag = Truncate(result.ETag, FieldLengths.ETag);
                entity.LastModified = result.LastModified?.ToUniversalTime();
                entity.Title = parsed.Title ?? entity.Title;
                entity.SiteUrl = parsed.SiteUrl ?? entity.SiteUrl;

                added = await AddNewItemsAsync(db, feed, parsed, startedAt, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);

            if (added > 0)
            {
                logger.LogInformation("Feed {FeedId} ({Url}): {Count} new item(s)", feed.Id, feed.Url, added);
            }
            else
            {
                logger.LogDebug("Feed {FeedId} ({Url}): no new items{NotModified}",
                    feed.Id, feed.Url, result.NotModified ? " (304 Not Modified)" : "");
            }

            return added;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutting down
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Feed {FeedId} ({Url}) failed ({Failures} failure(s) in a row)",
                feed.Id, feed.Url, feed.ConsecutiveFailures + 1);

            await RecordFailureAsync(feed.Id, startedAt, ex, cancellationToken);
            return null;
        }
    }

    private static async Task<int> AddNewItemsAsync(
        SrNotificationDbContext db, FeedState feed, ParsedFeed parsed, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (parsed.Items.Count == 0)
        {
            return 0;
        }

        var ids = parsed.Items.Select(i => i.ExternalId).ToList();

        var existing = (await db.Items
                .Where(i => i.FeedId == feed.Id && ids.Contains(i.ExternalId))
                .Select(i => i.ExternalId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var isInitialFetch = feed.LastSuccessAt is null;

        var newItems = parsed.Items
            .Where(i => !existing.Contains(i.ExternalId))
            .Select(i => new RssItem
            {
                FeedId = feed.Id,
                ExternalId = i.ExternalId,
                Title = i.Title,
                Link = i.Link,
                Summary = i.Summary,
                Author = i.Author,
                PublishedAt = i.PublishedAt,
                FetchedAt = now,
                IsFromInitialFetch = isInitialFetch,
            })
            .ToList();

        db.Items.AddRange(newItems);
        return newItems.Count;
    }

    private async Task RecordFailureAsync(
        int feedId, DateTimeOffset checkedAt, Exception exception, CancellationToken cancellationToken)
    {
        var error = Truncate($"{exception.GetType().Name}: {exception.Message}", FieldLengths.Error);

        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await db.Feeds
                .Where(f => f.Id == feedId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.LastCheckedAt, checkedAt)
                    .SetProperty(f => f.LastError, error)
                    .SetProperty(f => f.ConsecutiveFailures, f => f.ConsecutiveFailures + 1),
                    cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not record the failure of feed {FeedId}", feedId);
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
