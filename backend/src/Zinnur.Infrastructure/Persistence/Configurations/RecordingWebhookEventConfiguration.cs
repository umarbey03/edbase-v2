using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class RecordingWebhookEventConfiguration
    : IEntityTypeConfiguration<RecordingWebhookEvent>
{
    public void Configure(EntityTypeBuilder<RecordingWebhookEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RecordingWebhookEvents");

        builder.HasKey(e => e.EventId);

        // ★ `ValueGeneratedNever` MAJBURIY (`TelegramUpdateConfiguration`
        //   dagi AYNI tuzoq): kalit BIZNIKI EMAS — uni LiveKit beradi yoki
        //   biz tana xeshidan yasaymiz. EF uni o'zi generatsiya qiladigan
        //   deb hisoblasa, bizning qiymatimiz tashlab yuborilardi va takror
        //   himoyasi UMUMAN ishlamasdi — jimgina.
        builder.Property(e => e.EventId)
            .ValueGeneratedNever()
            .HasMaxLength(RecordingWebhookEvent.MaxEventIdLength);

        builder.Property(e => e.ReceivedAt).IsRequired();

        // Eski izlarni davriy o'chirish uchun (jadval cheksiz o'smasin).
        builder.HasIndex(e => e.ReceivedAt)
            .HasDatabaseName("IX_RecordingWebhookEvents_ReceivedAt");
    }
}
