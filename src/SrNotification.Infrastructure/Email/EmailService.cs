using Microsoft.EntityFrameworkCore;
using SrNotification.Data;
using SrNotification.Data.Entities;

namespace SrNotification.Infrastructure.Email;

/// <summary>Thrown when an email has to go out but no SMTP server has been set up yet.</summary>
public sealed class EmailNotConfiguredException()
    : InvalidOperationException("Email sending is not set up: an admin has to configure the SMTP server.");

/// <summary>Sends email through the SMTP server an admin configured in the app.</summary>
public sealed class EmailService(
    IDbContextFactory<SrNotificationDbContext> dbContextFactory,
    SecretProtector secretProtector,
    SmtpEmailSender sender)
{
    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.SmtpSettings.AnyAsync(s => s.Id == SmtpSettings.SingletonId, cancellationToken);
    }

    /// <exception cref="EmailNotConfiguredException">No SMTP server configured.</exception>
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.SmtpSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == SmtpSettings.SingletonId, cancellationToken)
            ?? throw new EmailNotConfiguredException();

        await sender.SendAsync(ToConnection(settings), message, cancellationToken);
    }

    public SmtpConnection ToConnection(SmtpSettings settings) => new(
        settings.Host,
        settings.Port,
        settings.Security,
        settings.Username,
        settings.PasswordProtected is null ? null : secretProtector.Unprotect(settings.PasswordProtected),
        settings.FromAddress,
        settings.FromName);
}
