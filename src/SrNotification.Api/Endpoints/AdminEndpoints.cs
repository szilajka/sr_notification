using System.Net.Mail;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SrNotification.Api.Auth;
using SrNotification.Data;
using SrNotification.Data.Entities;
using SrNotification.Infrastructure.Email;

namespace SrNotification.Api.Endpoints;

/// <summary>App-wide channel switches, SMTP setup, feed health and error logs. Admins only.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin")
            .RequireAuthorization(AuthenticationSetup.AdminPolicy)
            .WithTags("Admin");

        admin.MapGet("/channels", GetChannelsAsync);
        admin.MapPut("/channels/{channel}", UpdateChannelAsync);

        admin.MapGet("/smtp", GetSmtpAsync);
        admin.MapPut("/smtp", UpdateSmtpAsync);
        admin.MapPost("/smtp/test", SendTestEmailAsync).RequireRateLimiting(RateLimits.OutgoingMessages);

        admin.MapGet("/feeds", ListFeedsAsync);
        admin.MapGet("/logs/feeds", ListFeedErrorsAsync);
        admin.MapGet("/logs/notifications", ListNotificationErrorsAsync);
    }

    // ---- Channels -----------------------------------------------------------------------------------

    private static async Task<IResult> GetChannelsAsync(SrNotificationDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.ChannelSettings.AsNoTracking()
            .OrderBy(c => c.Channel)
            .Select(c => new ChannelSettingResponse(c.Channel, c.IsEnabled, c.UpdatedAt, c.UpdatedBy))
            .ToListAsync(ct));

    private static async Task<IResult> UpdateChannelAsync(
        NotificationChannel channel,
        UpdateChannelRequest request,
        ClaimsPrincipal principal,
        SrNotificationDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var setting = await db.ChannelSettings.FirstOrDefaultAsync(c => c.Channel == channel, ct);
        if (setting is null)
        {
            setting = new ChannelSetting { Channel = channel };
            db.ChannelSettings.Add(setting);
        }

        setting.IsEnabled = request.IsEnabled;
        setting.UpdatedAt = timeProvider.GetUtcNow();
        setting.UpdatedBy = principal.GetEmail();
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new ChannelSettingResponse(setting.Channel, setting.IsEnabled, setting.UpdatedAt, setting.UpdatedBy));
    }

    // ---- SMTP ---------------------------------------------------------------------------------------

    private static async Task<IResult> GetSmtpAsync(SrNotificationDbContext db, CancellationToken ct)
    {
        var settings = await db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SmtpSettings.SingletonId, ct);
        return TypedResults.Ok(ToResponse(settings));
    }

    private static async Task<IResult> UpdateSmtpAsync(
        UpdateSmtpRequest request,
        ClaimsPrincipal principal,
        SrNotificationDbContext db,
        SecretProtector protector,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var host = request.Host?.Trim();
        var fromAddress = request.FromAddress?.Trim();

        if (string.IsNullOrEmpty(host) || host.Length > 255)
        {
            errors["host"] = ["Enter the SMTP server's host name, like smtp.example.com."];
        }

        if (request.Port is < 1 or > 65535)
        {
            errors["port"] = ["Enter a port between 1 and 65535 (usually 587 or 465)."];
        }

        if (!Enum.IsDefined(request.Security))
        {
            errors["security"] = ["Choose how to secure the connection."];
        }

        if (string.IsNullOrEmpty(fromAddress) || fromAddress.Length > FieldLengths.Email ||
            !MailAddress.TryCreate(fromAddress, out _))
        {
            errors["fromAddress"] = ["Enter the address emails are sent from, like news@example.com."];
        }

        if (request.Username?.Trim().Length > FieldLengths.Email)
        {
            errors["username"] = ["The user name is too long."];
        }

        if (request.FromName?.Trim().Length > FieldLengths.Name)
        {
            errors["fromName"] = [$"Keep the sender name under {FieldLengths.Name} characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var settings = await db.SmtpSettings.FirstOrDefaultAsync(s => s.Id == SmtpSettings.SingletonId, ct);
        if (settings is null)
        {
            settings = new SmtpSettings { Host = host!, FromAddress = fromAddress! };
            db.SmtpSettings.Add(settings);
        }

        settings.Host = host!;
        settings.Port = request.Port;
        settings.Security = request.Security;
        settings.Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim();
        settings.FromAddress = fromAddress!;
        settings.FromName = string.IsNullOrWhiteSpace(request.FromName) ? null : request.FromName.Trim();

        if (request.ClearPassword)
        {
            settings.PasswordProtected = null;
        }
        else if (!string.IsNullOrEmpty(request.Password))
        {
            settings.PasswordProtected = protector.Protect(request.Password);
        }

        settings.UpdatedAt = timeProvider.GetUtcNow();
        settings.UpdatedBy = principal.GetEmail();
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponse(settings));
    }

    private static async Task<IResult> SendTestEmailAsync(
        SendTestEmailRequest request, ClaimsPrincipal principal, EmailService emailService, CancellationToken ct)
    {
        var to = string.IsNullOrWhiteSpace(request.To) ? principal.GetEmail() : request.To.Trim();
        if (to is null || !MailAddress.TryCreate(to, out _))
        {
            return ApiResults.Invalid("to", "Enter the address to send the test email to.");
        }

        try
        {
            await emailService.SendAsync(
                new EmailMessage(
                    to,
                    "SR Notification test email",
                    "This is a test email from SR Notification. The SMTP settings work."),
                ct);
        }
        catch (EmailNotConfiguredException)
        {
            return ApiResults.Problem(StatusCodes.Status409Conflict, "Save the SMTP settings first.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Admins need the server's actual answer to fix the settings.
            return ApiResults.Problem(StatusCodes.Status502BadGateway, "The test email couldn't be sent.", ex.Message);
        }

        return TypedResults.NoContent();
    }

    private static SmtpSettingsResponse ToResponse(SmtpSettings? s) => s is null
        ? new SmtpSettingsResponse(false, null, 587, SmtpSecurity.StartTls, null, false, null, null, null, null)
        : new SmtpSettingsResponse(true, s.Host, s.Port, s.Security, s.Username, s.PasswordProtected is not null,
            s.FromAddress, s.FromName, s.UpdatedAt, s.UpdatedBy);

    // ---- Feeds and logs -----------------------------------------------------------------------------

    /// <param name="status">all (default), failing or inactive.</param>
    private static async Task<IResult> ListFeedsAsync(
        string? status, int? page, int? pageSize, SrNotificationDbContext db, CancellationToken ct)
    {
        var paging = Paging.From(page, pageSize);
        var query = db.Feeds.AsNoTracking();
        query = status switch
        {
            "failing" => query.Where(f => f.ConsecutiveFailures > 0),
            "inactive" => query.Where(f => !f.IsActive),
            _ => query,
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(f => f.ConsecutiveFailures)
            .ThenBy(f => f.Url)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(f => new AdminFeedResponse(
                f.Id, f.Url, f.Title, f.IsActive, f.Subscriptions.Count(), f.CreatedAt,
                f.LastCheckedAt, f.LastSuccessAt, f.ConsecutiveFailures, f.LastError))
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResponse<AdminFeedResponse>(items, paging.Page, paging.PageSize, total));
    }

    private static async Task<IResult> ListFeedErrorsAsync(
        int? feedId, int? page, int? pageSize, SrNotificationDbContext db, CancellationToken ct)
    {
        var paging = Paging.From(page, pageSize);
        var query = db.FeedFetchErrors.AsNoTracking();
        if (feedId is not null)
        {
            query = query.Where(e => e.FeedId == feedId);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(e => new FeedErrorLogEntry(e.Id, e.FeedId, e.Feed.Url, e.Feed.Title, e.OccurredAt, e.Message))
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResponse<FeedErrorLogEntry>(items, paging.Page, paging.PageSize, total));
    }

    /// <summary>Failed notification deliveries (filled by the notification sender, part 3).</summary>
    private static async Task<IResult> ListNotificationErrorsAsync(
        NotificationChannel? channel, int? page, int? pageSize, SrNotificationDbContext db, CancellationToken ct)
    {
        var paging = Paging.From(page, pageSize);
        var query = db.NotificationDeliveries.AsNoTracking().Where(d => d.Status == DeliveryStatus.Failed);
        if (channel is not null)
        {
            query = query.Where(d => d.Channel == channel);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(d => d.LastAttemptAt ?? d.CreatedAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(d => new NotificationErrorLogEntry(
                d.Id, d.Channel, d.Status, d.Attempts, d.LastError, d.CreatedAt, d.LastAttemptAt,
                d.UserId, d.User.NotificationEmail ?? d.User.Email, d.RssItemId, d.RssItem.Title, d.RssItem.Feed.Url))
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResponse<NotificationErrorLogEntry>(items, paging.Page, paging.PageSize, total));
    }
}
