using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SrNotification.Data.Entities;

namespace SrNotification.Data.Configurations;

internal sealed class FeedFetchErrorConfiguration : IEntityTypeConfiguration<FeedFetchError>
{
    public void Configure(EntityTypeBuilder<FeedFetchError> builder)
    {
        builder.ToTable("FeedFetchErrors");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Message).HasMaxLength(FieldLengths.Error).IsRequired();

        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.FeedId, e.OccurredAt });

        builder.HasOne(e => e.Feed)
            .WithMany()
            .HasForeignKey(e => e.FeedId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
