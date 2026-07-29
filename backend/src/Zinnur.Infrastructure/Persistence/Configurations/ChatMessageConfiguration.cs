using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ChatMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.SenderName).IsRequired().HasMaxLength(200);

        // Domain'dagi cheklov bilan AYNAN bir xil (ChatMessage.MaxBodyLength = 500).
        builder.Property(m => m.Body).IsRequired().HasMaxLength(ChatMessage.MaxBodyLength);

        builder.HasOne(m => m.Session)
            .WithMany()
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // SenderId uchun FK ATAYLAB YARATILMAYDI.
        // Sabab: 200 kishilik darsda xabar oqimi tez va har INSERT'da Postgres
        // `Users` jadvaliga FK tekshiruvi (satr qulfi bilan) qilardi. Yuboruvchi
        // ismi `SenderName` da denormalizatsiya qilingani uchun o'qishda ham
        // JOIN kerak emas — FK'dan hech qanday foyda yo'q, faqat yozuv narxi.

        // ENG MUHIM CHAT INDEKSI: "oxirgi 50 ta xabar" so'rovi
        // (WHERE SessionId = @s ORDER BY Id DESC LIMIT 50).
        // Kompozit (SessionId, Id) indeks bu so'rovni to'liq qoplaydi —
        // sort ham, filtr ham indeksdan, jadvalga umuman kirilmaydi.
        builder.HasIndex(m => new { m.SessionId, m.Id })
            .HasDatabaseName("IX_ChatMessages_SessionId_Id");
    }
}
