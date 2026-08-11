using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Dars mediasi (video qismi / imtihon rasmi).
///
/// ★ CHECK CONSTRAINT — DARS TURI BILAN MOSLIK BAZADA HAM TEKSHIRILADI.
/// Qoida <c>ModuleLesson.EnsureAssetKindAllowed</c> da bor, lekin u faqat
/// BIZNING kodimizdan o'tgan yozuvni ushlaydi. Ma'lumot ko'chirish skripti,
/// qo'lda `INSERT` yoki kelajakdagi yangi servis uni chetlab o'tishi mumkin.
///
/// ⚠️ LEKIN: `CHECK` bitta jadval ichida BOSHQA jadvalga (`ModuleLessons`)
/// murojaat QILA OLMAYDI — Postgres buni ruxsat etmaydi. Shuning uchun bu
/// yerdagi `CHECK` faqat <c>Kind</c> ning O'ZI to'g'ri diapazonda ekanini
/// tekshiradi; dars turiga MOSLIK esa faqat Domain'da qo'riqlanadi
/// (izoh: <see cref="Zinnur.Domain.Entities.LessonAsset"/>).
/// </summary>
public sealed class LessonAssetConfiguration : IEntityTypeConfiguration<LessonAsset>
{
    /// <summary>
    /// Noma'lum enum qiymati bazaga tushmasin: `Kind` faqat 0 (Video) yoki
    /// 1 (Image). Yangi qiymat qo'shilsa BU RAQAM ham yangilanishi kerak —
    /// shuning uchun u nomlangan doimiylardan hisoblanadi.
    /// </summary>
    /// ⚠️ KO'P QATORLI xom satr (raw string) ATAYLAB: bir qatorli shaklda
    /// (<c>""""Kind" ..."""</c>) ochuvchi ajratgich boshidagi BARCHA
    /// qo'shtirnoqlarni yutib yuboradi va SQL <c>Kind" IN (0, 1)</c> bo'lib
    /// chiqadi — muvozanatsiz qo'shtirnoq, ya'ni migratsiya QO'LLANGANDA
    /// sintaksis xatosi. (Bu xato shu yerda haqiqatan yuz bergan va
    /// migratsiya SQL'ini o'qish orqali ushlangan.)
    private const string KindRangeCheck =
        """
        "Kind" IN (0, 1)
        """;

    public void Configure(EntityTypeBuilder<LessonAsset> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "LessonAssets",
            table => table.HasCheckConstraint("CK_LessonAssets_Kind", KindRangeCheck));

        builder.HasKey(a => a.Id);

        // OBYEKT KALITI, to'liq URL EMAS — sabab `SubmissionFileConfiguration`
        // da batafsil (presigned havola muddatli, bazadagi URL o'lardi).
        builder.Property(a => a.ObjectKey)
            .IsRequired()
            .HasMaxLength(AssignmentConfiguration.ObjectKeyMaxLength);

        builder.Property(a => a.ContentType)
            .IsRequired()
            .HasMaxLength(ContentTypeMaxLength);

        builder.Property(a => a.Title).HasMaxLength(LessonAsset.MaxTitleLength);

        builder.Property(a => a.Kind).HasConversion<int>();

        builder.HasOne(a => a.Lesson)
            .WithMany(l => l.Assets)
            .HasForeignKey(a => a.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Muallif — navigatsiyasiz FK. `Restrict`: foydalanuvchi hech qachon
        // o'chirilmaydi (`IsActive=false` qilinadi), shuning uchun User'ga
        // ishora qiluvchi BARCHA FK'lar `Restrict` — izchillik uchun.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        // "Shu darsning mediasi, tartib bo'yicha" — kurs daraxti va o'quvchi
        // pleyeri HAR SAFAR aynan shu so'rovni yuboradi.
        //
        // ⚠️ UNIKAL EMAS: `(LessonId, Position)` ga unikal indeks qo'yilsa
        // tartibni almashtirish IMKONSIZ bo'lardi — EF `UPDATE` larni
        // qatorma-qator yuboradi va oraliq holatda ikki qator bir xil
        // raqamga tushadi (aynan shu sabab `ModuleLessons` da ham
        // takrorlangan; batafsil: `CourseService` izohi).
        builder.HasIndex(a => new { a.LessonId, a.Position })
            .HasDatabaseName("IX_LessonAssets_LessonId_Position");
    }

    /// <summary>`video/quicktime` kabi qiymatlar uchun yetarli.</summary>
    private const int ContentTypeMaxLength = 100;
}
