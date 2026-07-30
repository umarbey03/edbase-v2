using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class StudentDiscountConfiguration : IEntityTypeConfiguration<StudentDiscount>
{
    /// <summary>
    /// Chegirma qiymati MUSBAT, foizli chegirma esa 100 dan oshmaydi.
    ///
    /// <c>0</c> yoki manfiy qiymat "chegirma" bo'lib turib narxni OSHIRARDI;
    /// 100 dan katta foiz esa yakuniy summani manfiyga tushirardi (markaz
    /// o'quvchiga qarzdor). Domain'da bu <c>Validate()</c> da, lekin qo'lda
    /// kiritilgan qator uchun oxirgi to'siq shu yerda.
    ///
    /// <c>"Kind" &lt;&gt; 0</c> — <c>DiscountKind.Percent = 0</c> (enum bazada
    /// int sifatida saqlanadi). Ya'ni: "foizli bo'lmasa cheklov yo'q,
    /// foizli bo'lsa 100 dan oshmasin".
    /// </summary>
    private const string ValueRangeCheck =
        """("Value" > 0 AND ("Kind" <> 0 OR "Value" <= 100))""";

    /// <summary>
    /// Tugash sanasi boshlanishdan oldin bo'lmaydi.
    ///
    /// Teskari oraliqli chegirma HECH QACHON amal qilmaydi
    /// (<c>IsActiveOn</c> doim <c>false</c>), lekin ro'yxatda "amaldagi
    /// chegirma" bo'lib ko'rinardi — xodim ota-onaga chegirma va'da qilib,
    /// keyin to'liq summa chiqardi.
    /// </summary>
    private const string ValidRangeCheck =
        """("ValidTo" IS NULL OR "ValidTo" >= "ValidFrom")""";

    public void Configure(EntityTypeBuilder<StudentDiscount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StudentDiscounts", table =>
        {
            table.HasCheckConstraint("CK_StudentDiscounts_Value_Range", ValueRangeCheck);
            table.HasCheckConstraint("CK_StudentDiscounts_Valid_Range", ValidRangeCheck);
        });

        builder.HasKey(d => d.Id);

        // Enum -> int (butun loyihada bir xil uslub). Yuqoridagi `CHECK` ham
        // aynan shu int qiymatga tayanadi.
        builder.Property(d => d.Kind).HasConversion<int>();

        // ★ `Value` — foiz YOKI pul summasi, ikkalasi ham `numeric(18,2)`.
        // Foiz uchun 2 kasr yetarli (12.5%), summa uchun aniqlik shart.
        builder.Property(d => d.Value)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        // Mahalliy KALENDAR sanalari — chegirma muddati kun aniqligida.
        builder.Property(d => d.ValidFrom).HasColumnType("date");
        builder.Property(d => d.ValidTo).HasColumnType("date");

        builder.Property(d => d.Reason).HasMaxLength(ReasonMaxLength);

        // Hisoblanuvchi property (guruhga atalgani ustunroq) — ustun EMAS.
        builder.Ignore(d => d.Specificity);

        // O'CHIRISH: Restrict — chegirma o'quvchining moliyaviy shartnomasi
        // qismi. O'chirilsa o'tmishdagi oylarda "nega 400 000 to'lagan?"
        // savoliga javob qolmasdi.
        builder.HasOne(d => d.Student)
            .WithMany()
            .HasForeignKey(d => d.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // O'CHIRISH: Restrict, `SetNull` EMAS — bu yerda `SetNull` XAVFLI:
        // bitta guruhga atalgan chegirma guruh o'chganda o'quvchining BARCHA
        // guruhlariga tarqalib ketardi va markaz jimgina pul yo'qotardi.
        builder.HasOne(d => d.Group)
            .WithMany()
            .HasForeignKey(d => d.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // OYLIK HISOB-KITOB: har oy yozuvlarini yaratishda HAR o'quvchi uchun
        // "amaldagi chegirmalari" so'raladi — eng issiq so'rov.
        // `StudentId` boshda turgani uchun EF alohida FK indeksi yaratmaydi.
        builder.HasIndex(d => new { d.StudentId, d.IsActive })
            .HasDatabaseName("IX_StudentDiscounts_StudentId_IsActive");
    }

    /// <summary>Chegirma sababi ("ko'p bolali oila", "aka-uka").</summary>
    private const int ReasonMaxLength = 500;
}
