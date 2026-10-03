using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SrNotification.Data.Entities;

namespace SrNotification.Data.Configurations;

internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("NotificationDeliveries");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.LastError).HasMaxLength(FieldLengths.Error);

        // Never notify the same user about the same item twice on one channel.
        builder.HasIndex(d => new { d.RssItemId, d.UserId, d.Channel }).IsUnique();
        // The sender picks up pending rows; the admin log lists failed ones newest first.
        builder.HasIndex(d => new { d.Status, d.CreatedAt });
        builder.HasIndex(d => d.UserId);

        builder.HasOne(d => d.RssItem)
            .WithMany()
            .HasForeignKey(d => d.RssItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
