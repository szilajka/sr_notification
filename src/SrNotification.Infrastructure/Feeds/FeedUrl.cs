using System.Diagnostics.CodeAnalysis;
using System.Net;
using SrNotification.Data;
using SrNotification.Infrastructure.Http;

namespace SrNotification.Infrastructure.Feeds;

/// <summary>Validates and normalizes a feed URL typed by a user.</summary>
public static class FeedUrl
{
    /// <returns>True with the normalized URL, or false with a message that can be shown to the user.</returns>
    public static bool TryNormalize(
        string? input,
        [NotNullWhen(true)] out string? url,
        [NotNullWhen(false)] out string? error)
    {
        url = null;
        error = null;

        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = "Enter the address of the feed.";
            return false;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "Enter a full address that starts with http:// or https://.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "Addresses that contain a user name or password aren't supported.";
            return false;
        }

        // Friendly early answer for obvious cases; SafeHttpHandler enforces the rule when connecting.
        var host = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host.Trim('[', ']') : uri.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(host, out var ip) && !PublicAddress.IsPublic(ip)))
        {
            error = "Feeds on local or private network addresses aren't supported.";
            return false;
        }

        var normalized = uri.AbsoluteUri;
        if (normalized.Length > FieldLengths.Url)
        {
            error = $"The address is too long (at most {FieldLengths.Url} characters).";
            return false;
        }

        url = normalized;
        return true;
    }
}
