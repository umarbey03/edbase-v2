using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    /// <summary>
    /// ★ JURNAL SUMMASI DOIM MUSBAT — yo'nalishni <c>Kind</c> aytadi.
    ///
    /// Manfiy summa bilan "teskari" yozuv yozilsa, jamini hisoblovchi
    /// so'rovlarning bir qismi manfiylarni QO'SHIB, bir qismi
    /// (`WHERE Kind = Payment`) ularni umuman ko'rmay qolardi — natijada
    /// kunlik tushum har hisobotda boshqacha chiqardi. Nol ham taqiqlanadi:
    /// "hech nima bo'lmagan" yozuv jurnalni faqat ifloslantiradi.
    /// </summary>
    private const string AmountPositiveCheck = """("Amount" > 0)""";

    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "PaymentTransactions",
            table => table.HasCheckConstraint("CK_PaymentTransactions_Amount_Positive", AmountPositiveCheck));

        builder.HasKey(t => t.Id);

        // ★ PUL — `numeric(18,2)`, `float` EMAS (yig'indi aniq bo'lishi shart:
        // bu jadval kunlik kassa hisobotining YAGONA manbai).
        builder.Property(t => t.Amount)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        // Enum -> int (butun loyihada bir xil uslub).
        builder.Property(t => t.Kind).HasConversion<int>();

        // `ZN-2026-07-000123` = 17 belgi; 32 zaxira bilan (prefiks o'zgarsa).
        builder.Property(t => t.ReceiptNo).HasMaxLength(ReceiptNoMaxLength);

        // Usul ENUM (naqd/karta); balansdan yopish va kechirimda `null`.
        builder.Property(t => t.Method).HasConversion<int?>();
        builder.Property(t => t.Note).HasMaxLength(PaymentConfiguration.NoteMaxLength);

        // O'CHIRISH: Restrict — jurnal PUL TARIXI, o'quvchi bilan birga
        // o'chib ketsa kunlik/oylik tushum hisobotlari ORQAGA QARAB
        // o'zgarardi (o'tgan oy yopilgan kassa raqami bugun boshqa bo'lardi).
        builder.HasOne(t => t.Student)
            .WithMany()
            .HasForeignKey(t => t.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // O'CHIRISH: Restrict, `SetNull` EMAS. `SetNull` bo'lsa guruh
        // o'chirilganda yozuv qolardi-yu, lekin "qaysi guruh uchun to'langan"
        // ma'lumoti jimgina yo'qolardi — nizoda aynan shu savol beriladi.
        builder.HasOne(t => t.Group)
            .WithMany()
            .HasForeignKey(t => t.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // Amalni bajargan xodim — navigatsiyasiz FK. Restrict: `User` ga
        // ishora qiluvchi BARCHA FK'lar Restrict (izchillik).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        // ============================================================
        // ★ KVITANSIYA RAQAMI UNIKAL — FILTRLANGAN indeks.
        //
        // Raqam ota-onaga QOG'OZDA beriladi va nizoda aynan shu bo'yicha
        // qidiriladi. Ikki kvitansiya bir raqam olsa qidiruv ikki yozuv
        // qaytaradi va qaysi biri haqiqiy to'lov ekani noaniq qoladi.
        //
        // Filtr (`IS NOT NULL`) kerak, chunki pul harakati kvitansiyasiz ham
        // bo'ladi (balansdan yopish, kechirim). Postgres bir nechta NULL'ga
        // ruxsat bersa-da, filtr niyatni kodda ochiq ko'rsatadi va indeksni
        // kichraytiradi. Ustun nomi PascalCase bo'lgani uchun TIRNOQ shart.
        // ============================================================
        builder.HasIndex(t => t.ReceiptNo)
            .IsUnique()
            .HasFilter("\"ReceiptNo\" IS NOT NULL")
            .HasDatabaseName("UX_PaymentTransactions_ReceiptNo");

        // O'QUVCHI TARIXI: "shu o'quvchining to'lovlari, yangisidan eskisiga"
        // — to'lov kartochkasidagi asosiy ro'yxat.
        builder.HasIndex(t => new { t.StudentId, t.CreatedAt })
            .HasDatabaseName("IX_PaymentTransactions_StudentId_CreatedAt");

        // KUNLIK KASSA: "bugun qancha tushdi" — sana oralig'i bo'yicha,
        // o'quvchisiz. Yuqoridagi indeks `StudentId` dan boshlangani uchun
        // bu so'rovga yaramaydi.
        builder.HasIndex(t => t.CreatedAt)
            .HasDatabaseName("IX_PaymentTransactions_CreatedAt");
    }

    /// <summary>`ZN-2026-07-000123` — 17 belgi, zaxira bilan 32.</summary>
    private const int ReceiptNoMaxLength = 32;
}
