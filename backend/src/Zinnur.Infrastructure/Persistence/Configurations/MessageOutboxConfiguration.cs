using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Application.Notifications;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class MessageOutboxConfiguration : IEntityTypeConfiguration<MessageOutbox>
{
    /// <summary>Kanal ichidagi manzil (Telegram <c>chat_id</c> — 20 belgigacha).</summary>
    public const int AddressMaxLength = 128;

    /// <summary>Xabar turi kodi: <c>lesson_reminder</c>.</summary>
    public const int TemplateKeyMaxLength = 64;

    /// <summary>Takrorlanishga qarshi kalit: <c>lesson_reminder:45:123</c>.</summary>
    public const int IdempotencyKeyMaxLength = 128;

    /// <summary>Oxirgi xato matni — logga to'liq, bazaga qisqartirilgan holda.</summary>
    public const int LastErrorMaxLength = 500;

    /// <summary>Inline tugmalar uchun kodlangan ma'lumot (bir necha qator tugma sig'ishi uchun).</summary>
    public const int CallbackDataMaxLength = 2000;

    public void Configure(EntityTypeBuilder<MessageOutbox> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("MessageOutbox");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.RecipientAddress).HasMaxLength(AddressMaxLength);
        builder.Property(m => m.TemplateKey).IsRequired().HasMaxLength(TemplateKeyMaxLength);

        // Matn uzunligi Telegram chegarasi bilan AYNAN bir xil: undan uzun
        // xabar baribir yuborilmasdi, shuning uchun u navbatga umuman
        // tushmasligi kerak — xato yozuv paytida, ya'ni sababi ko'rinib
        // turgan joyda chiqsin.
        builder.Property(m => m.Body).IsRequired().HasMaxLength(NotificationText.MaxBodyLength);

        builder.Property(m => m.LastError).HasMaxLength(LastErrorMaxLength);

        builder.Property(m => m.CallbackData).HasMaxLength(CallbackDataMaxLength);

        builder.Property(m => m.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(IdempotencyKeyMaxLength);

        // O'CHIRISH: Restrict — barcha `User` ga havolalar bilan izchil.
        // Navbatdagi yozuv "kimga yuborildi" savolining dalili, va
        // foydalanuvchi baribir o'chirilmaydi (`IsActive` bilan yopiladi).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ═════════════════════════════════════════════════════════════
        // ★ UNIKAL: takror xabar BAZA darajasida to'siladi.
        //
        // Koddagi tekshiruv ikki jarayon orasidagi poygada ishlamaydi:
        // ikkalasi ham "yo'q ekan" deb ko'radi va ikkalasi ham yozadi.
        // Bu indeks — yagona ishonchli to'siq.
        // ═════════════════════════════════════════════════════════════
        builder.HasIndex(m => m.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_MessageOutbox_IdempotencyKey");

        // ═════════════════════════════════════════════════════════════
        // ★ NAVBATNI TANLASH INDEKSI — QISMAN (partial).
        //
        // Worker so'rovi AYNAN shunday:
        //   WHERE "Status" = 0 AND "NextAttemptAt" <= now
        //   ORDER BY "NextAttemptAt", "Id" LIMIT n FOR UPDATE SKIP LOCKED
        //
        // Jadvalning 99% i vaqt o'tishi bilan `Sent` bo'ladi. To'liq indeks
        // o'sha yuborilgan millionlab qatorni ham saqlab, har yozuvda
        // yangilanardi. `WHERE Status = 0` li qisman indeks esa faqat
        // KUTAYOTGANLARNI ushlaydi: u kichik, keshda turadi va xabar
        // yuborilishi bilan indeksdan CHIQIB ketadi.
        //
        // Filtr enum RAQAMI bilan yozilgan (`Pending` = 0) — Postgres
        // filtrni matn sifatida saqlaydi va u C# nomlarini bilmaydi.
        // ═════════════════════════════════════════════════════════════
        builder.HasIndex(m => new { m.NextAttemptAt, m.Id })
            .HasFilter(""" "Status" = 0 """)
            .HasDatabaseName("IX_MessageOutbox_Pending");

        // "Shu o'quvchiga nima ketgan" — qo'llab-quvvatlash savoli.
        builder.HasIndex(m => new { m.RecipientUserId, m.CreatedAt })
            .HasDatabaseName("IX_MessageOutbox_Recipient_CreatedAt");
    }
}
