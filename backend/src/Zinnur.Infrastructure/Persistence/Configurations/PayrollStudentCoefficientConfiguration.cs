using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class PayrollStudentCoefficientConfiguration
    : IEntityTypeConfiguration<PayrollStudentCoefficient>
{
    private const string PercentRangeCheck =
        """("Percent" >= 0 AND "Percent" <= 100)""";

    public void Configure(EntityTypeBuilder<PayrollStudentCoefficient> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PayrollStudentCoefficients", table =>
            table.HasCheckConstraint("CK_PayrollStudentCoefficients_Percent", PercentRangeCheck));

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Percent)
            .HasPrecision(PaymentConfiguration.MoneyPrecision, PaymentConfiguration.MoneyScale);

        builder.Property(c => c.Note)
            .HasMaxLength(PayrollStudentCoefficient.MaxNoteLength);

        // Davr — oyning birinchi kuni (`PayrollAdjustment.PeriodStart` bilan AYNI).
        builder.Property(c => c.PeriodStart).HasColumnType("date");

        // ★ BITTA XODIM + BITTA O'QUVCHI + BITTA OY = BITTA KOEFFITSIENT.
        //   Ikkita qator bo'lsa qaysi biri qo'llanishi tasodifga qolardi va
        //   admin nega summa "sakrab" turganini tushunmasdi.
        builder.HasIndex(c => new { c.UserId, c.StudentId, c.PeriodStart })
            .IsUnique()
            .HasDatabaseName("UX_PayrollStudentCoefficients_User_Student_Period");

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // O'quvchi o'chirilsa koeffitsient ham ketadi — u o'quvchisiz ma'nosiz.
        // (Xodim tomonidagi kaskad ham shu sababdan.)
        builder.HasOne(c => c.Student)
            .WithMany()
            .HasForeignKey(c => c.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Kim kiritgani — AUDIT izi, kaskad bilan yo'qolmaydi.
        builder.HasOne(c => c.CreatedBy)
            .WithMany()
            .HasForeignKey(c => c.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
