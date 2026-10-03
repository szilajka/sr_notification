using Microsoft.AspNetCore.DataProtection;

namespace SrNotification.Data;

/// <summary>
/// Encrypts secrets stored in the database (SMTP password, Slack webhook URLs). Every app that needs
/// them must call <see cref="DependencyInjection.AddSrNotificationDataProtection"/> so they share keys.
/// </summary>
public sealed class SecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("SrNotification.Secrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
