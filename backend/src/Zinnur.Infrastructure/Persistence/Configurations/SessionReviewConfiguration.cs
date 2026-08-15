using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SessionReviewConfiguration : IEntityTypeConfiguration<SessionReview>
{
    public void Configure(EntityTypeBuilder<SessionReview> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SessionReviews");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Plus).HasMaxLength(SessionReview.MaxSectionLength);
        builder.Property(r => r.Minus).HasMaxLength(SessionReview.MaxSectionLength);
        builder.Property(r => r.Conclusion).IsRequired().HasMaxLength(SessionReview.MaxSectionLength);

        // Enum -> int (loyihaning umumiy uslubi). Nomi o'zgarsa baza
        // tegilmaydi; tartibi o'zgarsa `SessionReviewVerdict` izohidagi
        // ogohlantirish ishlaydi.
        builder.Property(r => r.Verdict).HasConversion<int>();

        builder.Ignore(r => r.IsDecided);
        builder.Ignore(r => r.TotalScore);
        builder.Ignore(r => r.TotalMaxScore);
        builder.Ignore(r => r.ScorePercent);

        // Dars o'chirilsa tahlil ham ketadi: u yakka o'zi hech narsani
        // anglatmaydi (`SessionRecordings` bilan AYNI qoida va AYNI
        // `LiveSessions -> Groups` Cascade zanjiri).
        builder.HasOne(r => r.Session)
            .WithMany()
            .HasForeignKey(r => r.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // MUALLIF: `Restrict` — `StudentNote.Author` bilan AYNI qoida.
        // Xodim ishdan ketsa ham "bu bahoni kim qo'ygan" savoli javobsiz
        // qolmasin: ustoz uchun anonim baho — e'tiroz bildirib bo'lmaydigan
        // baho.
        builder.HasOne(r => r.Author)
            .WithMany()
            .HasForeignKey(r => r.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        // ============================================================
        // 🔴 BITTA DARSGA BITTA TAHLIL — BAZANING O'ZIDA
        //
        // Qoida servisda ham bor (upsert), lekin u ikkita bir vaqtda
        // kelgan so'rovdan himoya qilmaydi: ikkala tranzaksiya ham "qator
        // yo'q" deb ko'rib, ikkitasini qo'shardi. Keyin ro'yxatda ikkita
        // nishon chiqib, ustoz bir dars uchun ikki xil xulosa ko'rardi va
        // qaysi biri haqiqiy ekanini HECH KIM ayta olmasdi.
        //
        // ★ Bu indeks AYNI paytda O'QISH indeksi ham: `RecordingDto` ning
        //   `hasReview`/`reviewStatus` maydonlari va ustozning "Darslarim"
        //   jadvali AYNAN `SessionId` bo'yicha korrelyatsion so'rov
        //   bajaradi (N+1 emas — bitta `SELECT` ichida).
        // ============================================================
        builder.HasIndex(r => r.SessionId)
            .IsUnique()
            .HasDatabaseName("UX_SessionReviews_SessionId");
    }
}
