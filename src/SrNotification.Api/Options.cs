namespace SrNotification.Api;

/// <summary>Sign-in through an OpenID Connect provider (Entra External ID). Section "Authentication".</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Authentication";

    /// <summary>For Entra External ID: https://&lt;tenant-subdomain&gt;.ciamlogin.com/&lt;tenant-id&gt;/v2.0</summary>
    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    /// <summary>Keep it out of appsettings: set OIDC_CLIENT_SECRET in .env.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>The Entra app role that makes a user an admin.</summary>
    public string AdminRole { get; set; } = "Admin";

    /// <summary>
    /// Sign-in emails that are treated as admins even without the app role. Handy to bootstrap the
    /// first admin; prefer app roles afterwards.
    /// </summary>
    public List<string> AdminEmails { get; set; } = [];
}

/// <summary>General app settings. Section "App".</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// The public address of the app, e.g. https://news.example.com. Used in links sent by email.
    /// Required outside Development (otherwise the links would trust the request's Host header).
    /// </summary>
    public string? PublicUrl { get; set; }

    public int MaxSubscriptionsPerUser { get; set; } = 200;

    /// <summary>Allow feeds on private/loopback addresses. Local development only.</summary>
    public bool AllowPrivateNetworkFeeds { get; set; }

    /// <summary>Trust X-Forwarded-* headers (set when running behind a reverse proxy that terminates TLS).</summary>
    public bool BehindReverseProxy { get; set; }

    public string UserAgent { get; set; } = "SrNotification/1.0";
}
