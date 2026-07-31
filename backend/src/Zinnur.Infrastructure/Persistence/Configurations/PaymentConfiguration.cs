using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    /// <summary>
    /// Summalar MANFIY bo'lmaydi.
    ///
    /// NIMA UCHUN BAZADA HAM: qoida <c>Payment.Validate()</c> da bor, lekin u
    /// faqat BIZNING kodimizdan o'tgan yozuvni tekshiradi. Eski bazadan
    /// ma'lumot ko'chirish skripti, qo'lda `UPDATE` yoki kelajakdagi yangi
    /// servis uni chetlab o'tishi mumkin — pul jadvalida esa bitta buzuq
    /// qator butun qarz hisobotini yolg'on qiladi. `CHECK` bazaning o'zida,
    /// uni chetlab o'tishning yo'li YO'Q.
    /// </summary>
    private const string AmountsNonNegativeCheck =
        """("Amount" >= 0 AND "BaseAmount" >= 0 AND "DiscountAmount" >= 0)""";

    /// <summary>
    /// ★ CHEGIRMA TARIF SUMMASIDAN OSHMAYDI.
    ///
    /// Aks holda <c>Amount = BaseAmount - DiscountAmount</c> manfiyga tushib,
    /// markaz o'quvchiga QARZDOR bo'lib qolardi: "200 000 chegirma" 150 000 lik
    /// oyga qo'llansa hisobot -50 000 ko'rsatardi va bu son yig'indilarga
    /// jimgina aralashib ketardi.
    /// </summary>
    private const string DiscountWithinBaseCheck =
        """("DiscountAmount" <= "BaseAmount")""";

    /// <summary>
    /// ★ ENG QIMMAT ESKI XATONING BAZADAGI QULFI: <c>0 &lt;= PaidAmount &lt;= Amount</c>.
    ///
    /// Eski tizim qisman to'lovni ham "to'liq to'langan" qilib yozardi
    /// (100 000 so'm 540 000 lik oyni yopardi) va teskarisi ham bo'lardi —
    /// oyga summasidan ORTIQ pul yozilib, ortiqcha qism hech qayerda
    /// ko'rinmasdi. Bu <c>CHECK</c> ikkala yo'nalishni ham yopadi:
    /// ortiqcha pul faqat <c>StudentAccount.Balance</c> ga tushishi mumkin.
    /// </summary>
    private const string PaidWithinAmountCheck =
        """("PaidAmount" >= 0 AND "PaidAmount" <= "Amount")""";

    /// <summary>
    /// ★ UCH SUMMA MOS BO'LISHI SHART: <c>Amount = BaseAmount − DiscountAmount</c>.
    ///
    /// Busiz <c>BaseAmount=600 000, DiscountAmount=60 000, Amount=999 999</c>
    /// kabi qator qolgan barcha tekshiruvlardan o'tib ketardi. Moliya hisoboti
    /// aynan shu uch ustunga tayanadi ("tarif bo'yicha kutilgan tushum",
    /// "berilgan chegirma", "to'lanishi kerak bo'lgan summa") — ular mos
    /// kelmasa hisobot jimgina uydirmaga aylanadi.
    ///
    /// Qoida <c>Payment.Validate()</c> dagi bilan BIR XIL: Domain xatoni
    /// tushunarli xabar bilan darrov aytadi, baza esa oxirgi himoya —
    /// qo'lda yozilgan SQL ham o'tolmaydi.
    /// </summary>
    private const string AmountConsistencyCheck =
        """("Amount" = "BaseAmount" - "DiscountAmount")""";

    /// <summary>
    /// Davr formati QAT'IY <c>YYYY-MM</c> (oy ikki xonali, 01..12).
    ///
    /// NIMA UCHUN: "eng eski qarz birinchi yopiladi" tartibi shu ustunning
    /// SATR bo'yicha taqqoslanishiga tayanadi. Bitta joyda oldiga nol
    /// qo'yilmasa (<c>2026-7</c>) satr taqqoslashda u <c>2026-12</c> dan
    /// KEYIN turadi va pul noto'g'ri oyga tushadi — eski tizimdagi haqiqiy
    /// nosozlik. <c>BillingPeriod</c> buni kodda qulflaydi, regex esa bazada.
    /// </summary>
    private const string PeriodFormatCheck =
        """("Period" ~ '^[0-9]{4}-(0[1-9]|1[0-2])$')""";

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Payments", table =>
        {
            table.HasCheckConstraint("CK_Payments_Amounts_NonNegative", AmountsNonNegativeCheck);
            table.HasCheckConstraint("CK_Payments_Discount_WithinBase", DiscountWithinBaseCheck);
            table.HasCheckConstraint("CK_Payments_Paid_WithinAmount", PaidWithinAmountCheck);
            table.HasCheckConstraint("CK_Payments_Amount_Consistent", AmountConsistencyCheck);
            table.HasCheckConstraint("CK_Payments_Period_Format", PeriodFormatCheck);
        });

        builder.HasKey(p => p.Id);

        // DAVR — `varchar(7)`, `char(7)` EMAS.
        // Postgres'da `char(n)` qiymatni bo'shliq bilan TO'LDIRADI (bpchar);
        // qiymat doim aniq 7 belgi bo'lgani uchun bu foyda bermaydi, lekin
        // to'ldirilgan satr .NET tomonida `"2026-07 "` bo'lib qaytishi va
        // `==` taqqoslashni jimgina buzishi mumkin edi.
        builder.Property(p => p.Period).IsRequired().HasMaxLength(PeriodMaxLength);

        // ★ PUL — `numeric(18,2)`. `float`/`double` PUL UCHUN YAROQSIZ:
        // ikkilik kasr 0.1 ni aniq saqlay olmaydi va oylar yig'indisi jamiga
        // teng bo'lmay qolardi. Aniqlik `ConfigureConventions` da ham
        // qo'yilgan, bu yerda OSHKOR takrorlangan: pul ustunining turi
        // konvensiya faylini ochmasdan ko'rinib tursin.
        builder.Property(p => p.Amount).HasPrecision(MoneyPrecision, MoneyScale);
        builder.Property(p => p.BaseAmount).HasPrecision(MoneyPrecision, MoneyScale);
        builder.Property(p => p.DiscountAmount).HasPrecision(MoneyPrecision, MoneyScale);
        builder.Property(p => p.PaidAmount).HasPrecision(MoneyPrecision, MoneyScale);

        // Enum -> int (butun loyihada BIR XIL uslub). Matn sifatida saqlansa
        // enum a'zosi nomi o'zgarganda bazadagi eski qatorlar o'qilmay qolardi.
        builder.Property(p => p.Status).HasConversion<int>();

        // To'lov usuli ENUM (naqd/karta) va bazada `int` — mavjud enum'lar
        // bilan bir xil. Erkin satr bo'lganda kunlik kassa usul bo'yicha
        // bo'linmay qolardi (eski tizim xatosi).
        builder.Property(p => p.Method).HasConversion<int?>();
        builder.Property(p => p.Note).HasMaxLength(NoteMaxLength);

        // Hisoblanuvchi property'lar — ustun EMAS.
        // `PeriodValue` ayniqsa muhim: u `BillingPeriod` struct'i, EF uni
        // murakkab tur sifatida xaritalashga urinib model qurilishida yiqilardi.
        builder.Ignore(p => p.Outstanding);
        builder.Ignore(p => p.IsOpen);
        builder.Ignore(p => p.PeriodValue);

        // ============================================================
        // ★ OPTIMISTIK QULF (`xmin` — Postgres tizim ustuni, yangi ustun
        // YARATILMAYDI).
        //
        // Ssenariy: kassada ikki xodim (yoki ikki marta bosilgan tugma) ayni
        // bir oy yozuvini o'qiydi — ikkalasi ham `PaidAmount = 0` ko'radi va
        // ikkalasi ham `PaidAmount = 540 000` yozadi. Qulfsiz ikkinchi
        // `UPDATE` birinchisini jimgina bosib ketardi: 1 080 000 so'm kelgan
        // bo'lsa ham bazada 540 000 turardi va farq YO'QOLARDI.
        //
        // `xmin` bilan yutqazgan so'rov 0 qator yangilaydi ->
        // `DbUpdateConcurrencyException` -> servis 409 qaytaradi va xodim
        // qaytadan urinadi.
        //
        // `UseXminAsConcurrencyToken()` yordamchisi Npgsql 9 da OLIB
        // TASHLANGAN — rasmiy almashtiruv aynan shu oshkor yozuv.
        // ============================================================
        builder.Property<uint>("xmin")
            .IsRowVersion()
            .HasColumnName("xmin");

        // O'CHIRISH: Restrict — o'quvchi o'chirilsa PUL TARIXI birga
        // yo'qolmasin. O'quvchi amalda hech qachon o'chirilmaydi
        // (`IsActive = false` qilinadi), shuning uchun bu cheklov kundalik
        // ishga xalaqit bermaydi, lekin xato `DELETE` ni bazada to'xtatadi.
        builder.HasOne(p => p.Student)
            .WithMany()
            .HasForeignKey(p => p.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // O'CHIRISH: Restrict — guruh o'chirilganda uning to'lov yozuvlari
        // KASKAD bilan ketsa, o'sha oylardagi tushum hisobotdan jimgina
        // yo'qolardi va kassa balansi tarixiy hisobotga mos kelmay qolardi.
        // Guruhni o'chirish uchun avval moliya yozuvlari bilan nima
        // qilinishi ONGLI ravishda hal qilinishi kerak.
        builder.HasOne(p => p.Group)
            .WithMany()
            .HasForeignKey(p => p.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // Oxirgi o'zgartirgan xodim — navigatsiyasiz FK.
        // Restrict: `User` ga ishora qiluvchi BARCHA FK'lar Restrict (izchillik).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.MarkedById)
            .OnDelete(DeleteBehavior.Restrict);

        // ============================================================
        // ★ BIR O'QUVCHI + BIR GURUH + BIR OY = BITTA YOZUV.
        //
        // Oylik yozuvlarni yaratuvchi ish (rejalashtirilgan job yoki qo'lda
        // "oyni ochish") ikki marta ishga tushsa — konteyner qayta ko'tarildi,
        // xodim tugmani ikki marta bosdi — kod darajasidagi "bormi?"
        // tekshiruvi ikkala urinishni ham o'tkazib yuborardi. Natijada bitta
        // oy IKKI marta hisoblanib, o'quvchi ikki barobar qarzdor bo'lib
        // ko'rinardi va bloklanardi. Indeks — oxirgi va ishonchli himoya.
        // ============================================================
        builder.HasIndex(p => new { p.StudentId, p.GroupId, p.Period })
            .IsUnique()
            .HasDatabaseName("UX_Payments_StudentId_GroupId_Period");

        // QARZ HISOBI: "shu o'quvchining yopilmagan oylari" — bloklash qoidasi
        // (`PaymentBlockPolicy`) har HTTP so'rovda shu so'rovni bajaradi,
        // shuning uchun indekssiz butun jadval skan qilinardi.
        builder.HasIndex(p => new { p.StudentId, p.Status })
            .HasDatabaseName("IX_Payments_StudentId_Status");

        // OYLIK HISOBOT: "iyul oyi bo'yicha tushum/qarz" — davr bo'yicha
        // butun markaz kesimi.
        builder.HasIndex(p => p.Period)
            .HasDatabaseName("IX_Payments_Period");

        // GURUH KESIMI: "shu guruhning iyul oyi" — ustoz/akademik bo'lim
        // ro'yxati. Unikal indeks `StudentId` dan boshlangani uchun uni
        // bu so'rovga ishlata olmaydi.
        builder.HasIndex(p => new { p.GroupId, p.Period })
            .HasDatabaseName("IX_Payments_GroupId_Period");

        // ============================================================
        // QARZ YOSHI (moliya dashboard'i): "bugungi holatda ochiq qolgan
        // barcha oylar" — davr filtriga bog'liq EMAS, ya'ni butun jadvalni
        // holat bo'yicha kesadi.
        //
        // NIMA UCHUN ALOHIDA INDEKS: yuqoridagi `IX_Payments_StudentId_Status`
        // `StudentId` dan boshlanadi va bitta o'quvchi uchun ishlaydi; qarz
        // yoshi esa MARKAZ bo'yicha yig'adi, shuning uchun undan foydalana
        // olmaydi va `Seq Scan` ga tushardi.
        //
        // `INCLUDE` ustunlari ATAYLAB: ular bilan so'rov `Index Only Scan`
        // ga aylanadi (`Heap Fetches: 0`) — jadval sahifalariga umuman
        // murojaat qilinmaydi. 72 000 qatorda o'lchangan: 4.39 ms -> 0.71 ms.
        // ============================================================
        builder.HasIndex(p => new { p.Status, p.Period })
            .IncludeProperties(p => new { p.Amount, p.PaidAmount, p.StudentId })
            .HasDatabaseName("IX_Payments_Status_Period");
    }

    /// <summary>
    /// PUL uchun YAGONA aniqlik: <c>numeric(18,2)</c>. 18 xona so'mda
    /// istalgan real summaga yetadi, 2 kasr esa tiyinni saqlaydi (amalda
    /// ishlatilmasa ham, foizli chegirma oraliq natijasi uchun kerak).
    /// Barcha moliya konfiguratsiyalari SHU doimiylardan foydalanadi —
    /// ikki jadvalda ikki xil aniqlik bo'lib qolmasin.
    /// </summary>
    internal const int MoneyPrecision = 18;

    internal const int MoneyScale = 2;

    /// <summary><c>YYYY-MM</c> — aniq 7 belgi.</summary>
    internal const int PeriodMaxLength = 7;

    /// <summary>To'lov usuli: `cash`, `card`, `transfer`.</summary>

    /// <summary>Xodim izohi.</summary>
    internal const int NoteMaxLength = 500;
}
