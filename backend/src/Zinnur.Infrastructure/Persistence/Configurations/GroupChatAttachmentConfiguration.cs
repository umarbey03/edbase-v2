using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Guruh chati xabariga biriktirilgan fayl (R16b).
///
/// Sxema <see cref="AssignmentAttachmentConfiguration"/> bilan ATAYLAB
/// o'xshash ("obyekt kaliti + tur + tartib" naqshi). Alohida jadval
/// ekanining sababi <see cref="Zinnur.Domain.Entities.GroupChatAttachment"/>
/// izohida.
/// </summary>
public sealed class GroupChatAttachmentConfiguration
    : IEntityTypeConfiguration<GroupChatAttachment>
{
    /// <summary>
    /// <c>AttachmentKind</c>: 0 (Image), 1 (Audio), 2 (Document).
    /// Video ATAYLAB yo'q — chatga 1 GB fayl yuklanmaydi (sabab
    /// <c>GroupChatService</c> dagi ruxsat etilgan turkumlar izohida).
    /// </summary>
    /// ⚠️ KO'P QATORLI xom satr — sabab <see cref="LessonAssetConfiguration"/> da.
    private const string KindRangeCheck =
        """
        "Kind" IN (0, 1, 2)
        """;

    public void Configure(EntityTypeBuilder<GroupChatAttachment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "GroupChatAttachments",
            table => table.HasCheckConstraint("CK_GroupChatAttachments_Kind", KindRangeCheck));

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ObjectKey)
            .IsRequired()
            .HasMaxLength(AssignmentConfiguration.ObjectKeyMaxLength);

        builder.Property(a => a.ContentType)
            .IsRequired()
            .HasMaxLength(ContentTypeMaxLength);

        builder.Property(a => a.FileName)
            .HasMaxLength(GroupChatAttachment.MaxFileNameLength);

        builder.Property(a => a.Kind).HasConversion<int>();

        // ============================================================
        // XABAR O'CHIRILSA BIRIKTIRMA HAM O'CHADI (Cascade).
        //
        // 🔴 BU YO'L HAQIQATAN YURILADI — `ChatRetentionJob` har soatda
        // kesimdan eski xabarlarni QATTIQ o'chiradi. Ya'ni bu kaskad
        // "nazariy tozalash yo'li" emas, DOIMIY ishlaydigan mexanizm.
        //
        // ⚠️ Kaskad OMBORDAGI obyektni o'chirmaydi (baza R2 ni bilmaydi).
        // Shuning uchun vazifa qatorlarni o'chirishdan OLDIN
        // `ObjectKey` larni o'qib, ularni ombordan o'chiradi. Kaskadni
        // `Restrict` ga o'zgartirish YECHIM EMAS: o'shanda tozalash
        // butunlay yiqilardi va eng katta jadval cheksiz o'sib ketardi.
        // ============================================================
        builder.HasOne(a => a.Message)
            .WithMany(m => m.Attachments)
            .HasForeignKey(a => a.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================================
        // ★ ASOSIY INDEKS: (MessageId, Position)
        //
        // Ikki o'quvchisi bor va ikkalasi ham shu indeksdan foydalanadi:
        //   1) sahifa yuklashda `WHERE MessageId IN (…)` (EF `Include`),
        //   2) `ChatRetentionJob` — o'chiriladigan paketning kalitlarini
        //      AYNAN shu shart bilan yig'adi.
        //
        // ⚠️ UNIKAL EMAS — sabab `LessonAssetConfiguration` da (tartib
        // almashtirilganda oraliq holatda raqamlar to'qnashadi).
        // ============================================================
        builder.HasIndex(a => new { a.MessageId, a.Position })
            .HasDatabaseName("IX_GroupChatAttachments_MessageId_Position");
    }

    /// <summary>`audio/webm;codecs=opus` kabi qiymatlar uchun yetarli.</summary>
    private const int ContentTypeMaxLength = 100;
}
