using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SrNotification.Data;
using SrNotification.Data.Entities;

namespace SrNotification.RssReader;

internal static class DatabaseStartup
{
    /// <summary>
    /// Applies pending EF Core migrations (when Database:ApplyMigrationsOnStartup is true) and
    /// inserts the configured seed feeds. Runs once before the worker starts.
    /// </summary>
    public static async Task InitializeAsync(IHost host, CancellationToken cancellationToken = default)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseStartup));
        var dbFactory = services.GetRequiredService<IDbContextFactory<SrNotificationDbContext>>();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync(cancellationToken);
        }

        var seedFeeds = services.GetRequiredService<IOptions<RssReaderOptions>>().Value.SeedFeeds
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (seedFeeds.Count == 0)
        {
            return;
        }

        var existing = await db.Feeds
            .Where(f => seedFeeds.Contains(f.Url))
            .Select(f => f.Url)
            .ToListAsync(cancellationToken);

        var missing = seedFeeds.Except(existing, StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.Feeds.AddRange(missing.Select(url => new RssFeed { Url = url, CreatedAt = now }));
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded {Count} feed(s): {Feeds}", missing.Count, string.Join(", ", missing));
    }
}
