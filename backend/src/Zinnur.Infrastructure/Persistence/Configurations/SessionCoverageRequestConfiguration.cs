using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SessionCoverageRequestConfiguration : IEntityTypeConfiguration<SessionCoverageRequest>
{
    public void Configure(EntityTypeBuilder<SessionCoverageRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SessionCoverageRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Reason).IsRequired().HasMaxLength(500);
        builder.Property(r => r.Status).HasConversion<int>();

        builder.HasOne(r => r.Session)
            .WithMany()
            .HasForeignKey(r => r.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Checkin)
            .WithMany()
            .HasForeignKey(r => r.CheckinId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.OriginalHostId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.ResolvedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ⚠️ UNIKAL EMAS (bitta SessionId'ga bir nechta tarixiy so'rov
        // bo'lishi mumkin — sabab entity izohida). Bitta OCHIQ so'rov
        // qoidasi SERVIS darajasida tekshiriladi.
        builder.HasIndex(r => new { r.SessionId, r.Status })
            .HasDatabaseName("IX_SessionCoverageRequests_SessionId_Status");
    }
}
