using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SrNotification.Data.Entities;

namespace SrNotification.Data.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.ExternalId).HasMaxLength(FieldLengths.UserExternalId).IsRequired();
        builder.HasIndex(u => u.ExternalId).IsUnique();

        builder.Property(u => u.Email).HasMaxLength(FieldLengths.Email);
        builder.Property(u => u.DisplayName).HasMaxLength(FieldLengths.Name);
        builder.Property(u => u.NotificationEmail).HasMaxLength(FieldLengths.Email);
        builder.Property(u => u.PendingNotificationEmail).HasMaxLength(FieldLengths.Email);
        builder.Property(u => u.PendingEmailTokenHash).HasMaxLength(FieldLengths.TokenHash);
        builder.HasIndex(u => u.PendingEmailTokenHash);
        builder.Property(u => u.SlackWebhookUrlProtected); // text

        builder.Ignore(u => u.EffectiveNotificationEmail);

        builder.HasMany(u => u.Subscriptions)
            .WithOne(s => s.User)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
