using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class LessonGradeConfiguration : IEntityTypeConfiguration<LessonGrade>
{
    /// <summary>
    /// BAHO INVARIANTI BAZANING O'ZIDA.
    ///
    /// Qoida <c>LessonGrade.Apply</c> da ham bor, lekin u faqat BIZNING
    /// kodimizdan o'tgan yozuvni tekshiradi. Ma'lumot ko'chirish skripti,
    /// qo'lda `INSERT` yoki kelajakdagi yangi servis uni chetlab o'tishi
    /// mumkin — `CHECK` esa bazada, uni chetlab o'tishning yo'li yo'q.
    ///
    /// NIMA UCHUN MUHIM: <c>Score &gt; MaxScore</c> bo'lsa foiz 100 dan
    /// oshadi va reytingdagi "har mezon 0..100" invarianti buziladi —
    /// yakuniy ball 100 dan katta chiqib, jadval ma'nosini yo'qotardi.
    ///
    /// 🔴 SQL ICHIDAGI `5` — <see cref="LessonGrade.DefaultMaxScore"/>
    /// ning NUSXASI. Infrastructure SQL satri ichidan C# doimiysiga
    /// havola qila olmaydi (`AttendanceService.ReasonMaxLength` bilan
    /// AYNI mulohaza). Ikkalasi BIRGA o'zgartiriladi, aks holda baza kod
    /// ruxsat bergan qiymatni rad etadi.
    /// </summary>
    /// <remarks>
    /// <c>ReplaceLineEndings("\n")</c> SHART: bu raw string literal fayldagi
    /// qator oxiri belgisini (CRLF/LF) AYNAN saqlaydi. `core.autocrlf=true`
    /// bo'lgan Windows checkout'da CRLF bo'ladi, migratsiya esa LF bilan
    /// yaratilgan edi — natijada `EF Core` ikkalasini boshqa SQL deb hisoblab
    /// <c>PendingModelChangesWarning</c> bilan ilovani ko'tarmasdi.
    /// </remarks>
    private static readonly string ScoreCheck =
        """
        "Score" >= 0
        AND ("MaxScore" IS NULL OR "MaxScore" > 0)
        AND "Score" <= COALESCE("MaxScore", 5)
        """.ReplaceLineEndings("\n");

    public void Configure(EntityTypeBuilder<LessonGrade> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "LessonGrades",
            table => table.HasCheckConstraint("CK_LessonGrades_Score", ScoreCheck));

        builder.HasKey(g => g.Id);

        // BAHO — `decimal(6,2)`, `float` EMAS. Aniqlik `Assignment.MaxScore`
        // bilan AYNI: ikki mezon bir xil shkalada o'qilishi kerak.
        builder.Property(g => g.Score)
            .HasPrecision(AssignmentConfiguration.ScorePrecision, AssignmentConfiguration.ScoreScale);

        builder.Property(g => g.MaxScore)
            .HasPrecision(AssignmentConfiguration.ScorePrecision, AssignmentConfiguration.ScoreScale);

        builder.Property(g => g.Comment).HasMaxLength(LessonGrade.MaxCommentLength);

        // Hisoblanuvchi property'lar — ustun EMAS.
        builder.Ignore(g => g.EffectiveMaxScore);
        builder.Ignore(g => g.Percent);

        builder.HasOne(g => g.Session)
            .WithMany()
            .HasForeignKey(g => g.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // `User` ga ishora qiluvchi BARCHA FK — `Restrict` (izchillik:
        // foydalanuvchi o'chirilmaydi, `IsActive=false` qilinadi).
        builder.HasOne(g => g.Student)
            .WithMany()
            .HasForeignKey(g => g.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.GradedBy)
            .WithMany()
            .HasForeignKey(g => g.GradedById)
            .OnDelete(DeleteBehavior.Restrict);

        // BITTA dars + BITTA o'quvchi = BITTA baho.
        //
        // Kod darajasidagi "bormi?" tekshiruvi yetarli emas: ustoz va o'quv
        // bo'limi bir katakni bir vaqtda BIRINCHI marta baholasa ikkita
        // qator paydo bo'lardi va matritsa qaysi birini ko'rsatishi
        // tasodifga bog'liq bo'lardi. Indeks ikkinchi INSERT'ni bazada rad
        // etadi, servis esa uni 409 ga aylantiradi.
        builder.HasIndex(g => new { g.SessionId, g.StudentId })
            .IsUnique()
            .HasDatabaseName("UX_LessonGrades_SessionId_StudentId");

        // "O'quvchining baho tarixi" — profil paneli va reyting hisobi.
        builder.HasIndex(g => g.StudentId).HasDatabaseName("IX_LessonGrades_StudentId");
    }
}
