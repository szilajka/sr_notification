using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SrNotification.Data.Entities;

namespace SrNotification.Data.Configurations;

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(FieldLengths.Name);

        // A user follows a feed at most once.
        builder.HasIndex(s => new { s.UserId, s.FeedId }).IsUnique();
        builder.HasIndex(s => s.FeedId);

        builder.HasOne(s => s.Feed)
            .WithMany(f => f.Subscriptions)
            .HasForeignKey(s => s.FeedId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
