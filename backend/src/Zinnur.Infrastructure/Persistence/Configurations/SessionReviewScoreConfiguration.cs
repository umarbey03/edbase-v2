using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SessionReviewScoreConfiguration : IEntityTypeConfiguration<SessionReviewScore>
{
    public void Configure(EntityTypeBuilder<SessionReviewScore> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SessionReviewScores");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.CriterionName).IsRequired().HasMaxLength(AnalysisCriterion.MaxNameLength);
        builder.Property(s => s.MaxScore).HasPrecision(5, 1);
        builder.Property(s => s.Score).HasPrecision(5, 1);

        // Ota tahlil o'chsa ballar ham ketadi — ular yakka o'zi hech
        // narsani anglatmaydi (`SessionReview -> LiveSession` bilan AYNI
        // Cascade mulohazasi).
        builder.HasOne(s => s.SessionReview)
            .WithMany(r => r.Scores)
            .HasForeignKey(s => s.SessionReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        // Mezon o'chsa qator QOLADI (snapshot maydonlar bilan o'qiladi) —
        // `AnalysisCriterion` sinfi izohidagi qaror.
        builder.HasOne(s => s.Criterion)
            .WithMany()
            .HasForeignKey(s => s.CriterionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => s.SessionReviewId);
    }
}
