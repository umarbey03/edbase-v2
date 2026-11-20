using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SelfStudyCuratorPoolMemberConfiguration
    : IEntityTypeConfiguration<SelfStudyCuratorPoolMember>
{
    public void Configure(EntityTypeBuilder<SelfStudyCuratorPoolMember> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "SelfStudyCuratorPoolMembers",
            table => table.HasCheckConstraint(
                "CK_SelfStudyCuratorPoolMembers_Capacity",
                $"\"Capacity\" BETWEEN {SelfStudyCuratorPoolMember.MinCapacity} AND {SelfStudyCuratorPoolMember.MaxCapacity}"));

        builder.HasKey(m => m.Id);

        // Bir kurator ro'yxatda BIR marta — ikki marta qo'shilsa sig'imi ikki
        // baravar hisoblanib, "to'ldi" eslatmasi kech kelardi.
        builder.HasIndex(m => m.CuratorId).IsUnique();

        builder.Property(m => m.Capacity)
            .HasDefaultValue(SelfStudyCuratorPoolMember.DefaultCapacity);

        builder.HasOne(m => m.Curator)
            .WithMany()
            .HasForeignKey(m => m.CuratorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.AddedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
