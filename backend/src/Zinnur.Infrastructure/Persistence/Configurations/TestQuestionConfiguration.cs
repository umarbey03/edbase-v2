using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class TestQuestionConfiguration : IEntityTypeConfiguration<TestQuestion>
{
    public void Configure(EntityTypeBuilder<TestQuestion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TestQuestions");
        builder.HasKey(q => q.Id);

        builder.Property(q => q.Body).IsRequired().HasMaxLength(TestQuestion.MaxBodyLength);
        builder.Property(q => q.ImageKey).HasMaxLength(AssignmentConfiguration.ObjectKeyMaxLength);

        // BALL — `decimal(6,2)`, `float` EMAS. Yarim ball (0.5) ishlatiladi,
        // `float` bo'lsa 0.1 + 0.2 ≠ 0.3 muammosi natijalarga tushardi.
        builder.Property(q => q.Points)
            .HasPrecision(AssignmentConfiguration.ScorePrecision, AssignmentConfiguration.ScoreScale);

        // Hisoblanuvchi: to'g'ri variantlar variantlardan olinadi.
        builder.Ignore(q => q.CorrectOptionIds);
        builder.Ignore(q => q.IsMultipleChoice);

        builder.HasOne(q => q.Test)
            .WithMany(t => t.Questions)
            .HasForeignKey(q => q.TestId)
            .OnDelete(DeleteBehavior.Cascade);

        // SAVOL TARTIBI BARQAROR: har so'rovda `ORDER BY Position, Id`.
        // Indekssiz Postgres tartibni ixtiyoriy qoldirardi va o'quvchi
        // sahifani yangilaganda savollar joyini almashtirib turardi.
        builder.HasIndex(q => new { q.TestId, q.Position })
            .HasDatabaseName("IX_TestQuestions_TestId_Position");
    }
}
