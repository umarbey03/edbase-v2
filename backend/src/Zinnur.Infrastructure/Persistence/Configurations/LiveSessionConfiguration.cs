using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class LiveSessionConfiguration : IEntityTypeConfiguration<LiveSession>
{
    public void Configure(EntityTypeBuilder<LiveSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("LiveSessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title).HasMaxLength(200);
        builder.Property(s => s.RoomName).IsRequired().HasMaxLength(64);
        builder.Property(s => s.RecordingUrl).HasMaxLength(500);
        builder.Property(s => s.CancelReason).HasMaxLength(300);
        builder.Property(s => s.FreeLessonReason).HasMaxLength(300);

        builder.Property(s => s.Type).HasConversion<int>();
        builder.Property(s => s.Status).HasConversion<int>();

        // Hisoblanuvchi property'lar — ustun emas (domain mantiqi).
        builder.Ignore(s => s.EndsAt);
        builder.Ignore(s => s.PlannedDurationMinutes);

        // ============================================================
        // MAJBURIY UNIKAL INDEKS — LOYIHADAGI ENG MUHIM INDEKS.
        //
        // Eski tizimda xona nomi `g{guruh}-l{tartib}` edi; jadval qayta
        // tuzilganda tartib noldan sanalar va IKKI dars bir xil xona nomini
        // olardi. LiveKit webhook'i kelganda `SingleOrDefault()`
        // `MultipleResultsFound` bilan yiqilardi va O'SHA KUNGI BARCHA
        // DAVOMAT YOZILMAY QOLARDI.
        //
        // Bu indeks o'sha xatoni bazaning O'ZIDA imkonsiz qiladi:
        // dublikat INSERT 23505 xatosi bilan darhol rad etiladi.
        // ============================================================
        builder.HasIndex(s => s.RoomName)
            .IsUnique()
            .HasDatabaseName("UX_LiveSessions_RoomName");

        builder.HasOne(s => s.Group)
            .WithMany()
            .HasForeignKey(s => s.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Host — User'ga ishora, navigatsiyasiz. Restrict: ustoz o'chirilmaydi.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.HostId)
            .OnDelete(DeleteBehavior.Restrict);

        // Asl ustoz (o'rinbosar tayinlanganda audit uchun) — AYNI qoida.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.OriginalHostId)
            .OnDelete(DeleteBehavior.Restrict);

        // Jadval so'rovi: guruh bo'yicha va vaqt bo'yicha tartib.
        builder.HasIndex(s => new { s.GroupId, s.ScheduledStart })
            .HasDatabaseName("IX_LiveSessions_GroupId_ScheduledStart");

        // `Status != Cancelled && ScheduledEnd >= now-6h` filtri uchun
        // (ListForUserAsync har foydalanuvchi uchun shu so'rovni bajaradi —
        // 200 kishilik darsda sahifa ochilishida 200 marta).
        builder.HasIndex(s => new { s.Status, s.ScheduledEnd })
            .HasDatabaseName("IX_LiveSessions_Status_ScheduledEnd");
    }
}
