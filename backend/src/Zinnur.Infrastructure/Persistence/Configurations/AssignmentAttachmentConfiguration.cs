using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Uy vazifasi SHARTINING biriktirmasi (rasm/audio/hujjat).
///
/// Sxema <see cref="LessonAssetConfiguration"/> bilan ATAYLAB juda o'xshash:
/// ikkalasi ham "obyekt kaliti + tur + tartib" naqshi. Bir jadvalga
/// qo'shilmagani esa ONGLI qaror — sabab
/// <see cref="Zinnur.Domain.Entities.AssignmentAttachment"/> izohida
/// (ular boshqa-boshqa ruxsat qoidasiga bo'ysunadi).
/// </summary>
public sealed class AssignmentAttachmentConfiguration
    : IEntityTypeConfiguration<AssignmentAttachment>
{
    /// <summary>
    /// <c>AttachmentKind</c>: 0 (Image), 1 (Audio), 2 (Document).
    /// Noma'lum qiymat bazaga tushmasin — enumga yangi qiymat qo'shilsa
    /// shu ro'yxat ham yangilanishi kerak.
    /// </summary>
    /// ⚠️ KO'P QATORLI xom satr — sabab <see cref="LessonAssetConfiguration"/> da.
    private const string KindRangeCheck =
        """
        "Kind" IN (0, 1, 2)
        """;

    public void Configure(EntityTypeBuilder<AssignmentAttachment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "AssignmentAttachments",
            table => table.HasCheckConstraint("CK_AssignmentAttachments_Kind", KindRangeCheck));

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ObjectKey)
            .IsRequired()
            .HasMaxLength(AssignmentConfiguration.ObjectKeyMaxLength);

        builder.Property(a => a.ContentType)
            .IsRequired()
            .HasMaxLength(ContentTypeMaxLength);

        builder.Property(a => a.Kind).HasConversion<int>();

        builder.HasOne(a => a.Assignment)
            .WithMany(x => x.Attachments)
            .HasForeignKey(a => a.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        // ⚠️ UNIKAL EMAS — sabab `LessonAssetConfiguration` da (tartibni
        // almashtirishda oraliq holatda raqamlar to'qnashadi).
        builder.HasIndex(a => new { a.AssignmentId, a.Position })
            .HasDatabaseName("IX_AssignmentAttachments_AssignmentId_Position");
    }

    /// <summary>`audio/webm;codecs=opus` kabi qiymatlar uchun yetarli.</summary>
    private const int ContentTypeMaxLength = 100;
}
