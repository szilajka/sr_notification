using Microsoft.Extensions.Options;
using SrNotification.Data;
using SrNotification.Infrastructure.Feeds;
using SrNotification.Infrastructure.Http;
using SrNotification.RssReader;
using SrNotification.RssReader.Feeds;

// Local runs: pull POSTGRES_PASSWORD etc. from the .env file at the repository root.
// In Docker there is no .env file; docker compose passes the variables instead.
DotEnvFile.Load();

var builder = Host.CreateApplicationBuilder(args);

var connectionString = DatabasePassword.AddTo(
    builder.Configuration.GetConnectionString("SrNotification")
        ?? throw new InvalidOperationException("Connection string 'SrNotification' is not configured."),
    builder.Configuration[DatabasePassword.VariableName]);

builder.Services.AddSrNotificationData(connectionString);

builder.Services
    .AddOptions<RssReaderOptions>()
    .Bind(builder.Configuration.GetSection(RssReaderOptions.SectionName))
    .Validate(o => o.PollInterval > TimeSpan.Zero, "RssReader:PollInterval must be positive.")
    .Validate(o => o.RefreshInterval > TimeSpan.Zero, "RssReader:RefreshInterval must be positive.")
    .Validate(o => o.MaxParallelFeeds > 0, "RssReader:MaxParallelFeeds must be at least 1.")
    .Validate(o => o.MaxFeedSizeBytes > 0, "RssReader:MaxFeedSizeBytes must be positive.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddHttpClient<FeedFetcher>((services, client) =>
    {
        var options = services.GetRequiredService<IOptions<RssReaderOptions>>().Value;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd(
            "application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.8, */*;q=0.5");
        client.MaxResponseContentBufferSize = options.MaxFeedSizeBytes;
    })
    // Feed URLs come from users: refuse to connect to private/internal addresses (SSRF guard).
    .ConfigurePrimaryHttpMessageHandler(services => SafeHttpHandler.Create(
        services.GetRequiredService<IOptions<RssReaderOptions>>().Value.AllowPrivateNetworkFeeds))
    // Retries (with jitter) on 5xx/408/429 and network errors, per-attempt + total timeouts,
    // and a circuit breaker per HttpClient.
    .AddStandardResilienceHandler();

builder.Services.AddScoped<FeedRefresher>();
builder.Services.AddHostedService<RssReaderWorker>();

var host = builder.Build();

await DatabaseStartup.InitializeAsync(host);

await host.RunAsync();
