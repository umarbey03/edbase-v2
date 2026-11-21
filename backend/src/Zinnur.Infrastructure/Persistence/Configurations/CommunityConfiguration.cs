using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class CommunityMessageConfiguration : IEntityTypeConfiguration<CommunityMessage>
{
    public void Configure(EntityTypeBuilder<CommunityMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CommunityMessages");
        builder.HasKey(m => m.Id);
        builder.Ignore(m => m.IsDeleted);

        builder.Property(m => m.Body).IsRequired().HasMaxLength(CommunityMessage.MaxBodyLength);

        builder.HasOne(m => m.Author)
            .WithMany()
            .HasForeignKey(m => m.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.ReplyTo)
            .WithMany()
            .HasForeignKey(m => m.ReplyToId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.DeletedById)
            .OnDelete(DeleteBehavior.SetNull);

        // Lenta "eng yangidan orqaga" o'qiladi; muallif bo'yicha — tezlik cheklovi.
        builder.HasIndex(m => m.CreatedAt);
        builder.HasIndex(m => new { m.AuthorId, m.CreatedAt });
    }
}

public sealed class CommunityMuteConfiguration : IEntityTypeConfiguration<CommunityMute>
{
    public void Configure(EntityTypeBuilder<CommunityMute> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CommunityMutes");
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => m.UserId).IsUnique();
        builder.Property(m => m.Reason).HasMaxLength(CommunityMute.MaxReasonLength);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.MutedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CommunityReadStateConfiguration : IEntityTypeConfiguration<CommunityReadState>
{
    public void Configure(EntityTypeBuilder<CommunityReadState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CommunityReadStates");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.UserId).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
