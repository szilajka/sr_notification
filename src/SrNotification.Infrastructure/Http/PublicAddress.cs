using System.Net;

namespace SrNotification.Infrastructure.Http;

/// <summary>
/// Tells public internet addresses from private, loopback, link-local and other reserved ranges.
/// Users decide which URLs the server fetches, so without this check someone could make the server
/// call internal services (databases, cloud metadata endpoints, admin panels): SSRF.
/// </summary>
public static class PublicAddress
{
    private static readonly IPNetwork[] BlockedNetworks =
    [
        // IPv4
        IPNetwork.Parse("0.0.0.0/8"),        // "this" network
        IPNetwork.Parse("10.0.0.0/8"),       // private
        IPNetwork.Parse("100.64.0.0/10"),    // carrier-grade NAT
        IPNetwork.Parse("127.0.0.0/8"),      // loopback
        IPNetwork.Parse("169.254.0.0/16"),   // link-local, incl. cloud metadata (169.254.169.254)
        IPNetwork.Parse("172.16.0.0/12"),    // private (incl. Docker networks)
        IPNetwork.Parse("192.0.0.0/24"),     // IETF protocol assignments
        IPNetwork.Parse("192.0.2.0/24"),     // documentation
        IPNetwork.Parse("192.88.99.0/24"),   // 6to4 relay
        IPNetwork.Parse("192.168.0.0/16"),   // private
        IPNetwork.Parse("198.18.0.0/15"),    // benchmarking
        IPNetwork.Parse("198.51.100.0/24"),  // documentation
        IPNetwork.Parse("203.0.113.0/24"),   // documentation
        IPNetwork.Parse("224.0.0.0/4"),      // multicast
        IPNetwork.Parse("240.0.0.0/4"),      // reserved, incl. broadcast
        // IPv6
        IPNetwork.Parse("::/128"),           // unspecified
        IPNetwork.Parse("::1/128"),          // loopback
        IPNetwork.Parse("64:ff9b::/96"),     // NAT64 (can embed private IPv4)
        IPNetwork.Parse("64:ff9b:1::/48"),   // local-use NAT64
        IPNetwork.Parse("100::/64"),         // discard
        IPNetwork.Parse("2001::/32"),        // Teredo
        IPNetwork.Parse("2001:db8::/32"),    // documentation
        IPNetwork.Parse("2002::/16"),        // 6to4 (can embed private IPv4)
        IPNetwork.Parse("fc00::/7"),         // unique local
        IPNetwork.Parse("fe80::/10"),        // link-local
        IPNetwork.Parse("fec0::/10"),        // site-local (deprecated)
        IPNetwork.Parse("ff00::/8"),         // multicast
    ];

    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        foreach (var network in BlockedNetworks)
        {
            // Contains returns false when the address family differs.
            if (network.Contains(address))
            {
                return false;
            }
        }

        return true;
    }
}
