using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SrNotification.Data.Entities;

namespace SrNotification.Data.Configurations;

internal sealed class SmtpSettingsConfiguration : IEntityTypeConfiguration<SmtpSettings>
{
    public void Configure(EntityTypeBuilder<SmtpSettings> builder)
    {
        builder.ToTable("SmtpSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Host).HasMaxLength(255).IsRequired();
        builder.Property(s => s.Security).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Username).HasMaxLength(FieldLengths.Email);
        builder.Property(s => s.PasswordProtected); // text
        builder.Property(s => s.FromAddress).HasMaxLength(FieldLengths.Email).IsRequired();
        builder.Property(s => s.FromName).HasMaxLength(FieldLengths.Name);
        builder.Property(s => s.UpdatedBy).HasMaxLength(FieldLengths.Email);
    }
}
