using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class TeacherDailyCheckinConfiguration : IEntityTypeConfiguration<TeacherDailyCheckin>
{
    public void Configure(EntityTypeBuilder<TeacherDailyCheckin> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TeacherDailyCheckins");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Status).HasConversion<int>();
        builder.Property(c => c.DeclineReason).HasMaxLength(TeacherDailyCheckin.MaxReasonLength);

        builder.HasOne(c => c.Teacher)
            .WithMany()
            .HasForeignKey(c => c.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        // BITTA ustoz, BITTA kunga — bot ikkinchi savolni yubormasin
        // (`TeacherMorningCheckinJob` idempotentligi shunga tayanadi).
        builder.HasIndex(c => new { c.TeacherId, c.CheckinDate })
            .IsUnique()
            .HasDatabaseName("UX_TeacherDailyCheckins_TeacherId_CheckinDate");
    }
}

public sealed class TeacherCheckinAffectedSessionConfiguration
    : IEntityTypeConfiguration<TeacherCheckinAffectedSession>
{
    public void Configure(EntityTypeBuilder<TeacherCheckinAffectedSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TeacherCheckinAffectedSessions");
        builder.HasKey(a => a.Id);

        builder.HasOne(a => a.Checkin)
            .WithMany(c => c.AffectedSessions)
            .HasForeignKey(a => a.CheckinId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Session)
            .WithMany()
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Bitta dars bitta checkin'da bir marta — qayta-qayta tugma bosish
        // dublikat qator yaratmasin (toggle mantiqi shu bilan qo'shimcha himoyalangan).
        builder.HasIndex(a => new { a.CheckinId, a.SessionId })
            .IsUnique()
            .HasDatabaseName("UX_TeacherCheckinAffectedSessions_CheckinId_SessionId");
    }
}
