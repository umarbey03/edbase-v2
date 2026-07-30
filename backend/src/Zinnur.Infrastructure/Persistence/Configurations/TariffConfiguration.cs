using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class TariffConfiguration : IEntityTypeConfiguration<Tariff>
{
    /// <summary>
    /// Narx MANFIY bo'lmaydi.
    ///
    /// Manfiy tarif butun zanjirni buzardi: <c>BaseAmount</c> manfiy bo'lsa
    /// chegirma hisobi ham, <c>PaidAmount &lt;= Amount</c> cheklovi ham
    /// ma'nosini yo'qotardi va o'quvchi "manfiy qarz" bilan yurardi.
    /// Nol ruxsat etiladi — bepul o'qish real holat.
    /// </summary>
    private const string AmountNonNegativeCheck = """("Amount" >= 0)""";

    /// <summary>
    /// Oylik darslar soni 1..60.
    ///
    /// Bu raqam kvitansiyada bosiladi va "bir dars necha so'm" hisobida
    /// maxraj bo'ladi — 0 bo'lsa nolga bo'lish, absurd katta bo'lsa esa
    /// kvitansiyadagi narx yolg'on chiqardi. Chegara
    /// <c>Tariff.Validate()</c> bilan AYNAN bir xil.
    /// </summary>
    private const string LessonsCountRangeCheck =
        """("LessonsCount" >= 1 AND "LessonsCount" <= 60)""";

    public void Configure(EntityTypeBuilder<Tariff> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Tariffs", table =>
        {
            table.HasCheckConstraint("CK_Tariffs_Amount_NonNegative", AmountNonNegativeCheck);
            table.HasCheckConstraint("CK_Tariffs_LessonsCount_Range", LessonsCountRangeCheck);
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(NameMaxLength);

        // ★ PUL — `numeric(18,2)`. Tarif — barcha hisob-kitobning boshlanish
        // nuqtasi; bu yerdagi aniqlik yo'qolsa quyidagi hamma raqam noto'g'ri.
        builder.Property(t => t.Amount)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        // Mahalliy KALENDAR sanasi (vaqt zonasi yo'q) — `timestamptz` emas.
        // "1-avgustdan yangi narx" degani Toshkent kalendari bo'yicha.
        builder.Property(t => t.ActiveFrom).HasColumnType("date");

        // Hisoblanuvchi property (guruh > kurs > umumiy tartibi) — ustun EMAS.
        builder.Ignore(t => t.Specificity);

        // O'CHIRISH: Restrict — tarif NARX TARIXI. Kurs o'chirilganda uning
        // narx qatorlari kaskad bilan ketsa, "nega bu oy 540 000 edi?" degan
        // savolga javob qoladigan yagona hujjat yo'qolardi.
        // DIQQAT: `SetNull` ATAYLAB ISHLATILMADI — `CourseId = NULL` tarif
        // UMUMIY tarifga aylanadi, ya'ni bitta kursning narxi jimgina
        // BARCHA kurslarga tarqalib ketardi.
        builder.HasOne(t => t.Course)
            .WithMany()
            .HasForeignKey(t => t.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        // O'CHIRISH: Restrict — sabab yuqoridagi bilan bir xil (guruhga
        // atalgan narx guruh o'chganda hammaga tarqab ketmasin).
        builder.HasOne(t => t.Group)
            .WithMany()
            .HasForeignKey(t => t.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // QO'SHIMCHA INDEKS YO'Q — ataylab.
        // Tarif jadvali o'nlab qatordan iborat (markazda bir nechta narx),
        // `CourseId`/`GroupId` uchun EF konvensiyasi FK indekslarini
        // O'ZI yaratadi. Keraksiz indeks har yozuvda narx qo'shimcha xarajat.
    }

    /// <summary>Tarif nomi ("Standart", "ATF kengaytirilgan").</summary>
    private const int NameMaxLength = 200;
}
