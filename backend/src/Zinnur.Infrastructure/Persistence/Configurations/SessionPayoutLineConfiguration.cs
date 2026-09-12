using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SessionPayoutLineConfiguration : IEntityTypeConfiguration<SessionPayoutLine>
{
    public void Configure(EntityTypeBuilder<SessionPayoutLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SessionPayoutLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Kind).HasConversion<int>();

        builder.Property(l => l.RuleName)
            .IsRequired()
            .HasMaxLength(SessionPayoutLine.MaxRuleNameLength);

        builder.Property(l => l.Basis)
            .HasMaxLength(SessionPayoutLine.MaxBasisLength);

        builder.Property(l => l.Amount)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        builder.HasIndex(l => l.PayoutId)
            .HasDatabaseName("IX_SessionPayoutLines_PayoutId");

        // Tashkil etuvchi haq yozuvining BO'LAGI — yozuv o'chsa u ham ketadi.
        // (Haq yozuvining o'zi hech qachon o'chirilmaydi: `SessionPayout`
        // ga bo'lgan havolalar Restrict.)
        builder.HasOne(l => l.Payout)
            .WithMany(p => p.Lines)
            .HasForeignKey(l => l.PayoutId)
            .OnDelete(DeleteBehavior.Cascade);

        // ★ QOIDAGA HAVOLA — `SetNull`: qoida o'chirilsa tashkil etuvchi
        //   QOLADI (nomi nusxa sifatida saqlangan, `RuleName`), faqat
        //   havola uziladi. `Restrict` bo'lganda admin eskirgan qoidani
        //   umuman o'chira olmasdi; `Cascade` esa o'tgan oyning tafsilotini
        //   jimgina yo'q qilardi.
        builder.HasOne(l => l.Rule)
            .WithMany()
            .HasForeignKey(l => l.RuleId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
