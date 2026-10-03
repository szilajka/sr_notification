using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SrNotification.Data.Entities;

namespace SrNotification.Data;

public class SrNotificationDbContext(DbContextOptions<SrNotificationDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<RssFeed> Feeds => Set<RssFeed>();
    public DbSet<RssItem> Items => Set<RssItem>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ChannelSetting> ChannelSettings => Set<ChannelSetting>();
    public DbSet<SmtpSettings> SmtpSettings => Set<SmtpSettings>();
    public DbSet<FeedFetchError> FeedFetchErrors => Set<FeedFetchError>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    /// <summary>
    /// ASP.NET Core Data Protection keys. Stored in the database so the API (sign-in cookies, encrypted
    /// secrets) and the notification sender (decrypting the SMTP password and Slack webhooks) share them,
    /// and so they survive container restarts.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SrNotificationDbContext).Assembly);
    }
}
