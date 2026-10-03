namespace SrNotification.Data.Entities;

public enum SmtpSecurity
{
    /// <summary>Let the client decide (STARTTLS when the server offers it).</summary>
    Auto = 0,
    None = 1,
    StartTls = 2,
    SslOnConnect = 3,
}

/// <summary>The SMTP server used for all outgoing email. A single row (Id = 1), edited by admins.</summary>
public class SmtpSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public required string Host { get; set; }

    public int Port { get; set; } = 587;

    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    public string? Username { get; set; }

    /// <summary>Encrypted with ASP.NET Core Data Protection; never returned by the API.</summary>
    public string? PasswordProtected { get; set; }

    public required string FromAddress { get; set; }

    public string? FromName { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}
