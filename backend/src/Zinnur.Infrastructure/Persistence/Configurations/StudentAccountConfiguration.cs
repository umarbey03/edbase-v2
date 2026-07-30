using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class StudentAccountConfiguration : IEntityTypeConfiguration<StudentAccount>
{
    /// <summary>
    /// ★ BALANS HECH QACHON MANFIY BO'LMAYDI.
    ///
    /// Manfiy balans — "YASHIRIN QARZ": u qarz hisobotiga tushmaydi (chunki
    /// qarz `Payments` dan hisoblanadi), bloklashga ta'sir qilmaydi va faqat
    /// yil oxirida kassa yig'indisi mos kelmaganda topiladi. `Withdraw`
    /// domain'da 0 dan pastga tushmaydi; bu `CHECK` esa qo'lda `UPDATE`,
    /// ma'lumot ko'chirish yoki kelajakdagi yangi kod uchun oxirgi to'siq.
    /// </summary>
    private const string BalanceNonNegativeCheck = """("Balance" >= 0)""";

    public void Configure(EntityTypeBuilder<StudentAccount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "StudentAccounts",
            table => table.HasCheckConstraint("CK_StudentAccounts_Balance_NonNegative", BalanceNonNegativeCheck));

        builder.HasKey(a => a.Id);

        // ★ PUL — `numeric(18,2)`. Balans o'nlab amallar (deposit/withdraw)
        // orqali o'tadi; `double` bo'lsa har amalda mikroskopik xato
        // to'planib, oxiri "0.0000001 so'm balans" kabi hech qachon nolga
        // tushmaydigan qoldiq qolardi.
        builder.Property(a => a.Balance)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        // ============================================================
        // ★ OPTIMISTIK QULF — BALANS UCHUN AYNIQSA MUHIM.
        //
        // Balans "o'qi -> hisobla -> yoz" ko'rinishida yangilanadi. Ikki
        // amal (masalan yangi to'lov va oylik yozuvlarni balansdan yopish
        // ishi) bir vaqtda ishlasa, ikkinchisi birinchisining natijasini
        // bosib ketardi va PUL YO'QOLARDI — buni keyin faqat qo'lda
        // solishtirish bilan topsa bo'lardi.
        //
        // `xmin` — Postgres'ning har qatorda mavjud tizim ustuni, yangi
        // ustun YARATILMAYDI. `UseXminAsConcurrencyToken()` Npgsql 9 da olib
        // tashlangan; rasmiy almashtiruv shu oshkor yozuv.
        // ============================================================
        builder.Property<uint>("xmin")
            .IsRowVersion()
            .HasColumnName("xmin");

        // O'CHIRISH: Restrict — o'quvchi o'chirilsa balansi (ya'ni markaz
        // qarzdor bo'lgan REAL pul) birga yo'qolmasin. Ota-ona oldindan
        // to'lagan pul kaskad bilan o'chsa, uni qaytarish talab qilinganda
        // tizimda hech qanday iz qolmasdi.
        builder.HasOne(a => a.Student)
            .WithMany()
            .HasForeignKey(a => a.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // ============================================================
        // ★ BIR O'QUVCHIGA BITTA HISOB.
        //
        // Hisob "yo'q bo'lsa yarat" (get-or-create) usulida ochiladi. Ikki
        // parallel to'lov bir vaqtda kelsa kod tekshiruvi ikkalasini ham
        // o'tkazib yuborardi va o'quvchida IKKI balans paydo bo'lardi:
        // pul biriga tushib, ikkinchisi o'qilardi — ya'ni pul "yo'qolgandek"
        // ko'rinardi.
        // ============================================================
        builder.HasIndex(a => a.StudentId)
            .IsUnique()
            .HasDatabaseName("UX_StudentAccounts_StudentId");
    }
}
