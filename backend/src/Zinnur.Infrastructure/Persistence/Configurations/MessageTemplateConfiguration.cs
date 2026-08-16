using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>Xabar shabloni (2026-08-16) — o'quv bo'limi Sozlamalar panelidan boshqaradigan lug'at.</summary>
public sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("MessageTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(MessageTemplate.MaxNameLength);
        builder.Property(t => t.Body).IsRequired().HasMaxLength(MessageTemplate.MaxBodyLength);

        // Ro'yxat "faol birinchi, so'ng nomi bo'yicha" o'qiladi — tanlagichda
        // arxivlangan shablon oxirida turadi.
        builder.HasIndex(t => new { t.IsActive, t.Name })
            .HasDatabaseName("IX_MessageTemplates_IsActive_Name");
    }
}
