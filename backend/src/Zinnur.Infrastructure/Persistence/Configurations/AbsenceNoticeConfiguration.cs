using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Kelmagan o'quvchiga yuborilgan xabar (2026-08-18). Sabab va falsafa
/// <see cref="AbsenceNotice"/> izohida.
/// </summary>
public sealed class AbsenceNoticeConfiguration : IEntityTypeConfiguration<AbsenceNotice>
{
    public void Configure(EntityTypeBuilder<AbsenceNotice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AbsenceNotices");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Body).IsRequired().HasMaxLength(AbsenceNotice.MaxBodyLength);
        builder.Property(n => n.OutboxKey).HasMaxLength(AbsenceNotice.MaxOutboxKeyLength);
        builder.Property(n => n.ReplyText).HasMaxLength(AbsenceNotice.MaxReplyLength);
        builder.Property(n => n.CallNote).HasMaxLength(AbsenceNotice.MaxReplyLength);

        // Hisoblanadigan xossalar — ustun EMAS.
        builder.Ignore(n => n.HasReply);
        builder.Ignore(n => n.WasCalled);

        builder.HasOne(n => n.CalledBy)
            .WithMany()
            .HasForeignKey(n => n.CalledById)
            .OnDelete(DeleteBehavior.Restrict);

        // Barcha havolalar `Restrict` — bu YOZUV, ya'ni ish tarixi.
        // O'quvchi yoki dars o'chirilsa ham "unga xabar yuborilgan edi"
        // fakti qolishi kerak (`GroupMembershipEvent` bilan AYNI qoida).
        builder.HasOne(n => n.Student)
            .WithMany()
            .HasForeignKey(n => n.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.Group)
            .WithMany()
            .HasForeignKey(n => n.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.Session)
            .WithMany()
            .HasForeignKey(n => n.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.SentBy)
            .WithMany()
            .HasForeignKey(n => n.SentById)
            .OnDelete(DeleteBehavior.Restrict);

        // Asosiy so'rov: "shu davrda yuborilgan xabarlar, yangidan eskiga".
        builder.HasIndex(n => n.SentAt)
            .HasDatabaseName("IX_AbsenceNotices_SentAt");

        // "Shu darsga kim xabar oldi?" — kelmaganlar ro'yxatida
        // "yuborilgan" belgisini chizish uchun eng ko'p ishlatiladi.
        builder.HasIndex(n => new { n.SessionId, n.StudentId })
            .HasDatabaseName("IX_AbsenceNotices_SessionId_StudentId");

        // Yetkazilish holatini kalit bo'yicha o'qish uchun.
        builder.HasIndex(n => n.OutboxKey)
            .HasDatabaseName("IX_AbsenceNotices_OutboxKey");

        // ★ TELEGRAM JAVOBINI TOPISH: bot o'quvchining matnini qabul
        //   qilganda "shu o'quvchining javob kutayotgan eng so'nggi
        //   xabari" izlanadi. Bu — har kelgan xabarda bajariladigan
        //   so'rov, ya'ni indeks bo'lmasa butun jadval skanerlanardi.
        //   Qisman: javob KELGANLARI umuman qidirilmaydi.
        builder.HasIndex(n => new { n.StudentId, n.SentAt })
            .HasFilter(""" "RepliedAt" IS NULL """)
            .HasDatabaseName("IX_AbsenceNotices_AwaitingReply");
    }
}
