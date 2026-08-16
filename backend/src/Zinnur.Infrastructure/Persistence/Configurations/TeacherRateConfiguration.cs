using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class TeacherRateConfiguration : IEntityTypeConfiguration<TeacherRate>
{
    /// <summary>Stavkalar MANFIY bo'lmaydi — sabab <c>TariffConfiguration</c> dagi bilan bir xil.</summary>
    private const string RatesNonNegativeCheck =
        """("PerSessionRate" >= 0 AND "PerStudentBonusRate" >= 0)""";

    public void Configure(EntityTypeBuilder<TeacherRate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TeacherRates", table =>
            table.HasCheckConstraint("CK_TeacherRates_Rates_NonNegative", RatesNonNegativeCheck));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.PerSessionRate)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        builder.Property(r => r.PerStudentBonusRate)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        // Mahalliy KALENDAR sanasi — `Tariff.ActiveFrom` bilan AYNI sabab.
        builder.Property(r => r.ActiveFrom).HasColumnType("date");

        // Hisoblanuvchi property — ustun EMAS.
        builder.Ignore(r => r.Specificity);

        // O'CHIRISH: Restrict — stavka HAQ TARIXI. Xodim o'chirilganda uning
        // stavka qatorlari kaskad bilan ketsa, o'tgan oy hisobotini qayta
        // tiklab bo'lmaydigan qilib yo'qotardi.
        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
