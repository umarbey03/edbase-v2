using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GroupMembers");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Status).HasConversion<int>();

        // `IsActive` — hisoblanuvchi property (Status'dan), ustun EMAS.
        builder.Ignore(m => m.IsActive);

        builder.HasOne(m => m.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Student)
            .WithMany()
            .HasForeignKey(m => m.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // BITTA o'quvchi bitta guruhda BITTA marta. Eski tizimda bu faqat
        // kodda tekshirilardi va parallel so'rovlarda dublikat a'zolik
        // yaratilib, davomat ikki marta hisoblanardi.
        builder.HasIndex(m => new { m.GroupId, m.StudentId })
            .IsUnique()
            .HasDatabaseName("UX_GroupMembers_GroupId_StudentId");

        // "Men qaysi guruhlardaman" so'rovi uchun (LiveSessionService.ListForUserAsync).
        builder.HasIndex(m => new { m.StudentId, m.Status })
            .HasDatabaseName("IX_GroupMembers_StudentId_Status");
    }
}
