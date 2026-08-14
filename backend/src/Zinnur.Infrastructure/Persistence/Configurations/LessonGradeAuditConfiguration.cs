using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class LessonGradeAuditConfiguration : IEntityTypeConfiguration<LessonGradeAudit>
{
    public void Configure(EntityTypeBuilder<LessonGradeAudit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("LessonGradeAudits");
        builder.HasKey(a => a.Id);

        // Aniqlik baho qatoridagi bilan AYNI — eski qiymat yangi qiymatga
        // sig'masligi mumkin bo'lgan holat bo'lmasin.
        builder.Property(a => a.OldScore)
            .HasPrecision(AssignmentConfiguration.ScorePrecision, AssignmentConfiguration.ScoreScale);
        builder.Property(a => a.NewScore)
            .HasPrecision(AssignmentConfiguration.ScorePrecision, AssignmentConfiguration.ScoreScale);
        builder.Property(a => a.OldMaxScore)
            .HasPrecision(AssignmentConfiguration.ScorePrecision, AssignmentConfiguration.ScoreScale);
        builder.Property(a => a.NewMaxScore)
            .HasPrecision(AssignmentConfiguration.ScorePrecision, AssignmentConfiguration.ScoreScale);

        builder.Property(a => a.OldComment).HasMaxLength(LessonGrade.MaxCommentLength);
        builder.Property(a => a.NewComment).HasMaxLength(LessonGrade.MaxCommentLength);

        // ★ DARS o'chsa iz ham ketadi — YAGONA cascade yo'l.
        //
        // `AttendanceAudits` da bu FK `Restrict` edi, chunki u yerda
        // `AttendanceId` orqali IKKINCHI cascade yo'li bor edi va Postgres
        // "multiple cascade paths" bilan migratsiyani rad etardi. Bu yerda
        // baho qatoriga FK UMUMAN YO'Q (sabab `LessonGradeAudit` izohida:
        // baho o'chirilganda ham iz QOLISHI kerak), ya'ni ikkinchi yo'l
        // ham yo'q va cascade xavfsiz.
        //
        // Nima uchun `Restrict` EMAS: o'chirilgan darsning baho tarixi
        // hech kimga kerak emas (dars o'zi yo'q), `Restrict` esa o'quv
        // bo'limiga jadvalni tozalashni umuman imkonsiz qilardi.
        builder.HasOne<LiveSession>()
            .WithMany()
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // O'quvchi va xodim — `User` ga ishora qiluvchi BARCHA FK
        // `Restrict` (odam o'chirilsa ham iz KIM ekanini yo'qotmasin).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Actor)
            .WithMany()
            .HasForeignKey(a => a.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        // "SHU DARSDA nima o'zgardi" — baho varag'ining tarixi.
        builder.HasIndex(a => new { a.SessionId, a.StudentId })
            .HasDatabaseName("IX_LessonGradeAudits_SessionId_StudentId");

        // "SHU O'QUVCHI bo'yicha nima bo'lgan, vaqt tartibida" — baho
        // nizosi tekshiruvidagi birinchi so'rov.
        builder.HasIndex(a => new { a.StudentId, a.CreatedAt })
            .HasDatabaseName("IX_LessonGradeAudits_StudentId_CreatedAt");
    }
}
