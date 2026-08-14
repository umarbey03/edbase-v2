using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class SessionRecordingConfiguration : IEntityTypeConfiguration<SessionRecording>
{
    /// <summary>
    /// <c>EG_<em>xxxxxxxxxxxx</em></c> ~30 belgi. 100 — zaxira bilan, lekin
    /// cheksiz emas: indeksga kiradigan ustun chegarasiz qolmasin.
    /// </summary>
    private const int EgressIdMaxLength = 100;

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
        // 🔴 R5 — KO'RINISH BAYROG'I VA MAVJUD QATORLAR
        //
        // `HasDefaultValue(true)` MAJBURIY: usiz migratsiya `NOT NULL`
        // ustunni `false` bilan to'ldirardi va deploy kunida BARCHA eski
        // yozuvlar o'quvchilardan yo'qolardi. Qaror va uning sababi
        // `SessionRecording.IsVisibleToStudents` izohida.
        //
        // ★ `HasSentinel(true)` — `UserConfiguration` dagi `IsActive`
        //   TUZOG'INI AYNAN SHU YERDA YOPADI. EF qiymat "sentinel" ga
        //   teng bo'lsa ustunni INSERT'dan tashlab yuboradi. Sentinel
        //   standart holda CLR default (`false`) bo'lardi, ya'ni
        //   ATAYLAB `false` qilib yaratilgan qator bazada `true` bo'lib
        //   qolardi — jimgina yolg'on. Sentinel `true` bo'lgach mantiq
        //   teskari va TO'G'RI bo'ladi: `true` tashlanadi (baza defaulti
        //   ayni `true`), `false` esa DOIM ochiq yoziladi.
        //
        // ⚠️ `UserConfiguration` dagi izoh "HasDefaultValue ishlatmang"
        //   deydi — u EF 9 dagi `HasSentinel` dan OLDIN yozilgan va
        //   o'sha yerda mavjud qatorlarni to'ldirish talabi yo'q edi.
        //   Bu yerda talab bor, shuning uchun tuzoq chetlab o'tilmaydi,
        //   OSHKORA yopiladi.
        // ============================================================
        builder.Property(r => r.IsVisibleToStudents)
            .HasDefaultValue(true)
            .HasSentinel(true);

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
    }
}
