using MailKit.Net.Smtp;

namespace SrNotification.Infrastructure.Email;

/// <summary>
/// One SMTP connection reused for several messages (the notification sender sends a batch per cycle).
/// Connects lazily, and reconnects on the next send after a failure.
/// </summary>
public sealed class SmtpSession(SmtpConnection smtp) : IAsyncDisposable
{
    private SmtpClient? _client;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var mime = SmtpEmailSender.ToMime(smtp, message);
        var client = await GetConnectedClientAsync(cancellationToken);
        try
        {
            await client.SendAsync(mime, cancellationToken);
        }
        catch
        {
            // The connection may be in an unknown state; start a fresh one next time.
            await DropClientAsync();
            throw;
        }
    }

    private async Task<SmtpClient> GetConnectedClientAsync(CancellationToken cancellationToken)
    {
        if (_client is { IsConnected: true })
        {
            return _client;
        }

        await DropClientAsync();
        var client = new SmtpClient { Timeout = 30_000 };
        try
        {
            await client.ConnectAsync(smtp.Host, smtp.Port, SmtpEmailSender.ToSocketOptions(smtp.Security), cancellationToken);
            if (!string.IsNullOrEmpty(smtp.Username))
            {
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, cancellationToken);
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        return client;
    }

    private async Task DropClientAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync(quit: true);
            }
        }
        catch
        {
            // Best effort.
        }

        _client.Dispose();
        _client = null;
    }

    public async ValueTask DisposeAsync() => await DropClientAsync();
}
