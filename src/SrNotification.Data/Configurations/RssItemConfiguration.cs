using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SrNotification.Data.Entities;

namespace SrNotification.Data.Configurations;

internal sealed class RssItemConfiguration : IEntityTypeConfiguration<RssItem>
{
    public void Configure(EntityTypeBuilder<RssItem> builder)
    {
        builder.ToTable("RssItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ExternalId).HasMaxLength(FieldLengths.ExternalId).IsRequired();
        builder.Property(i => i.Title).HasMaxLength(FieldLengths.Title).IsRequired();
        builder.Property(i => i.Link).HasMaxLength(FieldLengths.Url);
        builder.Property(i => i.Author).HasMaxLength(FieldLengths.Author);
        builder.Property(i => i.Summary); // text

        // How the reader recognises items it has already stored.
        builder.HasIndex(i => new { i.FeedId, i.ExternalId }).IsUnique();

        // The notification sender will look for recently fetched items.
        builder.HasIndex(i => i.FetchedAt);
    }
}
