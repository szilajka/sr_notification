using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace SrNotification.NotificationSender.Sending;

/// <summary>
/// Polly pipelines for sending. These are the quick, in-process retries; longer outages are handled by
/// the retry schedule stored in the database (see <see cref="NotificationSenderOptions.RetryDelays"/>).
/// </summary>
public static class ResiliencePipelines
{
    public const string Email = "email";
    public const string Slack = "slack";

    public static IServiceCollection AddSendingPipelines(this IServiceCollection services)
    {
        services.AddResiliencePipeline(Email, builder => Configure(builder));
        services.AddResiliencePipeline(Slack, builder => Configure(builder));
        return services;
    }

    private static void Configure(ResiliencePipelineBuilder builder)
    {
        var transient = new PredicateBuilder().Handle<Exception>(DeliveryErrors.IsTransient);

        builder
            // 1. A couple of quick retries with exponential backoff and jitter.
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = transient,
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            })
            // 2. If most sends on this channel fail, stop trying for a minute instead of hammering a
            //    server that is down; the deliveries stay queued.
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = transient,
                FailureRatio = 0.5,
                MinimumThroughput = 4,
                SamplingDuration = TimeSpan.FromMinutes(2),
                BreakDuration = TimeSpan.FromMinutes(1),
            })
            // 3. Each attempt gets at most 30 seconds.
            .AddTimeout(TimeSpan.FromSeconds(30));
    }
}
