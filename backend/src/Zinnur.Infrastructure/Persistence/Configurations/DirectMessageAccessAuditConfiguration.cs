using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class DirectMessageAccessAuditConfiguration : IEntityTypeConfiguration<DirectMessageAccessAudit>
{
    public void Configure(EntityTypeBuilder<DirectMessageAccessAudit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DirectMessageAccessAudits");
        builder.HasKey(a => a.Id);

        // HAMMASI `Restrict` — audit izining ma'nosi shunda: profil yoki fayl
        // o'chirilsa ham "kim kimning yozishmasini ko'rdi" yozuvi qolishi
        // kerak (`TelegramUnlinkAudit` dagi AYNI qoida).
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.ViewerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.StaffId).OnDelete(DeleteBehavior.Restrict);

        // "Shu juftlikni kim, qachon ochgan" — yozishma ko'rinishidagi so'rov.
        builder.HasIndex(a => new { a.StudentId, a.StaffId, a.At })
            .HasDatabaseName("IX_DirectMessageAccessAudits_Pair_At");

        // "Shu xodim nimalarni ochgan" — xodim faoliyatini tekshirish.
        builder.HasIndex(a => new { a.ViewerId, a.At })
            .HasDatabaseName("IX_DirectMessageAccessAudits_Viewer_At");
    }
}
