using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace SrNotification.Api.Auth;

/// <summary>
/// "Backend for frontend" sign-in: the API runs the OpenID Connect flow with the identity provider and
/// keeps the session in an HttpOnly cookie. The React app never sees a token; it just calls /api on
/// the same origin. Works with Entra External ID or any other OpenID Connect provider.
/// </summary>
public static class AuthenticationSetup
{
    public const string AdminPolicy = "Admin";

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AuthOptions.SectionName);
        services.Configure<AuthOptions>(section);
        var auth = section.Get<AuthOptions>() ?? new AuthOptions();

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "srn.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;

                // The API answers with status codes instead of redirecting to HTML pages.
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = auth.Authority;
                options.ClientId = auth.ClientId;
                options.ClientSecret = auth.ClientSecret;

                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                // The provider sends the user back with a normal GET, so the correlation cookies can stay
                // SameSite=Lax and sign-in also works on http://localhost during development.
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                options.MapInboundClaims = false; // keep the short JWT claim names: oid, email, name, roles
                options.SaveTokens = false;       // we don't call other APIs on the user's behalf
                options.TokenValidationParameters.NameClaimType = AppClaims.Name;
                options.TokenValidationParameters.RoleClaimType = AppClaims.Roles;

                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    // API calls from the SPA get a 401 instead of a redirect they couldn't follow.
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.HandleResponse();
                    }

                    return Task.CompletedTask;
                };
                options.Events.OnTokenValidated = UserProvisioning.OnTokenValidatedAsync;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(auth.AdminRole));

        return services;
    }

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").AllowAnonymous().ExcludeFromDescription();

        // Sign-in and sign-up: Entra External ID's hosted page offers both.
        group.MapGet("/login", (string? returnUrl) =>
            Results.Challenge(
                new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) },
                [OpenIdConnectDefaults.AuthenticationScheme]));

        // A normal form POST from the SPA, so the browser follows the redirect to the provider's sign-out page.
        group.MapPost("/logout", () =>
            Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
    }

    /// <summary>Only local paths, so the login link can't be used to bounce users to another site.</summary>
    private static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) &&
        returnUrl.StartsWith('/') &&
        !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
        !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
