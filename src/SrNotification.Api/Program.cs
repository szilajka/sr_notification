using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using SrNotification.Api;
using SrNotification.Api.Auth;
using SrNotification.Api.Endpoints;
using SrNotification.Api.Services;
using SrNotification.Data;
using SrNotification.Infrastructure.Email;
using SrNotification.Infrastructure.Feeds;
using SrNotification.Infrastructure.Http;
using SrNotification.Infrastructure.Slack;

// Local runs: load POSTGRES_PASSWORD, OIDC_CLIENT_SECRET etc. from the .env file at the repository root.
DotEnvFile.Load();

var builder = WebApplication.CreateBuilder(args);

// Deployment-specific settings and secrets come from environment variables with short names
// (.env locally, docker compose in containers) and override appsettings.json.
var fromEnvironment = new Dictionary<string, string?>();
void MapVariable(string variable, string configurationKey)
{
    if (builder.Configuration[variable] is { Length: > 0 } value)
    {
        fromEnvironment[configurationKey] = value;
    }
}

MapVariable("OIDC_AUTHORITY", "Authentication:Authority");
MapVariable("OIDC_CLIENT_ID", "Authentication:ClientId");
MapVariable("OIDC_CLIENT_SECRET", "Authentication:ClientSecret");
MapVariable("ADMIN_EMAIL", "Authentication:AdminEmails:0");
MapVariable("PUBLIC_URL", "App:PublicUrl");
builder.Configuration.AddInMemoryCollection(fromEnvironment);

var connectionString = DatabasePassword.AddTo(
    builder.Configuration.GetConnectionString("SrNotification")
        ?? throw new InvalidOperationException("Connection string 'SrNotification' is not configured."),
    builder.Configuration[DatabasePassword.VariableName]);

builder.Services.AddSrNotificationData(connectionString);
builder.Services.AddSrNotificationDataProtection();
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddAppAuthentication(builder.Configuration);
builder.Services.AddAppRateLimits();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Outgoing HTTP to user-supplied URLs goes through the SSRF guard.
builder.Services
    .AddHttpClient<FeedFetcher>((services, client) =>
    {
        var settings = services.GetRequiredService<IOptions<AppOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd(
            "application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.8, */*;q=0.5");
        client.MaxResponseContentBufferSize = 10 * 1024 * 1024;
    })
    .ConfigurePrimaryHttpMessageHandler(services => SafeHttpHandler.Create(
        services.GetRequiredService<IOptions<AppOptions>>().Value.AllowPrivateNetworkFeeds));

builder.Services
    .AddHttpClient<SlackWebhookClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => SafeHttpHandler.Create());

builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<EmailConfirmation>();
builder.Services.AddScoped<FeedCatalog>();

var appOptions = builder.Configuration.GetSection(AppOptions.SectionName).Get<AppOptions>() ?? new AppOptions();
if (appOptions.BehindReverseProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // The proxy runs in the same container network; trust it without listing its address.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration[$"{AuthOptions.SectionName}:{nameof(AuthOptions.Authority)}"]))
{
    app.Logger.LogWarning("Authentication:Authority is not set: signing in won't work until it is configured.");
}

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SrNotificationDbContext>();
    await db.Database.MigrateAsync(); // EF Core takes a lock, so the RSS reader can do the same safely
}

if (appOptions.BehindReverseProxy)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// The built React app (copied into wwwroot by the Dockerfile).
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // API explorer at /scalar
}

app.MapAuthEndpoints();

var api = app.MapGroup("/api").AddEndpointFilter<CsrfHeaderFilter>();
api.MapMeEndpoints();
api.MapSubscriptionEndpoints();
api.MapAdminEndpoints();

// Unknown /api routes are 404s; every other path is a page of the React app.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

await app.RunAsync();
