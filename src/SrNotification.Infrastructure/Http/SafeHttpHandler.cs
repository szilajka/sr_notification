using System.Net;
using System.Net.Sockets;

namespace SrNotification.Infrastructure.Http;

/// <summary>
/// Creates the primary handler for HttpClients that call user-supplied URLs (feeds, Slack webhooks).
/// The check runs when a connection is opened, after DNS resolution, and the socket connects to exactly
/// the addresses that were checked. That covers redirects and DNS tricks (a public name that resolves
/// to 127.0.0.1, or DNS rebinding between a check and the request).
/// </summary>
public static class SafeHttpHandler
{
    /// <param name="allowPrivateAddresses">
    /// Turns the guard off. Only for local development (e.g. a test feed served from your machine).
    /// </param>
    public static SocketsHttpHandler Create(bool allowPrivateAddresses = false)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };

        if (!allowPrivateAddresses)
        {
            // Through a proxy the callback would only see the proxy's address, not the target's.
            handler.UseProxy = false;
            handler.ConnectCallback = ConnectToPublicAddressAsync;
        }

        return handler;
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;

        var addresses = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

        if (addresses.Length == 0)
        {
            throw new HttpRequestException($"The host name '{host}' could not be resolved.");
        }

        if (addresses.Any(address => !PublicAddress.IsPublic(address)))
        {
            throw new BlockedAddressException(
                $"'{host}' points to a private or reserved network address, which is not allowed.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>The target of an outgoing request resolved to a private or reserved address and was refused.</summary>
public sealed class BlockedAddressException(string message) : IOException(message)
{
    /// <summary>True if <paramref name="exception"/> or one of its inner exceptions is a refused connection.</summary>
    public static bool IsCauseOf(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is BlockedAddressException)
            {
                return true;
            }
        }

        return false;
    }
}
