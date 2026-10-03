using Microsoft.Extensions.Options;
using SrNotification.Data;
using SrNotification.Infrastructure.Email;
using SrNotification.Infrastructure.Http;
using SrNotification.Infrastructure.Slack;
using SrNotification.NotificationSender;
using SrNotification.NotificationSender.FanOut;
using SrNotification.NotificationSender.Sending;

// Local runs: load POSTGRES_PASSWORD, PUBLIC_URL etc. from the .env file at the repository root.
DotEnvFile.Load();

var builder = Host.CreateApplicationBuilder(args);

if (builder.Configuration["PUBLIC_URL"] is { Length: > 0 } publicUrl)
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [$"{NotificationSenderOptions.SectionName}:{nameof(NotificationSenderOptions.PublicUrl)}"] = publicUrl,
    });
}

var connectionString = DatabasePassword.AddTo(
    builder.Configuration.GetConnectionString("SrNotification")
        ?? throw new InvalidOperationException("Connection string 'SrNotification' is not configured."),
    builder.Configuration[DatabasePassword.VariableName]);

builder.Services.AddSrNotificationData(connectionString);
// Same Data Protection keys as the API, to decrypt the SMTP password and Slack webhooks.
builder.Services.AddSrNotificationDataProtection();

builder.Services
    .AddOptions<NotificationSenderOptions>()
    .Bind(builder.Configuration.GetSection(NotificationSenderOptions.SectionName))
    .Validate(o => o.PollInterval > TimeSpan.Zero, "NotificationSender:PollInterval must be positive.")
    .Validate(o => o.FanOutBatchSize > 0 && o.SendBatchSize > 0, "Batch sizes must be at least 1.")
    .Validate(o => o.MaxItemsPerMessage > 0, "NotificationSender:MaxItemsPerMessage must be at least 1.")
    .Validate(o => o.RetryDelays.All(d => d > TimeSpan.Zero), "NotificationSender:RetryDelays must be positive.")
    .Validate(o => o.ClaimDuration > TimeSpan.FromMinutes(1), "NotificationSender:ClaimDuration must be over a minute.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);

// Webhook URLs come from users: same SSRF guard as the rest of the app. Retries are done by Polly below.
builder.Services
    .AddHttpClient<SlackWebhookClient>(client => client.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => SafeHttpHandler.Create());

builder.Services.AddSendingPipelines();
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<FanOutService>();
builder.Services.AddScoped<DeliveryService>();
builder.Services.AddHostedService<NotificationWorker>();

var host = builder.Build();
await host.RunAsync();
