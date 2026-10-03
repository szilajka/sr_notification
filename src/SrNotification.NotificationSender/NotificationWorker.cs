using System.Diagnostics;
using Microsoft.Extensions.Options;
using SrNotification.NotificationSender.FanOut;
using SrNotification.NotificationSender.Sending;

namespace SrNotification.NotificationSender;

/// <summary>Every PollInterval: fan out new items, send due deliveries, clean up old ones.</summary>
public sealed class NotificationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationSenderOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Notification sender started: polling every {PollInterval}", options.Value.PollInterval);

        using var timer = new PeriodicTimer(options.Value.PollInterval, timeProvider);
        do
        {
            await RunCycleAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));

        logger.LogInformation("Notification sender stopped");
    }

    private async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var fanOut = scope.ServiceProvider.GetRequiredService<FanOutService>();
            var delivery = scope.ServiceProvider.GetRequiredService<DeliveryService>();

            var created = await fanOut.RunAsync(stoppingToken);
            var result = await delivery.RunAsync(stoppingToken);
            await delivery.PurgeAsync(stoppingToken);

            if (created > 0 || result != DeliveryService.CycleResult.Empty)
            {
                logger.LogInformation(
                    "Cycle done in {Elapsed} ms: {Created} new delivery(ies); {Sent} sent, {Retrying} to retry, {Failed} failed, {Skipped} skipped",
                    stopwatch.ElapsedMilliseconds, created, result.Sent, result.Retrying, result.Failed, result.Skipped);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            // e.g. the database is unreachable: try again next tick instead of stopping the host
            logger.LogError(ex, "Notification cycle failed");
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
