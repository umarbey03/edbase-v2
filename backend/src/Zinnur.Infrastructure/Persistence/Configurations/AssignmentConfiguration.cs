using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    /// <summary>
    /// Vazifa YOKI guruhga, YOKI kurs darsiga biriktiriladi — ikkalasiga emas
    /// va hech qaysisiga ham emas.
    ///
    /// NIMA UCHUN BAZADA HAM: qoida <c>Assignment.Validate()</c> da bor, lekin
    /// u faqat BIZNING kodimizdan o'tgan yozuvni tekshiradi. Ma'lumot
    /// ko'chirish skripti, qo'lda `INSERT` yoki kelajakdagi yangi servis
    /// tekshiruvni chetlab o'tishi mumkin. `CHECK` esa bazaning o'zida —
    /// uni chetlab o'tishning yo'li yo'q.
    ///
    /// SQL'dagi <c>&lt;&gt;</c> (XOR) ikkita NULL-holat mos KELMASLIGINI talab
    /// qiladi: (bor, yo'q) yoki (yo'q, bor) — o'tadi; (bor, bor) va (yo'q, yo'q)
    /// — rad etiladi.
    /// </summary>
    private const string OneTargetCheck = """("GroupId" IS NULL) <> ("ModuleLessonId" IS NULL)""";

    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "Assignments",
            table => table.HasCheckConstraint("CK_Assignments_GroupXorLesson", OneTargetCheck));

        builder.HasKey(a => a.Id);

        // Uzunlik chegaralari Domain doimiylaridan olinadi — ikki joyda
        // (validatsiya va sxema) boshqa-boshqa raqam bo'lib qolmasin.
        builder.Property(a => a.Title).IsRequired().HasMaxLength(Assignment.MaxTitleLength);
        builder.Property(a => a.Description).HasMaxLength(Assignment.MaxDescriptionLength);

        // OBYEKT KALITI, to'liq URL emas: presigned URL muddati tugaydi,
        // shuning uchun bazada faqat kalit yashaydi (SubmissionFile.ObjectKey ham).
        builder.Property(a => a.ImageKey).HasMaxLength(ObjectKeyMaxLength);

        // BAHO — `decimal(6,2)`, `float` EMAS (MAJBURIY QOIDA 1).
        // 6,2 = 9999.99 gacha: 5 ballik ham, 100 ballik ham sig'adi.
        builder.Property(a => a.MaxScore).HasPrecision(ScorePrecision, ScoreScale);

        // Bayroqlar birlashmasi (Text|Image|Audio) -> int.
        builder.Property(a => a.AllowedFormats).HasConversion<int>();

        /* ===== R33 · SHU VAZIFANING TEKSHIRUVCHISI (guruhdan ISTISNO) ===== */

        // NULL = istisno yo'q, guruh sozlamasi ishlaydi. Shu sababli ustun
        // `int?` va standart qiymati YO'Q — "tanlanmagan" holat bazada ham
        // ANIQ ko'rinib turadi. Standartli `NOT NULL` bo'lganda "guruhdan
        // meros" bilan "ataylab guruh bilan bir xil qilib qo'yildi" ni
        // ajratib bo'lmasdi, ya'ni guruh sozlamasi keyin o'zgarsa vazifa
        // unga ERGASHMASDI.
        builder.Property(a => a.GraderRole).HasConversion<int?>();

        // ★ `CHECK` QO'SHILMADI ("faqat guruh vazifasida"), garchi qo'shni
        // `CK_Assignments_GroupXorLesson` shunday qilingan bo'lsa ham.
        // Sabab: u cheklov MA'LUMOT YAXLITLIGI haqida (nishonsiz vazifa
        // butun tizimni buzadi), bu esa SIYOSAT — ertaga o'quv bo'limi
        // "kurs vazifasida ham bo'lsin" desa `CHECK` migratsiya talab
        // qilardi. Qoida `Assignment.Validate()` da va u yagona yozish
        // yo'lida turibdi.

        /* ===== /R33 ===== */

        // Hisoblanuvchi property — ustun emas.
        builder.Ignore(a => a.IsCourseAssignment);

        builder.HasOne(a => a.Group)
            .WithMany()
            .HasForeignKey(a => a.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.ModuleLesson)
            .WithMany()
            .HasForeignKey(a => a.ModuleLessonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Muallif — navigatsiyasiz FK. Restrict: foydalanuvchi hech qachon
        // o'chirilmaydi (`IsActive=false` qilinadi), shuning uchun User'ga
        // ishora qiluvchi BARCHA FK'lar Restrict — izchillik uchun.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        // "Shu darsning vazifasi bormi" — gating va kurs daraxti HAR SAFAR
        // shu ustun bo'yicha izlaydi.
        builder.HasIndex(a => a.ModuleLessonId)
            .HasDatabaseName("IX_Assignments_ModuleLessonId");

        // "Guruhimning vazifalari, muddati bo'yicha" — o'quvchi ro'yxati.
        builder.HasIndex(a => new { a.GroupId, a.DueAt })
            .HasDatabaseName("IX_Assignments_GroupId_DueAt");
    }

    /// <summary>Barcha modullar uchun bir xil: baho `decimal(6,2)`.</summary>
    internal const int ScorePrecision = 6;

    internal const int ScoreScale = 2;

    /// <summary>R2 obyekt kaliti (`submissions/2026-07/ab12....jpg`).</summary>
    internal const int ObjectKeyMaxLength = 500;
}
