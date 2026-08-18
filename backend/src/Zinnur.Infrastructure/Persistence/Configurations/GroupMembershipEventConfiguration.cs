using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// A'zolik hodisalari jurnali (2026-08-17) — FAQAT QO'SHILADI.
/// Sabab va falsafa <see cref="GroupMembershipEvent"/> izohida.
/// </summary>
public sealed class GroupMembershipEventConfiguration : IEntityTypeConfiguration<GroupMembershipEvent>
{
    public void Configure(EntityTypeBuilder<GroupMembershipEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GroupMembershipEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Kind).HasConversion<int>();
        builder.Property(e => e.Reason).HasMaxLength(GroupMembershipEvent.MaxReasonLength);

        // Sabab tasnifi (2026-08-18). `Restrict` — katalog qatori
        // o'chirilmaydi (arxivlanadi), bu esa bazadagi kafolat.
        builder.HasOne(e => e.ReasonRef)
            .WithMany()
            .HasForeignKey(e => e.ReasonId)
            .OnDelete(DeleteBehavior.Restrict);

        // Hisoblanuvchi property — ustun EMAS (chegara o'zgarsa tarix qayta
        // baholanishi kerak, sabab entity izohida).
        builder.Ignore(e => e.IsTrial);

        // ★ HAMMA HAVOLA `Restrict`: bu TARIX. O'quvchi, guruh yoki xodim
        //   o'chirilsa ham to'kilish yozuvi QOLISHI kerak — aks holda
        //   hisobot jimgina "kamayib" ketardi (`SessionReview.Author` va
        //   `GroupMember.LeftBy` bilan AYNI mulohaza).
        builder.HasOne(e => e.Student)
            .WithMany()
            .HasForeignKey(e => e.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Group)
            .WithMany()
            .HasForeignKey(e => e.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Ustoz va nishon guruh — navigatsiyasiz FK (surat qiymatlari).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(e => e.MovedToGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // ═════════════════════════════════════════════════════════════
        // ASOSIY SO'ROV INDEKSI: panel "oraliqdagi hodisalar, yangidan
        // eskiga" ko'rinishini beradi — ya'ni `OccurredAt` bo'yicha
        // filtr + tartib. `GroupMember.LeftAt` da bunday indeks YO'Q
        // edi va shu sababdan ham u hisobot uchun yaroqsiz edi.
        // ═════════════════════════════════════════════════════════════
        builder.HasIndex(e => e.OccurredAt)
            .HasDatabaseName("IX_GroupMembershipEvents_OccurredAt");

        // "Shu o'quvchining butun tarixi" — profil ekrani uchun.
        builder.HasIndex(e => new { e.StudentId, e.OccurredAt })
            .HasDatabaseName("IX_GroupMembershipEvents_StudentId_OccurredAt");

        // "Shu guruhdan kimlar ketgan" + tur bo'yicha filtr.
        builder.HasIndex(e => new { e.GroupId, e.Kind })
            .HasDatabaseName("IX_GroupMembershipEvents_GroupId_Kind");
    }
}
