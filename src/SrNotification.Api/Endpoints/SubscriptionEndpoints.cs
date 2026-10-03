using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SrNotification.Api.Auth;
using SrNotification.Api.Services;
using SrNotification.Data;
using SrNotification.Data.Entities;
using SrNotification.Infrastructure.Feeds;

namespace SrNotification.Api.Endpoints;

/// <summary>The feeds a user follows, each with its own email and Slack switches.</summary>
public static class SubscriptionEndpoints
{
    public static void MapSubscriptionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/subscriptions").RequireAuthorization().WithTags("Subscriptions");

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync).RequireRateLimiting(RateLimits.FeedChecks);
        group.MapPut("/{id:int}", UpdateAsync).RequireRateLimiting(RateLimits.FeedChecks);
        group.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, SrNotificationDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var items = await Project(db.Subscriptions.Where(s => s.UserId == userId))
            .ToListAsync(ct);

        return TypedResults.Ok(items
            .OrderBy(s => s.Name ?? s.FeedTitle ?? s.Url, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    private static async Task<IResult> CreateAsync(
        SubscriptionRequest request,
        ClaimsPrincipal principal,
        SrNotificationDbContext db,
        FeedCatalog catalog,
        IOptions<AppOptions> appOptions,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (Validate(request, out var url) is { } invalid)
        {
            return invalid;
        }

        var userId = principal.GetUserId();
        if (await db.Subscriptions.CountAsync(s => s.UserId == userId, ct) >= appOptions.Value.MaxSubscriptionsPerUser)
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict,
                $"You can follow at most {appOptions.Value.MaxSubscriptionsPerUser} feeds. Remove one to add another.");
        }

        var lookup = await catalog.FindOrCreateAsync(db, url, ct);
        if (lookup.Feed is null)
        {
            return ApiResults.Invalid("url", lookup.Error!);
        }

        if (lookup.Feed.Id != 0 &&
            await db.Subscriptions.AnyAsync(s => s.UserId == userId && s.FeedId == lookup.Feed.Id, ct))
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "You already follow this feed.");
        }

        var now = timeProvider.GetUtcNow();
        var subscription = new Subscription
        {
            UserId = userId,
            Feed = lookup.Feed,
            Name = NormalizeName(request.Name),
            EmailEnabled = request.EmailEnabled,
            SlackEnabled = request.SlackEnabled,
            CreatedAt = now,
            FollowingSince = now,
            UpdatedAt = now,
        };
        db.Subscriptions.Add(subscription);

        if (await TrySaveAsync(db, ct) is { } conflict)
        {
            return conflict;
        }

        var created = await Project(db.Subscriptions.Where(s => s.Id == subscription.Id)).FirstAsync(ct);
        return TypedResults.Created($"/api/subscriptions/{subscription.Id}", created);
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        SubscriptionRequest request,
        ClaimsPrincipal principal,
        SrNotificationDbContext db,
        FeedCatalog catalog,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (Validate(request, out var url) is { } invalid)
        {
            return invalid;
        }

        var userId = principal.GetUserId();
        var subscription = await db.Subscriptions
            .Include(s => s.Feed)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (subscription is null)
        {
            return TypedResults.NotFound();
        }

        var now = timeProvider.GetUtcNow();
        int? previousFeedId = null;

        // A new URL moves only this user's subscription to another (shared) feed. The old feed's URL
        // isn't edited, because other users may follow it.
        if (!string.Equals(subscription.Feed.Url, url, StringComparison.Ordinal))
        {
            var lookup = await catalog.FindOrCreateAsync(db, url, ct);
            if (lookup.Feed is null)
            {
                return ApiResults.Invalid("url", lookup.Error!);
            }

            if (lookup.Feed.Id != 0 &&
                await db.Subscriptions.AnyAsync(s => s.UserId == userId && s.FeedId == lookup.Feed.Id && s.Id != id, ct))
            {
                return ApiResults.Problem(StatusCodes.Status409Conflict, "You already follow this feed.");
            }

            previousFeedId = subscription.FeedId;
            subscription.Feed = lookup.Feed;
            subscription.FollowingSince = now; // don't send the new feed's older items
        }

        subscription.Name = NormalizeName(request.Name);
        subscription.EmailEnabled = request.EmailEnabled;
        subscription.SlackEnabled = request.SlackEnabled;
        subscription.UpdatedAt = now;

        if (await TrySaveAsync(db, ct) is { } conflict)
        {
            return conflict;
        }

        if (previousFeedId is { } oldFeedId)
        {
            await FeedCatalog.DeactivateIfUnusedAsync(db, oldFeedId, ct);
        }

        return TypedResults.Ok(await Project(db.Subscriptions.Where(s => s.Id == id)).FirstAsync(ct));
    }

    private static async Task<IResult> DeleteAsync(
        int id, ClaimsPrincipal principal, SrNotificationDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (subscription is null)
        {
            return TypedResults.NotFound();
        }

        db.Subscriptions.Remove(subscription);
        await db.SaveChangesAsync(ct);
        await FeedCatalog.DeactivateIfUnusedAsync(db, subscription.FeedId, ct);

        return TypedResults.NoContent();
    }

    private static IQueryable<SubscriptionResponse> Project(IQueryable<Subscription> query) =>
        query.Select(s => new SubscriptionResponse(
            s.Id,
            s.Feed.Url,
            s.Name,
            s.Feed.Title,
            s.Feed.SiteUrl,
            s.EmailEnabled,
            s.SlackEnabled,
            s.CreatedAt,
            s.Feed.LastCheckedAt,
            s.Feed.LastSuccessAt,
            s.Feed.ConsecutiveFailures > 0));

    private static IResult? Validate(SubscriptionRequest request, out string url)
    {
        url = string.Empty;

        if (!FeedUrl.TryNormalize(request.Url, out var normalized, out var error))
        {
            return ApiResults.Invalid("url", error);
        }

        if (request.Name?.Trim().Length > FieldLengths.Name)
        {
            return ApiResults.Invalid("name", $"Keep the name under {FieldLengths.Name} characters.");
        }

        url = normalized;
        return null;
    }

    private static string? NormalizeName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    /// <summary>Two users adding the same new URL at the same moment can collide on the unique index.</summary>
    private static async Task<IResult?> TrySaveAsync(SrNotificationDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict,
                "Someone changed the same feed at the same moment. Try again.");
        }
    }
}
