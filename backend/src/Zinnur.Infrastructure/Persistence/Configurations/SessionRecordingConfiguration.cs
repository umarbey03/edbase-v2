using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SessionRecordingConfiguration : IEntityTypeConfiguration<SessionRecording>
{
    /// <summary>
    /// <c>EG_<em>xxxxxxxxxxxx</em></c> ~30 belgi. 100 — zaxira bilan, lekin
    /// cheksiz emas: indeksga kiradigan ustun chegarasiz qolmasin.
    ///
    /// ⚠️ <c>internal</c> — <see cref="RecordingTrackConfiguration"/> ham
    /// AYNI qiymatni ishlatadi (u ham LiveKit egress identifikatorini
    /// saqlaydi). Ikki nusxa chegara faqat "qaysi biri to'g'ri" degan
    /// savol tug'dirardi.
    /// </summary>
    internal const int EgressIdMaxLength = 100;

    public void Configure(EntityTypeBuilder<SessionRecording> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SessionRecordings");
        builder.HasKey(r => r.Id);

        // ============================================================
        // OBYEKT KALITI — TO'LIQ URL SAQLANMAYDI.
        //
        // Eski tizim `lessons.recording_url` ustuniga R2 kalitini yozardi,
        // lekin ustun NOMI "url" edi va vaqt o'tib unga haqiqiy URL ham
        // tushib qolgan holatlar bo'lgan. Bu yerda nom ham, mazmun ham
        // BITTA narsani anglatadi: ombordagi KALIT. Ko'rish havolasi har
        // so'rovda yangidan imzolanadi va bazaga HECH QACHON tushmaydi
        // (`SubmissionFileConfiguration` dagi AYNI qoida).
        // ============================================================
        builder.Property(r => r.ObjectKey)
            .IsRequired()
            .HasMaxLength(AssignmentConfiguration.ObjectKeyMaxLength);

        builder.Property(r => r.EgressId).HasMaxLength(EgressIdMaxLength);
        builder.Property(r => r.Error).HasMaxLength(SessionRecording.MaxErrorLength);

        // Enum BAZADA RAQAM (loyihaning umumiy uslubi), JSON'da esa SATR
        // (`RecordingDto.Status`). Nomi o'zgarsa baza tegilmaydi, tartibi
        // o'zgarsa esa `RecordingStatus` izohidagi ogohlantirish ishlaydi.
        builder.Property(r => r.Status).HasConversion<int>();

        // ============================================================
        // 🔴 R5 — KO'RINISH BAYROG'I (standart 2026-08-15 dan `false`)
        //
        // `HasDefaultValue(false)` — bazadagi USTUN DEFAULT'i, faqat
        // qiymat ko'rsatilmagan YANGI qatorlarga tegadi. Qaror va uning
        // sababi (nega endi `false`, nega bu mavjud yozuvlarni
        // o'zgartirmaydi) `SessionRecording.IsVisibleToStudents`
        // izohida.
        //
        // ★ `HasSentinel(false)` — `UserConfiguration` dagi `IsActive`
        //   TUZOG'INI AYNAN SHU YERDA YOPADI. EF qiymat "sentinel" ga
        //   teng bo'lsa ustunni INSERT'dan tashlab yuboradi, ya'ni baza
        //   DEFAULT'i ishlaydi. Sentinel `false` bo'lgach (CLR default
        //   bilan AYNI), ATAYLAB `false` qilib yaratilgan qator ham
        //   ATAYLAB `true` qilib yaratilgan qator ham TO'G'RI natija
        //   beradi: `false` tashlanadi (baza defaulti ayni `false`),
        //   `true` esa (masalan `ShowToStudents()` chaqirilganda) DOIM
        //   ochiq, aniq qiymat sifatida yoziladi.
        //
        // ⚠️ `UserConfiguration` dagi izoh "HasDefaultValue ishlatmang"
        //   deydi — u EF 9 dagi `HasSentinel` dan OLDIN yozilgan. Bu
        //   yerda `HasSentinel` bilan birga ishlatilgani uchun tuzoq
        //   chetlab o'tilmaydi, OSHKORA yopiladi.
        // ============================================================
        builder.Property(r => r.IsVisibleToStudents)
            .HasDefaultValue(false)
            .HasSentinel(false);

        // Ko'rinishni oxirgi o'zgartirgan xodim — `User` ga ishora,
        // navigatsiyasiz. `Restrict`: `RequestedBy` bilan AYNI qoida —
        // "kim yopdi" degan javob xodim o'chirilsa ham yo'qolmasin, chunki
        // aynan shu javobga qarab ustozning qayta ochishi to'siladi
        // (`IRecordingService` dagi ustunlik qoidasi).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.VisibilityChangedById)
            .OnDelete(DeleteBehavior.Restrict);

        // Hisoblanuvchi xossalar — ustun EMAS (domain mantiqi).
        builder.Ignore(r => r.IsPlayable);
        builder.Ignore(r => r.IsFinished);
        builder.Ignore(r => r.IsPending);
        builder.Ignore(r => r.CanRetryComposition);
        builder.Ignore(r => r.CanResumeComposition);

        // Dars o'chirilsa yozuv qatorlari ham ketadi: ularning yakka o'zi
        // hech narsani anglatmaydi (`LiveSessions` -> `Groups` zanjiri ham
        // Cascade).
        builder.HasOne(r => r.Session)
            .WithMany()
            .HasForeignKey(r => r.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Yozuvni SO'RAGAN odam — `User` ga ishora, navigatsiyasiz.
        // Restrict: xodim o'chirilmaydi (`LiveSessionConfiguration.HostId`
        // bilan AYNI qoida) — "kim yozib olishga qaror qildi" degan javob
        // yo'qolib qolmasin.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.RequestedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // ============================================================
        // ★ UNIKAL INDEKS — WEBHOOK AYNAN SHU USTUN BO'YICHA TOPADI.
        //
        // Postgres unikal indeksda NULL'lar BIR-BIRIDAN FARQLI hisoblanadi,
        // ya'ni hali Egress'ga yuborilmagan (EgressId = NULL) qatorlar
        // istalgancha bo'lishi mumkin — filtr kerak emas.
        //
        // Dublikat `EgressId` esa bazaning O'ZIDA imkonsiz bo'ladi: aks
        // holda webhook `FirstOrDefault` bilan "qaysidir" qatorni topib,
        // ikkinchisini abadiy `Starting` holida qoldirardi.
        // ============================================================
        builder.HasIndex(r => r.EgressId)
            .IsUnique()
            .HasDatabaseName("UX_SessionRecordings_EgressId");

        // Dars kartochkasi: "shu darsning urinishlari, yangisi birinchi".
        builder.HasIndex(r => new { r.SessionId, r.Id })
            .HasDatabaseName("IX_SessionRecordings_SessionId_Id");

        // WATCHDOG so'rovi: tugallanmagan yozuvlarni holat bo'yicha tanlaydi
        // va urinish paytiga qarab saralaydi. Bu indekssiz fon vazifasi har
        // yurishda BUTUN jadvalni skanerlardi — jadval esa har dars bilan
        // o'sib boradi.
        builder.HasIndex(r => new { r.Status, r.LastAttemptAt })
            .HasDatabaseName("IX_SessionRecordings_Status_LastAttemptAt");

        /* ═══════════════════════════════════════════════════════════════
           YOZUV YO'LI VA TUNGI YIG'ISH (yozuv quvuri v2) — QO'SHIMCHA

           Alohida, uzluksiz blok: yuqoridagi hech narsa o'zgarmadi,
           mavjud ustunlarning turi, nullligi va indekslari AVVALGIDEK.
           ═══════════════════════════════════════════════════════════════ */

        // Enum BAZADA RAQAM — faylning yuqorisidagi AYNI qoida.
        //
        // ⚠️ `HasDefaultValue` ATAYLAB QO'YILMADI, garchi
        //    `RecordingPipeline.RoomComposite` ham `0` bo'lsa-da.
        //    `RecordingsVisibleToStudents` dagi sentinel tuzog'i bu yerda
        //    UMUMAN paydo bo'lmaydi: ustun DEFAULT'i bo'lmasa EF qiymatni
        //    HAR DOIM oshkor yozadi, ya'ni "tashlab ketilgan ustun"
        //    holati yo'q. Mavjud qatorlarni esa MIGRATSIYA to'ldiradi
        //    (`AddColumn ... defaultValue: 0`) — bu bir martalik ish va
        //    ustun standarti sifatida qolishi shart emas.
        builder.Property(r => r.Pipeline).HasConversion<int>();

        // Bo'sh (`NULL`) — eski yo'l uchun YAGONA to'g'ri qiymat; sabab
        // `RecordingCompositionStatus` izohida.
        builder.Property(r => r.CompositionStatus).HasConversion<int>();

        builder.Property(r => r.CompositionError)
            .HasMaxLength(SessionRecording.MaxErrorLength);

        // ============================================================
        // 🔴 "BIR DARSGA, BIR YO'LDAN, BIR VAQTDA BITTA URINISH" —
        //     ENDI BAZANING QOIDASI, SERVISNING KELISHUVI EMAS.
        //
        // Ilgari buni faqat `AutoRecordingScheduler` ta'minlardi: u
        // yangi qator yaratishdan oldin tugallanmagan qator bor-yo'qligini
        // tekshirardi. Endi BITTA darsga IKKI yo'l qator yozadi (A/B
        // solishtirish davri), ya'ni tekshiruv "yo'l bo'yicha" bo'lishi
        // kerak — va aynan shu joyda kod bilan bazaning tushunchasi
        // ajralib ketishi mumkin.
        //
        // FILTR `"Status" < 3` — "yakuniy emas" degani: `Completed = 3`,
        // `Failed = 4`. Ya'ni bir darsda bir yo'ldan qancha TUGAGAN
        // urinish bo'lsa ham mayli (tarix aynan shuning uchun saqlanadi),
        // lekin AYNI vaqtda ochiq turgani BITTA.
        //
        // ⚠️ FILTR RAQAM BILAN YOZILGAN, enum nomi bilan emas — bu SQL,
        //    u C# ni bilmaydi. `RecordingStatus` raqamlarining
        //    o'zgarmasligi shu sababdan ham majburiy (izoh o'sha enumda).
        // ============================================================
        builder.HasIndex(r => new { r.SessionId, r.Pipeline })
            .IsUnique()
            .HasFilter("\"Status\" < 3")
            .HasDatabaseName("UX_SessionRecordings_SessionId_Pipeline_Active");

        /* ═══════════════════════════════ /yozuv yo'li va tungi yig'ish ═══ */
    }
}
