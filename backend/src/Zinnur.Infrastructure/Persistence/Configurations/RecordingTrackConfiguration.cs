using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class RecordingTrackConfiguration : IEntityTypeConfiguration<RecordingTrack>
{
    /// <summary>
    /// LiveKit trek identifikatori (<c>TR_<em>xxxxxxxxxxxx</em></c>) ~20
    /// belgi, sentinel esa 4–6. 64 — zaxira bilan, lekin unikal indeksga
    /// kiradigan ustun chegarasiz qolmasin
    /// (<c>SessionRecordingConfiguration.EgressIdMaxLength</c> dagi AYNI
    /// mulohaza).
    /// </summary>
    private const int TrackSidMaxLength = 64;

    /// <summary>
    /// LiveKit <c>identity</c> — bizda <c>User.Id</c> ning satr
    /// ko'rinishi, ya'ni o'nlab belgi ham emas. 64 — kelajakda identity
    /// sxemasi o'zgarsa ham yetadi.
    /// </summary>
    private const int ParticipantIdentityMaxLength = 64;

    /// <summary>`video/vp8`, `audio/opus` — eng uzuni ham 30 belgiga yetmaydi.</summary>
    private const int MimeTypeMaxLength = 64;

    public void Configure(EntityTypeBuilder<RecordingTrack> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RecordingTracks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TrackSid)
            .IsRequired()
            .HasMaxLength(TrackSidMaxLength);

        builder.Property(t => t.ParticipantIdentity)
            .HasMaxLength(ParticipantIdentityMaxLength);

        builder.Property(t => t.MimeType).HasMaxLength(MimeTypeMaxLength);

        // ============================================================
        // XOM OBYEKT KALITI — `raw/…` prefiksida.
        //
        // Uzunlik `SessionRecordings.ObjectKey` bilan AYNI chegarada:
        // ikkalasi ham AYNI ombordagi kalit va ikki xil chegara faqat
        // "qaysi biri qattiqroq" degan savolni tug'dirardi.
        //
        // ⚠️ Bu kalit foydalanuvchiga HECH QACHON berilmaydi va unga
        // ko'rish havolasi imzolanmaydi — u faqat tungi yig'ish uchun
        // (izoh `RecordingTrack` da).
        // ============================================================
        builder.Property(t => t.ObjectKey)
            .IsRequired()
            .HasMaxLength(AssignmentConfiguration.ObjectKeyMaxLength);

        builder.Property(t => t.EgressId)
            .HasMaxLength(SessionRecordingConfiguration.EgressIdMaxLength);
        builder.Property(t => t.Error).HasMaxLength(RecordingTrack.MaxErrorLength);

        // Enum BAZADA RAQAM — `SessionRecordingConfiguration` dagi AYNI
        // qoida. `Status` ATAYLAB `RecordingStatus` ni qayta ishlatadi
        // (sabab `RecordingTrack` izohida), ya'ni bu yerda ikkinchi
        // holat lug'ati yo'q.
        builder.Property(t => t.Kind).HasConversion<int>();
        builder.Property(t => t.Status).HasConversion<int>();

        // Hisoblanuvchi xossalar — ustun EMAS (domain mantiqi).
        builder.Ignore(t => t.IsFinished);
        builder.Ignore(t => t.IsRoomAudio);

        // ============================================================
        // YOZUV O'CHIRILSA BO'LAKLAR HAM KETADI (Cascade).
        //
        // Bo'lakning yakka o'zi hech narsani anglatmaydi: u yig'ilmagan
        // xom fayl haqidagi qayd va uni qaysi darsga qo'yishni faqat
        // yozuv qatori biladi. `SessionRecordings` -> `LiveSessions` ->
        // `Groups` zanjiri ham Cascade, ya'ni bu qoida yangi emas.
        // ============================================================
        builder.HasOne(t => t.Recording)
            .WithMany(r => r.Tracks)
            .HasForeignKey(t => t.RecordingId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================================
        // ★ IDEMPOTENTLIK DARVOZASI — IKKI VAZIFANI BAJARADI.
        //
        //   1) LiveKit `track_published` ni QAYTA yuboradi. Indekssiz har
        //      takror yangi qator, yangi egress va yangi fayl yaratardi —
        //      ya'ni bitta ekran ulashish ikki marta yozib olinardi.
        //
        //   2) "BITTA DARSGA BITTA MIKSER" KAFOLATI. Xona ovozi qatorining
        //      `TrackSid` i — sentinel (`RecordingTrack.RoomAudioSid`), ya'ni
        //      AYNIQ qiymat. Qayta yetkazilgan `room_started` ikkinchi
        //      mikserni ishga tushirmoqchi bo'lsa, uni BAZA to'sadi.
        //      Aynan shu sabab u yerda `NULL` emas, sentinel turadi
        //      (Postgres unikal indeksida `NULL` lar farqli hisoblanadi).
        // ============================================================
        builder.HasIndex(t => new { t.RecordingId, t.TrackSid })
            .IsUnique()
            .HasDatabaseName("UX_RecordingTracks_RecordingId_TrackSid");

        // ★ Webhook qatorni AYNAN shu ustun bo'yicha topadi —
        // `UX_SessionRecordings_EgressId` bilan AYNI sabab va AYNI
        // xotirjamlik: hali Egress'ga yuborilmagan qatorlarda `EgressId`
        // `NULL`, va Postgres'da bunday qatorlar istalgancha bo'lishi
        // mumkin, filtr kerak emas.
        builder.HasIndex(t => t.EgressId)
            .IsUnique()
            .HasDatabaseName("UX_RecordingTracks_EgressId");

        // TUNGI YIG'ISHNING o'qish yo'li: "shu yozuvning bo'laklari, turi
        // bo'yicha, vaqt o'qi tartibida". `StartedAt` indeksga kiritilgani
        // ATAYLAB — yig'ish bo'laklarni AYNAN shu tartibda joylashtiradi.
        builder.HasIndex(t => new { t.RecordingId, t.Kind, t.StartedAt })
            .HasDatabaseName("IX_RecordingTracks_RecordingId_Kind_StartedAt");

        // MOSLASHTIRUVCHI vazifaning o'qish yo'li: "boshlanmay qolgan
        // bo'laklar, oxirgi urinish paytiga qarab".
        // `IX_SessionRecordings_Status_LastAttemptAt` bilan AYNI naqsh va
        // AYNI sabab: indekssiz fon vazifasi har yurishda butun jadvalni
        // skanerlardi, jadval esa har dars bilan bir necha qatorga o'sadi.
        builder.HasIndex(t => new { t.Status, t.LastAttemptAt })
            .HasDatabaseName("IX_RecordingTracks_Status_LastAttemptAt");
    }
}
