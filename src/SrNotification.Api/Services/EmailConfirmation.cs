using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SrNotification.Infrastructure.Email;

namespace SrNotification.Api.Services;

/// <summary>
/// Confirmation links for a changed notification address. Notifications only go to an address after
/// someone clicked the link sent to it, so nobody can point their notifications at another person's inbox.
/// </summary>
public sealed class EmailConfirmation(EmailService emailService, IOptions<AppOptions> appOptions, IHostEnvironment environment)
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(24);

    /// <returns>The token for the link, and the hash stored in the database.</returns>
    public static (string Token, string Hash) CreateToken()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <returns>The link, or null when App:PublicUrl isn't set outside Development.</returns>
    public string? BuildLink(HttpRequest request, string token)
    {
        var baseUrl = appOptions.Value.PublicUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!environment.IsDevelopment())
            {
                return null;
            }

            baseUrl = $"{request.Scheme}://{request.Host}";
        }

        return $"{baseUrl.TrimEnd('/')}/confirm-email?token={Uri.EscapeDataString(token)}";
    }

    public Task SendAsync(string to, string link, CancellationToken cancellationToken)
    {
        var text =
            $"""
            Someone (hopefully you) asked SR Notification to send feed notifications to this address.

            Confirm the address by opening this link within 24 hours:
            {link}

            If you didn't ask for this, ignore this email and nothing will be sent here.
            """;

        var encodedLink = WebUtility.HtmlEncode(link);
        var html =
            $"""
            <p>Someone (hopefully you) asked SR Notification to send feed notifications to this address.</p>
            <p><a href="{encodedLink}">Confirm this address</a> within 24 hours.</p>
            <p>If you didn't ask for this, ignore this email and nothing will be sent here.</p>
            """;

        return emailService.SendAsync(
            new EmailMessage(to, "Confirm your notification address", text, html), cancellationToken);
    }
}
