using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class TestOptionConfiguration : IEntityTypeConfiguration<TestOption>
{
    public void Configure(EntityTypeBuilder<TestOption> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TestOptions");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Body).IsRequired().HasMaxLength(TestOption.MaxBodyLength);

        builder.HasOne(o => o.Question)
            .WithMany(q => q.Options)
            .HasForeignKey(o => o.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Variant tartibi ham barqaror bo'lishi kerak (savol tartibi bilan bir xil sabab).
        builder.HasIndex(o => new { o.QuestionId, o.Position })
            .HasDatabaseName("IX_TestOptions_QuestionId_Position");
    }
}
