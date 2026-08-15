using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class AnalysisCriterionConfiguration : IEntityTypeConfiguration<AnalysisCriterion>
{
    public void Configure(EntityTypeBuilder<AnalysisCriterion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AnalysisCriteria");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(AnalysisCriterion.MaxNameLength);

        builder.Property(c => c.MaxScore).HasPrecision(5, 1);

        builder.HasIndex(c => c.SortOrder);
    }
}
