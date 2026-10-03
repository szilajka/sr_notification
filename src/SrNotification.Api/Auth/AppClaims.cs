using System.Globalization;
using System.Security.Claims;

namespace SrNotification.Api.Auth;

public static class AppClaims
{
    /// <summary>Our own <c>Users.Id</c>, added to the sign-in cookie when the user signs in.</summary>
    public const string UserId = "srn_uid";

    public const string Email = "email";
    public const string Name = "name";
    public const string Roles = "roles";

    public static int GetUserId(this ClaimsPrincipal principal) =>
        int.Parse(
            principal.FindFirstValue(UserId) ?? throw new InvalidOperationException("The user has no app user id."),
            CultureInfo.InvariantCulture);

    public static string? GetEmail(this ClaimsPrincipal principal) => principal.FindFirstValue(Email);
}
