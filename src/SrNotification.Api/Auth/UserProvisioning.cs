using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SrNotification.Data;
using SrNotification.Data.Entities;

namespace SrNotification.Api.Auth;

/// <summary>
/// Runs after the identity provider signed someone in: creates or refreshes their row in Users and
/// adds our user id (and, for bootstrap admins, the admin role) to the sign-in cookie.
/// </summary>
internal static class UserProvisioning
{
    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        var identity = (ClaimsIdentity)principal.Identity!;

        // Entra puts the stable user id in 'oid'; other providers only have 'sub'.
        var externalId = principal.FindFirstValue("oid") ?? principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(externalId))
        {
            context.Fail("The sign-in token has no user identifier.");
            return;
        }

        var email = FindEmail(principal);
        var name = principal.FindFirstValue(AppClaims.Name);

        var services = context.HttpContext.RequestServices;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var dbFactory = services.GetRequiredService<IDbContextFactory<SrNotificationDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(context.HttpContext.RequestAborted);

        var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId, context.HttpContext.RequestAborted);
        if (user is null)
        {
            user = new AppUser { ExternalId = externalId, CreatedAt = now };
            db.Users.Add(user);
        }

        user.Email = Truncate(email, FieldLengths.Email) ?? user.Email;
        user.DisplayName = Truncate(name, FieldLengths.Name) ?? user.DisplayName;
        user.LastSignInAt = now;
        await db.SaveChangesAsync(context.HttpContext.RequestAborted);

        identity.AddClaim(new Claim(AppClaims.UserId, user.Id.ToString(CultureInfo.InvariantCulture)));
        if (email is not null && principal.FindFirstValue(AppClaims.Email) is null)
        {
            identity.AddClaim(new Claim(AppClaims.Email, email));
        }

        var auth = services.GetRequiredService<IOptions<AuthOptions>>().Value;
        if (email is not null &&
            !principal.IsInRole(auth.AdminRole) &&
            auth.AdminEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            identity.AddClaim(new Claim(identity.RoleClaimType, auth.AdminRole));
        }
    }

    private static string? FindEmail(ClaimsPrincipal principal)
    {
        var email = principal.FindFirstValue("email") ?? principal.FindFirstValue("emails");
        if (!string.IsNullOrWhiteSpace(email))
        {
            return email;
        }

        var username = principal.FindFirstValue("preferred_username");
        return username is not null && username.Contains('@') ? username : null;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
