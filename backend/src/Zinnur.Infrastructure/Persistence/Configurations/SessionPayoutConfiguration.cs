using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SessionPayoutConfiguration : IEntityTypeConfiguration<SessionPayout>
{
    public void Configure(EntityTypeBuilder<SessionPayout> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SessionPayouts");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Role).HasConversion<int>();

        builder.Property(p => p.SessionRate)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        builder.Property(p => p.BonusAmount)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        // ★ `HasDefaultValue(1)` — ESKI (bu ustun qo'shilishidan oldingi)
        // qatorlarni "ustama yo'q" (1x) deb TO'LDIRISH uchun, C# tomondagi
        // `= 1m` ARTIQCHA (faqat yangi obyektga tegishli, ustunga emas).
        builder.Property(p => p.PremiumMultiplierApplied)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale)
            .HasDefaultValue(1m);

        // ★ BITTA DARS — BITTA HAQ YOZUVI (`LiveSession.HostId` yagona).
        builder.HasIndex(p => p.SessionId)
            .IsUnique()
            .HasDatabaseName("UX_SessionPayouts_SessionId");

        builder.HasIndex(p => new { p.UserId, p.SessionId })
            .HasDatabaseName("IX_SessionPayouts_UserId_SessionId");

        // O'CHIRISH: Restrict — haq tarixi kaskad bilan yo'qolmasin
        // (`LessonCharge`/`Payments` dagi bilan AYNI mulohaza).
        builder.HasOne<LiveSession>()
            .WithMany()
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
