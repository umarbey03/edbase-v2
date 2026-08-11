using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class TelegramUnlinkAuditConfiguration : IEntityTypeConfiguration<TelegramUnlinkAudit>
{
    public void Configure(EntityTypeBuilder<TelegramUnlinkAudit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TelegramUnlinkAudits");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Reason).HasMaxLength(TelegramUnlinkAudit.MaxReasonLength);

        // Chegara `Users.TelegramUsername` bilan AYNI: audit qatori asl
        // qiymatning nusxasi, ya'ni undan uzunroq bo'lishi mumkin emas.
        builder.Property(a => a.OldTelegramUsername)
            .HasMaxLength(User.MaxTelegramUsernameLength);

        // IKKALASI HAM `Restrict` — audit izining ma'nosi shunda: profil
        // o'chirilsa ham "kim kimni uzdi" yozuvi qolishi kerak. Bu
        // `AttendanceAudit` dagi `User` FK'lari bilan AYNI qoida.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Actor)
            .WithMany()
            .HasForeignKey(a => a.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        // "SHU profil bo'yicha oxirgi uzish" — profil drawer'i ochilganda
        // bajariladigan so'rov (`ORDER BY Id DESC LIMIT 1`).
        builder.HasIndex(a => new { a.UserId, a.Id })
            .IsDescending(false, true)
            .HasDatabaseName("IX_TelegramUnlinkAudits_UserId_Id");
    }
}
