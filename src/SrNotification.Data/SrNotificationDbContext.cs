using Microsoft.EntityFrameworkCore;
using SrNotification.Data.Entities;

namespace SrNotification.Data;

public class SrNotificationDbContext(DbContextOptions<SrNotificationDbContext> options) : DbContext(options)
{
    public DbSet<RssFeed> Feeds => Set<RssFeed>();
    public DbSet<RssItem> Items => Set<RssItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SrNotificationDbContext).Assembly);
    }
}
