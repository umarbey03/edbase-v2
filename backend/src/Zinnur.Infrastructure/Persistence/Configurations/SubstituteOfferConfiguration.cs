using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SubstituteOfferConfiguration : IEntityTypeConfiguration<SubstituteOffer>
{
    public void Configure(EntityTypeBuilder<SubstituteOffer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SubstituteOffers");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Status).HasConversion<int>();

        builder.HasOne(o => o.CoverageRequest)
            .WithMany(r => r.Offers)
            .HasForeignKey(o => o.CoverageRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.CandidateTeacher)
            .WithMany()
            .HasForeignKey(o => o.CandidateTeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        // Bir nomzodga bir so'rov uchun bitta taklif — ikkinchi urinish
        // (masalan job qayta yugursa) dublikat xabar yubormasin.
        builder.HasIndex(o => new { o.CoverageRequestId, o.CandidateTeacherId })
            .IsUnique()
            .HasDatabaseName("UX_SubstituteOffers_CoverageRequestId_CandidateTeacherId");
    }
}
