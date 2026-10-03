using System.Diagnostics;
using Microsoft.Extensions.Options;
using SrNotification.RssReader.Feeds;

namespace SrNotification.RssReader;

/// <summary>
/// Background job: runs a refresh cycle immediately on startup, then every PollInterval.
/// Each cycle only fetches feeds whose RefreshInterval has elapsed (see <see cref="FeedSchedule"/>),
/// so feeds added through the UI are picked up within one poll interval.
/// </summary>
public sealed class RssReaderWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RssReaderOptions> options,
    TimeProvider timeProvider,
    ILogger<RssReaderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.PollInterval;
        logger.LogInformation(
            "RSS reader started: polling every {PollInterval}, refreshing feeds every {RefreshInterval}",
            interval, options.Value.RefreshInterval);

        using var timer = new PeriodicTimer(interval, timeProvider);

        do
        {
            await RunCycleAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));

        logger.LogInformation("RSS reader stopped");
    }

    private async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var refresher = scope.ServiceProvider.GetRequiredService<FeedRefresher>();

            var summary = await refresher.RefreshDueFeedsAsync(stoppingToken);
            await refresher.PurgeOldErrorsAsync(stoppingToken);

            if (summary.FeedsDue > 0)
            {
                logger.LogInformation(
                    "Refresh cycle done in {Elapsed} ms: {Due} feed(s) due, {Succeeded} ok, {Failed} failed, {NewItems} new item(s)",
                    stopwatch.ElapsedMilliseconds, summary.FeedsDue, summary.FeedsSucceeded,
                    summary.FeedsFailed, summary.NewItems);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            // e.g. the database is unreachable: log and try again next tick instead of killing the host
            logger.LogError(ex, "Refresh cycle failed");
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
