using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SrNotification.Data.Entities;

namespace SrNotification.Data.Configurations;

internal sealed class ChannelSettingConfiguration : IEntityTypeConfiguration<ChannelSetting>
{
    public void Configure(EntityTypeBuilder<ChannelSetting> builder)
    {
        builder.ToTable("ChannelSettings");
        builder.HasKey(c => c.Channel);

        builder.Property(c => c.Channel)
            .HasConversion<string>()
            .HasMaxLength(20)
            .ValueGeneratedNever();
        builder.Property(c => c.UpdatedBy).HasMaxLength(FieldLengths.Email);

        // Both channels exist from the start and are switched on.
        builder.HasData(
            new ChannelSetting { Channel = NotificationChannel.Email, IsEnabled = true },
            new ChannelSetting { Channel = NotificationChannel.Slack, IsEnabled = true });
    }
}
