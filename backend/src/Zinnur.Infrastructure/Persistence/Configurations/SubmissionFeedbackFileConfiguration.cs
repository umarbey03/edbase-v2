using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ustoz TEKSHIRISHDA biriktirgan fayl (R37).
///
/// Sxema <see cref="SubmissionFileConfiguration"/> ga juda o'xshaydi, lekin
/// ALOHIDA jadval — sabab
/// <see cref="Zinnur.Domain.Entities.SubmissionFeedbackFile"/> izohida
/// batafsil (qisqasi: mavjud o'qish yo'llari "har qator o'quvchiniki"
/// degan yozilmagan taxminga qurilgan).
/// </summary>
public sealed class SubmissionFeedbackFileConfiguration
    : IEntityTypeConfiguration<SubmissionFeedbackFile>
{
    /// <summary><c>AttachmentKind</c>: 0 (Image), 1 (Audio), 2 (Document).</summary>
    /// ⚠️ KO'P QATORLI xom satr — sabab <see cref="LessonAssetConfiguration"/> da.
    private const string KindRangeCheck =
        """
        "Kind" IN (0, 1, 2)
        """;

    public void Configure(EntityTypeBuilder<SubmissionFeedbackFile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "SubmissionFeedbackFiles",
            table => table.HasCheckConstraint("CK_SubmissionFeedbackFiles_Kind", KindRangeCheck));

        builder.HasKey(f => f.Id);

        // TO'LIQ URL SAQLANMAYDI — faqat kalit (sabab
        // `SubmissionFileConfiguration` da batafsil: presigned havola
        // muddatli va bazadagi URL bir soatdan keyin ishlamay qolardi).
        builder.Property(f => f.ObjectKey)
            .IsRequired()
            .HasMaxLength(AssignmentConfiguration.ObjectKeyMaxLength);

        builder.Property(f => f.ContentType)
            .IsRequired()
            .HasMaxLength(ContentTypeMaxLength);

        builder.Property(f => f.FileName)
            .HasMaxLength(SubmissionFeedbackFile.MaxFileNameLength);

        builder.Property(f => f.Kind).HasConversion<int>();

        // Javob o'chirilsa tekshiruv fayllari ham o'chadi — ular o'sha
        // javobning bir qismi. ⚠️ Amalda javob O'CHIRILMAYDI (baholar
        // yo'qolmasin), ya'ni bu yo'l faqat vazifa bilan birga o'chirishda
        // yuriladi.
        builder.HasOne(f => f.Submission)
            .WithMany(s => s.FeedbackFiles)
            .HasForeignKey(f => f.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Xodim o'chirilsa fayl qolsin — kim qo'ygani noma'lum bo'lib
        // qolgani, faylning O'ZINI yo'qotishdan yaxshiroq.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(f => f.SubmissionId)
            .HasDatabaseName("IX_SubmissionFeedbackFiles_SubmissionId");
    }

    /// <summary>`audio/webm;codecs=opus` kabi qiymatlar uchun yetarli.</summary>
    private const int ContentTypeMaxLength = 100;
}
