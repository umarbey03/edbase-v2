using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Submissions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Text).HasMaxLength(Submission.MaxTextLength);
        builder.Property(s => s.Feedback).HasMaxLength(Submission.MaxFeedbackLength);
        builder.Property(s => s.ResubmitNote).HasMaxLength(ResubmitNoteMaxLength);

        builder.Property(s => s.Status).HasConversion<int>();

        // BAHO — `decimal(6,2)`, `float` EMAS.
        builder.Property(s => s.Score)
            .HasPrecision(AssignmentConfiguration.ScorePrecision, AssignmentConfiguration.ScoreScale);

        builder.Ignore(s => s.IsGraded);

        // ============================================================
        // OPTIMISTIK QULF (`xmin` — Postgres tizim ustuni).
        //
        // NIMA UCHUN: ustoz baho qo'yayotganda o'quvchi ayni o'sha
        // sekundda qayta topshirsa, ikkinchi `UPDATE` birinchisining
        // natijasini JIMGINA yo'q qilardi (baho boshqa javobga tegib
        // qolardi). `xmin` bilan yutqazgan so'rov
        // `DbUpdateConcurrencyException` oladi va servis uni 409 ga
        // aylantiradi — foydalanuvchi qaytadan urinadi.
        //
        // Ustun YARATILMAYDI: `xmin` har Postgres jadvalida allaqachon bor.
        //
        // `UseXminAsConcurrencyToken()` yordamchisi o'rniga OSHKOR yozuv:
        // Npgsql'ning rasmiy tavsiyasi aynan shu shakl.
        // ============================================================
        builder.Property<uint>("xmin")
            .IsRowVersion()
            .HasColumnName("xmin");

        builder.HasOne(s => s.Assignment)
            .WithMany(a => a.Submissions)
            .HasForeignKey(s => s.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Student)
            .WithMany()
            .HasForeignKey(s => s.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Baho qo'ygan xodim — navigatsiyasiz FK.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.GradedById)
            .OnDelete(DeleteBehavior.Restrict);

        // ============================================================
        // ★ BIR VAZIFAGA BITTA JAVOB — UNIKAL.
        //
        // "Bir marta topshirish" qoidasi Domain'da (`Submission.Resubmit`
        // ruxsatsiz yiqiladi), lekin BIRINCHI topshirish ikki marta parallel
        // kelsa kod tekshiruvi ikkalasini ham o'tkazib yuborardi va bazada
        // ikkita javob paydo bo'lardi (baholash qaysi birini ko'rishi
        // tasodifga qolardi). Indeks — oxirgi va ishonchli himoya.
        // ============================================================
        builder.HasIndex(s => new { s.AssignmentId, s.StudentId })
            .IsUnique()
            .HasDatabaseName("UX_Submissions_AssignmentId_StudentId");

        // "Mening javoblarim" (o'quvchi ro'yxati) va baholanmaganlar navbati.
        builder.HasIndex(s => new { s.StudentId, s.Status })
            .HasDatabaseName("IX_Submissions_StudentId_Status");

        // Ustozning "baholash kerak" ro'yxati: vazifa bo'yicha + holat.
        builder.HasIndex(s => new { s.AssignmentId, s.Status })
            .HasDatabaseName("IX_Submissions_AssignmentId_Status");
    }

    private const int ResubmitNoteMaxLength = 500;
}
