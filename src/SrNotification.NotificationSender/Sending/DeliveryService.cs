using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using SrNotification.Data;
using SrNotification.Data.Entities;
using SrNotification.Infrastructure.Email;
using SrNotification.Infrastructure.Slack;
using SrNotification.NotificationSender.Messages;

namespace SrNotification.NotificationSender.Sending;

/// <summary>
/// Step 2 of a cycle: claims due Pending deliveries, groups them into one message per user and channel,
/// sends through the Polly pipelines and records the outcome (Sent, retry later, Failed or Skipped).
/// </summary>
public sealed class DeliveryService(
    IDbContextFactory<SrNotificationDbContext> dbContextFactory,
    SecretProtector secretProtector,
    EmailService emailService,
    SlackWebhookClient slack,
    ResiliencePipelineProvider<string> pipelines,
    IOptions<NotificationSenderOptions> options,
    TimeProvider timeProvider,
    ILogger<DeliveryService> logger)
{
    private readonly NotificationSenderOptions _options = options.Value;

    public sealed record CycleResult(int Sent, int Retrying, int Failed, int Skipped)
    {
        public static CycleResult Empty { get; } = new(0, 0, 0, 0);
        public CycleResult Add(CycleResult other) =>
            new(Sent + other.Sent, Retrying + other.Retrying, Failed + other.Failed, Skipped + other.Skipped);
    }

    public async Task<CycleResult> RunAsync(CancellationToken cancellationToken)
    {
        var total = CycleResult.Empty;

        // One SMTP connection for the whole cycle, opened only if an email is due.
        SmtpSession? smtp = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var claimed = await ClaimAsync(cancellationToken);
                if (claimed.Count == 0)
                {
                    break;
                }

                var (result, session) = await SendBatchAsync(claimed, smtp, cancellationToken);
                smtp = session;
                total = total.Add(result);

                if (claimed.Count < _options.SendBatchSize)
                {
                    break;
                }
            }
        }
        finally
        {
            if (smtp is not null)
            {
                await smtp.DisposeAsync();
            }
        }

        return total;
    }

    /// <summary>
    /// Reserves due rows by pushing their NextAttemptAt forward, in one statement. SKIP LOCKED lets
    /// several sender instances work side by side; if this one crashes, the rows become due again
    /// when the claim runs out.
    /// </summary>
    private async Task<List<long>> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var claimUntil = now + _options.ClaimDuration;
        var pending = nameof(DeliveryStatus.Pending);
        var batchSize = _options.SendBatchSize;

        return await db.Database.SqlQuery<long>($"""
            UPDATE "NotificationDeliveries" SET "NextAttemptAt" = {claimUntil}
            WHERE "Id" IN (
                SELECT "Id" FROM "NotificationDeliveries"
                WHERE "Status" = {pending} AND "NextAttemptAt" <= {now}
                ORDER BY "NextAttemptAt", "Id"
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id" AS "Value"
            """).ToListAsync(cancellationToken);
    }

    private async Task<(CycleResult, SmtpSession?)> SendBatchAsync(
        List<long> ids, SmtpSession? smtp, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var deliveries = await db.NotificationDeliveries
            .Where(d => ids.Contains(d.Id))
            .Include(d => d.User)
            .Include(d => d.RssItem).ThenInclude(i => i.Feed)
            .ToListAsync(cancellationToken);

        var userIds = deliveries.Select(d => d.UserId).Distinct().ToList();
        var feedIds = deliveries.Select(d => d.RssItem.FeedId).Distinct().ToList();
        var subscriptions = (await db.Subscriptions.AsNoTracking()
                .Where(s => userIds.Contains(s.UserId) && feedIds.Contains(s.FeedId))
                .ToListAsync(cancellationToken))
            .ToDictionary(s => (s.UserId, s.FeedId));

        var enabledChannels = (await db.ChannelSettings.AsNoTracking()
                .Where(c => c.IsEnabled).Select(c => c.Channel).ToListAsync(cancellationToken))
            .ToHashSet();

        var result = CycleResult.Empty;
        foreach (var group in deliveries.GroupBy(d => (d.UserId, d.Channel)))
        {
            var (groupResult, session) = await SendGroupAsync(
                group.Key.Channel, group.ToList(), subscriptions, enabledChannels, smtp, cancellationToken);
            smtp = session;
            result = result.Add(groupResult);

            await db.SaveChangesAsync(cancellationToken); // keep progress even if a later group fails
        }

        return (result, smtp);
    }

    private async Task<(CycleResult, SmtpSession?)> SendGroupAsync(
        NotificationChannel channel,
        List<NotificationDelivery> group,
        Dictionary<(int UserId, int FeedId), Subscription> subscriptions,
        HashSet<NotificationChannel> enabledChannels,
        SmtpSession? smtp,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var user = group[0].User;
        int skipped = 0;

        // Check again: the user or an admin may have switched things off since fan-out.
        var toSend = new List<(NotificationDelivery Delivery, Subscription Subscription)>();
        foreach (var delivery in group)
        {
            subscriptions.TryGetValue((delivery.UserId, delivery.RssItem.FeedId), out var subscription);
            var stillWanted = enabledChannels.Contains(channel) && subscription is not null &&
                              (channel == NotificationChannel.Email ? subscription.EmailEnabled : subscription.SlackEnabled) &&
                              (channel == NotificationChannel.Email
                                  ? user.EffectiveNotificationEmail is not null
                                  : user.SlackWebhookUrlProtected is not null);
            if (stillWanted)
            {
                toSend.Add((delivery, subscription!));
            }
            else
            {
                delivery.Status = DeliveryStatus.Skipped;
                delivery.LastAttemptAt = now;
                skipped++;
            }
        }

        if (toSend.Count == 0)
        {
            return (new CycleResult(0, 0, 0, skipped), smtp);
        }

        var items = toSend
            .OrderBy(x => x.Delivery.RssItem.PublishedAt ?? x.Delivery.RssItem.FetchedAt)
            .Select(x => new DigestItem(
                FeedName: x.Subscription.Name ?? x.Delivery.RssItem.Feed.Title ?? HostOf(x.Delivery.RssItem.Feed.Url),
                Title: x.Delivery.RssItem.Title,
                Link: x.Delivery.RssItem.Link,
                Summary: x.Delivery.RssItem.Summary,
                PublishedAt: x.Delivery.RssItem.PublishedAt,
                FetchedAt: x.Delivery.RssItem.FetchedAt))
            .ToList();

        try
        {
            if (channel == NotificationChannel.Email)
            {
                smtp ??= await OpenSmtpAsync(cancellationToken);
                var message = DigestComposer.ComposeEmail(
                    user.EffectiveNotificationEmail!, items, _options.MaxItemsPerMessage, _options.PublicUrl);
                var session = smtp;
                await pipelines.GetPipeline(ResiliencePipelines.Email)
                    .ExecuteAsync(async ct => await session.SendAsync(message, ct), cancellationToken);
            }
            else
            {
                var webhook = secretProtector.Unprotect(user.SlackWebhookUrlProtected!);
                var text = DigestComposer.ComposeSlack(items, _options.MaxItemsPerMessage, _options.PublicUrl);
                await pipelines.GetPipeline(ResiliencePipelines.Slack)
                    .ExecuteAsync(async ct => await slack.SendAsync(webhook, text, ct), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutting down: the claim runs out and the rows are picked up again
        }
        catch (Exception ex)
        {
            return (RecordFailure(toSend.Select(x => x.Delivery), ex, now, user.Id, channel) with { Skipped = skipped }, smtp);
        }

        foreach (var (delivery, _) in toSend)
        {
            delivery.Status = DeliveryStatus.Sent;
            delivery.Attempts++;
            delivery.LastAttemptAt = now;
            delivery.SentAt = now;
            delivery.LastError = null;
        }

        logger.LogInformation("Sent {Count} item(s) to user {UserId} by {Channel}", toSend.Count, user.Id, channel);
        return (new CycleResult(toSend.Count, 0, 0, skipped), smtp);
    }

    private CycleResult RecordFailure(
        IEnumerable<NotificationDelivery> deliveries, Exception exception, DateTimeOffset now, int userId, NotificationChannel channel)
    {
        var error = Truncate(DeliveryErrors.Describe(exception), FieldLengths.Error);
        var permanent = DeliveryErrors.IsPermanent(exception);
        int retrying = 0, failed = 0;

        foreach (var delivery in deliveries)
        {
            delivery.LastAttemptAt = now;
            delivery.LastError = error;

            if (exception is BrokenCircuitException)
            {
                // The channel is paused (see ResiliencePipelines); this wasn't a real attempt, so don't
                // use up a retry. The break lasts a minute.
                delivery.NextAttemptAt = now + TimeSpan.FromMinutes(1);
                retrying++;
                continue;
            }

            delivery.Attempts++;
            if (permanent || delivery.Attempts >= _options.MaxAttempts)
            {
                delivery.Status = DeliveryStatus.Failed;
                failed++;
            }
            else
            {
                delivery.NextAttemptAt = now + _options.EffectiveRetryDelays[delivery.Attempts - 1];
                retrying++;
            }
        }

        logger.LogWarning(exception,
            "Sending by {Channel} to user {UserId} failed ({Outcome})",
            channel, userId, failed > 0 ? "gave up" : "will retry");

        return new CycleResult(0, retrying, failed, 0);
    }

    private async Task<SmtpSession> OpenSmtpAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.SmtpSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == SmtpSettings.SingletonId, cancellationToken)
            ?? throw new EmailNotConfiguredException();

        return new SmtpSession(emailService.ToConnection(settings));
    }

    private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>Deletes old Sent/Skipped and Failed rows.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var sentCutoff = now - _options.SentRetention;
        var failedCutoff = now - _options.FailedRetention;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.NotificationDeliveries
            .Where(d => ((d.Status == DeliveryStatus.Sent || d.Status == DeliveryStatus.Skipped) && d.CreatedAt < sentCutoff) ||
                        (d.Status == DeliveryStatus.Failed && d.CreatedAt < failedCutoff))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
