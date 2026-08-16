using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>Guruhlarga yuborilgan xabar tarixi (2026-08-16) — "Xabarlar" panelining jurnali.</summary>
public sealed class GroupBroadcastConfiguration : IEntityTypeConfiguration<GroupBroadcast>
{
    public void Configure(EntityTypeBuilder<GroupBroadcast> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GroupBroadcasts");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Body).IsRequired().HasMaxLength(GroupBroadcast.MaxBodyLength);
        builder.Property(b => b.TargetGroupNames)
            .IsRequired()
            .HasMaxLength(GroupBroadcast.MaxTargetNamesLength);

        // Yuborgan xodim — `User`ga ishora, navigatsiyasiz FK bilan bir xil
        // naqsh (`SessionRecording.RequestedBy` izohi): Restrict, xodim
        // o'chirilmaydi, "kim yubordi" tarixdan yo'qolmasin.
        builder.HasOne(b => b.Author)
            .WithMany()
            .HasForeignKey(b => b.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Shablon o'chirilsa TARIX yo'qolmaydi — faqat ishora bo'shaydi
        // (`Body` allaqachon SNAPSHOT, ya'ni matn saqlanadi; `TemplateId`
        // esa faqat "qaysi shablondan foydalanilgan edi" degan bog'lanish).
        builder.HasOne(b => b.Template)
            .WithMany()
            .HasForeignKey(b => b.TemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        // Tarix ro'yxati DOIM "yangisi birinchi" tartibida.
        builder.HasIndex(b => b.CreatedAt)
            .HasDatabaseName("IX_GroupBroadcasts_CreatedAt");
    }
}
