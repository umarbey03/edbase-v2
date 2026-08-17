using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Shaxsiy yozishmaga (kurator ↔ o'quvchi) biriktirilgan fayl (2026-08-17).
///
/// Sxema <see cref="GroupChatAttachmentConfiguration"/> bilan AYNI naqsh —
/// alohida jadval ekanining sababi
/// <see cref="Zinnur.Domain.Entities.DirectMessageAttachment"/> izohida.
/// </summary>
public sealed class DirectMessageAttachmentConfiguration
    : IEntityTypeConfiguration<DirectMessageAttachment>
{
    /// <summary><c>AttachmentKind</c>: 0 (Image), 1 (Audio), 2 (Document).</summary>
    private const string KindRangeCheck =
        """
        "Kind" IN (0, 1, 2)
        """;

    public void Configure(EntityTypeBuilder<DirectMessageAttachment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "DirectMessageAttachments",
            table => table.HasCheckConstraint("CK_DirectMessageAttachments_Kind", KindRangeCheck));

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ObjectKey)
            .IsRequired()
            .HasMaxLength(AssignmentConfiguration.ObjectKeyMaxLength);

        builder.Property(a => a.ContentType)
            .IsRequired()
            .HasMaxLength(ContentTypeMaxLength);

        builder.Property(a => a.FileName)
            .HasMaxLength(DirectMessageAttachment.MaxFileNameLength);

        builder.Property(a => a.Kind).HasConversion<int>();

        // Xabar o'chirilsa biriktirma ham o'chadi (Cascade) — lekin bu
        // yo'l `GroupChatAttachment` dagidan farqli o'laroq AMALIYOTDA
        // deyarli yurilmaydi: `DirectMessage` ni o'chiradigan endpoint YO'Q
        // va `ChatRetentionJob` bu jadvalga TEGMAYDI (sabab
        // `DirectMessageAttachment` izohida). Kaskad shunga qaramay
        // qo'yiladi — kelajakda "shaxsiy yozishmani o'chirish" qo'shilsa,
        // ombordagi biriktirma yetim qolib ketmasin.
        builder.HasOne(a => a.Message)
            .WithMany(m => m.Attachments)
            .HasForeignKey(a => a.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Sahifa yuklashda `WHERE MessageId IN (…)` (EF `Include`) uchun.
        // ⚠️ UNIKAL EMAS — sabab `GroupChatAttachmentConfiguration` dagi bilan AYNI.
        builder.HasIndex(a => new { a.MessageId, a.Position })
            .HasDatabaseName("IX_DirectMessageAttachments_MessageId_Position");
    }

    /// <summary>`audio/webm;codecs=opus` kabi qiymatlar uchun yetarli.</summary>
    private const int ContentTypeMaxLength = 100;
}
