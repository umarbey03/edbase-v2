using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class PayrollRuleTierConfiguration : IEntityTypeConfiguration<PayrollRuleTier>
{
    private const string NonNegativeCheck =
        """("StudentCount" >= 0 AND "Amount" >= 0)""";

    public void Configure(EntityTypeBuilder<PayrollRuleTier> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PayrollRuleTiers", table =>
            table.HasCheckConstraint("CK_PayrollRuleTiers_NonNegative", NonNegativeCheck));

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Amount)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        // ★ BITTA QOIDADA BITTA CHEGARA BIR MARTA: "3 o'quvchi uchun" ikkita
        //   qator bo'lsa, qaysi biri ishlashi tartibga bog'lanib qolardi —
        //   ya'ni natija so'rovdan so'rovga o'zgarishi mumkin edi.
        builder.HasIndex(t => new { t.RuleId, t.StudentCount })
            .IsUnique()
            .HasDatabaseName("UX_PayrollRuleTiers_RuleId_StudentCount");
    }
}
