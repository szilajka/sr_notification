using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SrNotification.Api.Auth;

namespace SrNotification.Api;

/// <summary>
/// Limits on endpoints that make the server send messages or fetch URLs on a user's behalf, so a
/// public sign-up can't be used to spam inboxes or hammer other sites.
/// </summary>
public static class RateLimits
{
    /// <summary>Confirmation emails and Slack test messages.</summary>
    public const string OutgoingMessages = "outgoing-messages";

    /// <summary>Adding or changing feeds (each may download the URL).</summary>
    public const string FeedChecks = "feed-checks";

    public static IServiceCollection AddAppRateLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(OutgoingMessages, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromHours(1) }));

            options.AddPolicy(FeedChecks, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(10) }));
        });

        return services;
    }

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst(AppClaims.UserId)?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";
}
