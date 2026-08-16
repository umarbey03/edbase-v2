using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class LessonChargeConfiguration : IEntityTypeConfiguration<LessonCharge>
{
    public void Configure(EntityTypeBuilder<LessonCharge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("LessonCharges");
        builder.HasKey(c => c.Id);

        // PUL — `Payments`/`Tariffs` bilan AYNI aniqlik (`PaymentConfiguration`
        // dagi izoh: butun loyihada bitta joyda bir xil aniqlik).
        builder.Property(c => c.Amount)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        builder.Property(c => c.NetAmount)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        builder.Property(c => c.SkipReason).HasConversion<int?>();

        // ============================================================
        // ★ IDEMPOTENTLIK QULFI — LOYIHADAGI ENG MUHIM QATOR.
        //
        // `LessonAccrualService` shu darsga yozuv bormi deb SO'ROV bilan
        // tekshiradi (indekssiz sekin bo'lardi), lekin oxirgi himoya —
        // shu indeks: bitta darsning bitta o'quvchiga IKKI marta ulush
        // qo'shilishini BAZANING O'ZIDA imkonsiz qiladi (`UX_LiveSessions_
        // RoomName`/`UX_Attendances_SessionId_StudentId` bilan AYNI naqsh).
        // ============================================================
        builder.HasIndex(c => new { c.SessionId, c.StudentId })
            .IsUnique()
            .HasDatabaseName("UX_LessonCharges_SessionId_StudentId");

        // "Bu oyga qaysi darslar kirdi" hisoboti — `PaymentId` bo'yicha.
        builder.HasIndex(c => c.PaymentId).HasDatabaseName("IX_LessonCharges_PaymentId");

        // O'CHIRISH: Restrict — hammasi `Payments`dagi bilan AYNI mulohaza
        // (pul tarixi kaskad bilan yo'qolmasin).
        builder.HasOne<LiveSession>()
            .WithMany()
            .HasForeignKey(c => c.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(c => c.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Payment)
            .WithMany()
            .HasForeignKey(c => c.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
