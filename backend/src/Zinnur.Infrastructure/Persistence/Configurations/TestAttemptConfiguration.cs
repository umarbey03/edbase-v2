using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class TestAttemptConfiguration : IEntityTypeConfiguration<TestAttempt>
{
    public void Configure(EntityTypeBuilder<TestAttempt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TestAttempts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Status).HasConversion<int>();

        // BALL — `decimal(6,2)`, `float` EMAS.
        builder.Property(a => a.Score)
            .HasPrecision(AssignmentConfiguration.ScorePrecision, AssignmentConfiguration.ScoreScale);

        builder.Property(a => a.MaxScore)
            .HasPrecision(AssignmentConfiguration.ScorePrecision, AssignmentConfiguration.ScoreScale);

        builder.Ignore(a => a.IsSubmitted);
        builder.Ignore(a => a.Percent);

        // ============================================================
        // ★ OPTIMISTIK QULF — BIR VAQTDA IKKI TOPSHIRISHGA QARSHI.
        //
        // Ssenariy: o'quvchi "Yakunlash" tugmasini ikki marta bosdi (yoki
        // tarmoq qayta yubordi). Ikki so'rov ham AYNI `InProgress` urinishni
        // o'qiydi va ikkalasi ham `UPDATE ... SET Status='Submitted'` yozadi.
        // Qulfsiz ikkinchisi jimgina o'tib ketardi va javob qatorlari IKKI
        // marta yozilardi (ball ikki barobar yoki 500 xato).
        //
        // `xmin` — Postgres'ning har qatorda mavjud tizim ustuni (yangi ustun
        // YARATILMAYDI). EF uni `WHERE xmin = @original` shartiga qo'shadi:
        // yutqazgan so'rov 0 qator yangilaydi -> `DbUpdateConcurrencyException`
        // -> servis 409 qaytaradi. Ikkinchi himoya —
        // `UX_TestAnswers_AttemptId_QuestionId_OptionId`.
        //
        // `UseXminAsConcurrencyToken()` yordamchisi o'rniga OSHKOR yozuv:
        // Npgsql'ning rasmiy tavsiyasi aynan shu shakl.
        // ============================================================
        builder.Property<uint>("xmin")
            .IsRowVersion()
            .HasColumnName("xmin");

        builder.HasOne(a => a.Test)
            .WithMany()
            .HasForeignKey(a => a.TestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Student)
            .WithMany()
            .HasForeignKey(a => a.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // ============================================================
        // ★ BIR TEST BIR MARTA — UNIKAL.
        //
        // `POST /tests/{id}/start` ikki marta parallel kelsa kod tekshiruvi
        // ("urinish bormi?") ikkalasini ham o'tkazardi va o'quvchi ikkita
        // urinishga ega bo'lardi — natijalar ro'yxatida u ikki marta
        // ko'rinardi va yaxshi ballini tanlash imkoni paydo bo'lardi.
        // ============================================================
        builder.HasIndex(a => new { a.TestId, a.StudentId })
            .IsUnique()
            .HasDatabaseName("UX_TestAttempts_TestId_StudentId");

        // Natijalar ro'yxati: test bo'yicha topshirilganlar.
        builder.HasIndex(a => new { a.TestId, a.Status })
            .HasDatabaseName("IX_TestAttempts_TestId_Status");

        // Gating: "shu o'quvchi qaysi testlarni topshirgan".
        builder.HasIndex(a => new { a.StudentId, a.Status })
            .HasDatabaseName("IX_TestAttempts_StudentId_Status");
    }
}
