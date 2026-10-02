using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SrNotification.Data.Entities;

namespace SrNotification.Data.Configurations;

internal sealed class RssFeedConfiguration : IEntityTypeConfiguration<RssFeed>
{
    public void Configure(EntityTypeBuilder<RssFeed> builder)
    {
        builder.ToTable("RssFeeds");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Url).HasMaxLength(FieldLengths.Url).IsRequired();
        builder.HasIndex(f => f.Url).IsUnique();

        builder.Property(f => f.Title).HasMaxLength(FieldLengths.Title);
        builder.Property(f => f.SiteUrl).HasMaxLength(FieldLengths.Url);
        builder.Property(f => f.LastError).HasMaxLength(FieldLengths.Error);
        builder.Property(f => f.ETag).HasMaxLength(FieldLengths.ETag);
        builder.Property(f => f.CreatedAt).HasDefaultValueSql("now()");

        builder.HasMany(f => f.Items)
            .WithOne(i => i.Feed)
            .HasForeignKey(i => i.FeedId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
