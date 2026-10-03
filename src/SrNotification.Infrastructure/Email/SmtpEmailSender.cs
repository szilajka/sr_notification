using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SrNotification.Data.Entities;

namespace SrNotification.Infrastructure.Email;

public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>Connection details for one send. Built from <see cref="SmtpSettings"/> with the decrypted password.</summary>
public sealed record SmtpConnection(
    string Host,
    int Port,
    SmtpSecurity Security,
    string? Username,
    string? Password,
    string FromAddress,
    string? FromName);

/// <summary>Sends email with MailKit (Microsoft recommends it over System.Net.Mail.SmtpClient).</summary>
public sealed class SmtpEmailSender
{
    public async Task SendAsync(SmtpConnection smtp, EmailMessage message, CancellationToken cancellationToken)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(smtp.FromName ?? string.Empty, smtp.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 30_000 };
        await client.ConnectAsync(smtp.Host, smtp.Port, ToSocketOptions(smtp.Security), cancellationToken);

        if (!string.IsNullOrEmpty(smtp.Username))
        {
            await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    private static SecureSocketOptions ToSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto,
    };
}
