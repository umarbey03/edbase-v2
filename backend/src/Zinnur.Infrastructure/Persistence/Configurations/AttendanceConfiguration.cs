using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class AttendanceConfiguration : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Attendances");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Status).HasConversion<int>();

        builder.HasOne(a => a.Session)
            .WithMany()
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Student)
            .WithMany()
            .HasForeignKey(a => a.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // BITTA dars + BITTA o'quvchi = BITTA yozuv.
        // Zaif internetda o'quvchi 5-10 marta qayta ulanadi; `RegisterJoinAsync`
        // har safar "bormi?" deb qaraydi va yo'q bo'lsa yaratadi. Ikki parallel
        // ulanish bir vaqtda kelsa kod darajasidagi tekshiruv yetarli emas —
        // dublikat yozuvlar davomatni ikki barobar ko'rsatardi. Bu indeks
        // ikkinchi INSERT'ni bazada rad etadi.
        builder.HasIndex(a => new { a.SessionId, a.StudentId })
            .IsUnique()
            .HasDatabaseName("UX_Attendances_SessionId_StudentId");

        // "O'quvchining davomat tarixi" hisoboti uchun.
        builder.HasIndex(a => a.StudentId).HasDatabaseName("IX_Attendances_StudentId");
    }
}
