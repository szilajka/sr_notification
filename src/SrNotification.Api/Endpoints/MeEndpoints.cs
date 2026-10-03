using System.Net.Mail;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SrNotification.Api.Auth;
using SrNotification.Api.Services;
using SrNotification.Data;
using SrNotification.Data.Entities;
using SrNotification.Infrastructure.Email;
using SrNotification.Infrastructure.Slack;

namespace SrNotification.Api.Endpoints;

/// <summary>The signed-in user's profile and where their notifications go.</summary>
public static class MeEndpoints
{
    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").RequireAuthorization().WithTags("Me");

        me.MapGet("", GetMeAsync);
        me.MapGet("/notifications", GetNotificationSettingsAsync);
        me.MapPut("/notifications/email", UpdateEmailAsync).RequireRateLimiting(RateLimits.OutgoingMessages);
        me.MapPut("/notifications/slack", UpdateSlackAsync);
        me.MapPost("/notifications/slack/test", SendSlackTestAsync).RequireRateLimiting(RateLimits.OutgoingMessages);

        // Opened from the emailed link, possibly in another browser, so the token alone is enough.
        api.MapPost("/email-confirmations", ConfirmEmailAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimits.OutgoingMessages)
            .WithTags("Me");
    }

    private static async Task<IResult> GetMeAsync(
        ClaimsPrincipal principal, SrNotificationDbContext db, IOptions<AuthOptions> auth, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == principal.GetUserId(), ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new MeResponse(
            user.Id, user.Email, user.DisplayName, principal.IsInRole(auth.Value.AdminRole)));
    }

    private static async Task<IResult> GetNotificationSettingsAsync(
        ClaimsPrincipal principal, SrNotificationDbContext db, SecretProtector protector, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == principal.GetUserId(), ct);
        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(await BuildSettingsAsync(db, user, protector, ct));
    }

    private static async Task<IResult> UpdateEmailAsync(
        UpdateEmailRequest request,
        ClaimsPrincipal principal,
        SrNotificationDbContext db,
        SecretProtector protector,
        EmailConfirmation confirmation,
        EmailService emailService,
        TimeProvider timeProvider,
        HttpRequest httpRequest,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == principal.GetUserId(), ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var address = request.Address?.Trim();

        // Back to the sign-in address (always allowed, nothing to confirm).
        if (string.IsNullOrEmpty(address) || string.Equals(address, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            user.NotificationEmail = null;
            ClearPending(user);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(await BuildSettingsAsync(db, user, protector, ct));
        }

        if (address.Length > FieldLengths.Email ||
            !MailAddress.TryCreate(address, out var parsed) ||
            !string.Equals(parsed.Address, address, StringComparison.Ordinal))
        {
            return ApiResults.Invalid("address", "Enter a valid email address, like name@example.com.");
        }

        // Already confirmed earlier.
        if (string.Equals(address, user.NotificationEmail, StringComparison.OrdinalIgnoreCase))
        {
            ClearPending(user);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(await BuildSettingsAsync(db, user, protector, ct));
        }

        if (!await emailService.IsConfiguredAsync(ct))
        {
            return ApiResults.Problem(StatusCodes.Status503ServiceUnavailable,
                "Email sending isn't set up yet, so the new address can't be confirmed. Try again later.");
        }

        var (token, hash) = EmailConfirmation.CreateToken();
        var link = confirmation.BuildLink(httpRequest, token);
        if (link is null)
        {
            return ApiResults.Problem(StatusCodes.Status503ServiceUnavailable,
                "The app's public address isn't configured, so confirmation links can't be sent.",
                "An administrator has to set App:PublicUrl.");
        }

        user.PendingNotificationEmail = address;
        user.PendingEmailTokenHash = hash;
        user.PendingEmailExpiresAt = timeProvider.GetUtcNow() + EmailConfirmation.LinkLifetime;
        await db.SaveChangesAsync(ct);

        try
        {
            await confirmation.SendAsync(address, link, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggerFactory.CreateLogger(typeof(MeEndpoints)).LogWarning(ex, "Could not send the confirmation email");
            ClearPending(user);
            await db.SaveChangesAsync(CancellationToken.None);
            return ApiResults.Problem(StatusCodes.Status502BadGateway,
                "The confirmation email couldn't be sent. Try again later.");
        }

        return TypedResults.Accepted((string?)null, await BuildSettingsAsync(db, user, protector, ct));
    }

    private static async Task<IResult> ConfirmEmailAsync(
        ConfirmEmailRequest request, SrNotificationDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        const string invalidLink =
            "This confirmation link is invalid or has expired. Ask for a new one in your notification settings.";

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return ApiResults.Invalid("token", invalidLink);
        }

        var hash = EmailConfirmation.Hash(request.Token.Trim());
        var user = await db.Users.FirstOrDefaultAsync(u => u.PendingEmailTokenHash == hash, ct);
        if (user?.PendingNotificationEmail is null || user.PendingEmailExpiresAt < timeProvider.GetUtcNow())
        {
            return ApiResults.Invalid("token", invalidLink);
        }

        var address = user.PendingNotificationEmail;
        user.NotificationEmail = string.Equals(address, user.Email, StringComparison.OrdinalIgnoreCase) ? null : address;
        ClearPending(user);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new ConfirmEmailResponse(address));
    }

    private static async Task<IResult> UpdateSlackAsync(
        UpdateSlackRequest request,
        ClaimsPrincipal principal,
        SrNotificationDbContext db,
        SecretProtector protector,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == principal.GetUserId(), ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var url = request.WebhookUrl?.Trim();
        if (string.IsNullOrEmpty(url))
        {
            user.SlackWebhookUrlProtected = null;
        }
        else if (!SlackWebhook.IsValid(url))
        {
            return ApiResults.Invalid("webhookUrl",
                "Paste a Slack incoming webhook URL. It starts with https://hooks.slack.com/services/.");
        }
        else
        {
            user.SlackWebhookUrlProtected = protector.Protect(url);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await BuildSettingsAsync(db, user, protector, ct));
    }

    private static async Task<IResult> SendSlackTestAsync(
        ClaimsPrincipal principal,
        SrNotificationDbContext db,
        SecretProtector protector,
        SlackWebhookClient slack,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == principal.GetUserId(), ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await IsChannelEnabledAsync(db, NotificationChannel.Slack, ct))
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict,
                "Slack notifications are turned off for everyone at the moment.");
        }

        if (user.SlackWebhookUrlProtected is null)
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "Add a Slack webhook first.");
        }

        try
        {
            await slack.SendAsync(
                protector.Unprotect(user.SlackWebhookUrlProtected),
                "This is a test from SR Notification. New items from the feeds you follow will arrive here.",
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ApiResults.Problem(StatusCodes.Status502BadGateway,
                "Slack didn't accept the test message. Check the webhook URL.", ex.Message);
        }

        return TypedResults.NoContent();
    }

    private static async Task<NotificationSettingsResponse> BuildSettingsAsync(
        SrNotificationDbContext db, AppUser user, SecretProtector protector, CancellationToken ct)
    {
        var channels = await db.ChannelSettings.AsNoTracking().ToDictionaryAsync(c => c.Channel, c => c.IsEnabled, ct);

        string? webhookHint = null;
        if (user.SlackWebhookUrlProtected is not null)
        {
            try
            {
                webhookHint = SlackWebhook.Mask(protector.Unprotect(user.SlackWebhookUrlProtected));
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Keys were lost; the user has to enter the webhook again.
            }
        }

        return new NotificationSettingsResponse(
            new EmailSettingsResponse(
                user.EffectiveNotificationEmail,
                user.Email,
                user.NotificationEmail is not null,
                user.PendingNotificationEmail,
                channels.GetValueOrDefault(NotificationChannel.Email, true)),
            new SlackSettingsResponse(
                user.SlackWebhookUrlProtected is not null,
                webhookHint,
                channels.GetValueOrDefault(NotificationChannel.Slack, true)));
    }

    internal static async Task<bool> IsChannelEnabledAsync(
        SrNotificationDbContext db, NotificationChannel channel, CancellationToken ct) =>
        await db.ChannelSettings.AsNoTracking()
            .Where(c => c.Channel == channel)
            .Select(c => (bool?)c.IsEnabled)
            .FirstOrDefaultAsync(ct) ?? true;

    private static void ClearPending(AppUser user)
    {
        user.PendingNotificationEmail = null;
        user.PendingEmailTokenHash = null;
        user.PendingEmailExpiresAt = null;
    }
}
