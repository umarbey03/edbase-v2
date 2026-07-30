using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SubmissionFileConfiguration : IEntityTypeConfiguration<SubmissionFile>
{
    public void Configure(EntityTypeBuilder<SubmissionFile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SubmissionFiles");
        builder.HasKey(f => f.Id);

        // ============================================================
        // OBYEKT KALITI — TO'LIQ URL SAQLANMAYDI.
        //
        // Eski tizim `/media/submissions/2026-07/ab12.jpg` ko'rinishidagi
        // URL'ni bazaga yozardi. Ikki muammo:
        //   1) fayllar lokal diskda edi (masshtab to'sig'i) va `/media`
        //      autentifikatsiyasiz ochiq edi (zaiflik X-6);
        //   2) R2/S3 ga o'tilganda presigned URL MUDDATLI bo'ladi —
        //      bazadagi URL bir soatdan keyin ishlamay qolardi.
        // Endi bazada faqat KALIT; ko'rish linki har so'rovda yangidan
        // imzolanadi.
        // ============================================================
        builder.Property(f => f.ObjectKey)
            .IsRequired()
            .HasMaxLength(AssignmentConfiguration.ObjectKeyMaxLength);

        builder.Property(f => f.ContentType).HasMaxLength(ContentTypeMaxLength);
        builder.Property(f => f.Kind).HasConversion<int>();

        builder.HasOne(f => f.Submission)
            .WithMany(s => s.Files)
            .HasForeignKey(f => f.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.SubmissionId)
            .HasDatabaseName("IX_SubmissionFiles_SubmissionId");
    }

    /// <summary>`audio/webm;codecs=opus` kabi qiymatlar uchun yetarli.</summary>
    private const int ContentTypeMaxLength = 100;
}
