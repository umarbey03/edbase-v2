using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class AttendanceAuditConfiguration : IEntityTypeConfiguration<AttendanceAudit>
{
    public void Configure(EntityTypeBuilder<AttendanceAudit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AttendanceAudits");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.OldStatus).HasConversion<int>();
        builder.Property(a => a.NewStatus).HasConversion<int>();

        builder.Property(a => a.OldReason).HasMaxLength(AttendanceConfiguration.ReasonMaxLength);
        builder.Property(a => a.NewReason).HasMaxLength(AttendanceConfiguration.ReasonMaxLength);

        // ★ DAVOMAT QATORI o'chsa audit ham ketadi (Cascade).
        //
        // Nima uchun `Restrict` EMAS (moliya auditidan farqli): davomat
        // qatori faqat DARS yoki O'QUVCHI o'chganda o'chadi, ular esa
        // o'z navbatida Cascade/Restrict bilan himoyalangan. `Restrict`
        // qo'yilsa, bekor qilingan darsni o'chirish audit tufayli
        // butunlay imkonsiz bo'lardi va o'quv bo'limi jadvalni tozalay
        // olmasdi. Pul izidan farqli o'laroq, o'chirilgan darsning
        // davomat tarixi hech kimga kerak emas — dars o'zi yo'q.
        builder.HasOne(a => a.Attendance)
            .WithMany()
            .HasForeignKey(a => a.AttendanceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Dars va o'quvchi — navigatsiyasiz FK.
        //
        // ★ `Restrict`, chunki `SessionId` bo'yicha allaqachon Cascade yo'l
        // bor (`AttendanceAudits -> Attendances -> LiveSessions`). Ikkinchi
        // Cascade yo'li qo'shilsa Postgres "multiple cascade paths" bilan
        // migratsiyani rad etardi.
        builder.HasOne<LiveSession>()
            .WithMany()
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Tuzatgan xodim — izchillik: `User` ga ishora qiluvchi barcha FK
        // `Restrict` (xodim o'chirilsa ham iz KIM ekanini yo'qotmasin).
        builder.HasOne(a => a.Actor)
            .WithMany()
            .HasForeignKey(a => a.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        // "SHU DARSDA nima o'zgardi" — ustoz panelidagi davomat jadvali
        // ochilganda har katakning oxirgi tuzatishi shu indeks bo'yicha
        // topiladi.
        builder.HasIndex(a => new { a.SessionId, a.StudentId })
            .HasDatabaseName("IX_AttendanceAudits_SessionId_StudentId");

        // "SHU O'QUVCHI bo'yicha nima bo'lgan, vaqt tartibida" — davomat
        // nizosi tekshiruvidagi birinchi so'rov.
        builder.HasIndex(a => new { a.StudentId, a.CreatedAt })
            .HasDatabaseName("IX_AttendanceAudits_StudentId_CreatedAt");
    }
}
