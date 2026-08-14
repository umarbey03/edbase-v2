using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class DirectMessageConfiguration : IEntityTypeConfiguration<DirectMessage>
{
    public void Configure(EntityTypeBuilder<DirectMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DirectMessages");
        builder.HasKey(m => m.Id);

        // Domain cheklovi bilan AYNAN bir xil (DirectMessage.MaxBodyLength).
        builder.Property(m => m.Body).IsRequired().HasMaxLength(DirectMessage.MaxBodyLength);

        // ============================================================
        // O'CHIRISH XATTI-HARAKATI
        //
        // Ishtirokchilar uchun `Restrict`: yozishma — o'quvchi bilan
        // markaz o'rtasidagi YOZMA IZ (kim nima so'radi, kurator nima
        // deb javob berdi). Xodim hisobi o'chirilganda kaskad bilan
        // butun tarix yo'qolsa, nizoli holatda hech qanday dalil
        // qolmasdi.
        //
        // ★ IKKI FK BIR JADVALGA (Users) — `WithMany()` ATAYLAB nomsiz:
        // `User` da "mening yozishmalarim" degan navigatsiya YO'Q va
        // bo'lishi ham kerak emas (uni yuklash foydalanuvchi bilan birga
        // butun chat tarixini tortib kelardi).
        // ============================================================
        builder.HasOne(m => m.Student)
            .WithMany()
            .HasForeignKey(m => m.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Staff)
            .WithMany()
            .HasForeignKey(m => m.StaffId)
            .OnDelete(DeleteBehavior.Restrict);

        // Kontekst darsi o'chirilsa xabar QOLADI, kontekst bo'shaydi:
        // savolning o'zi hamon qimmatli.
        builder.HasOne(m => m.ModuleLesson)
            .WithMany()
            .HasForeignKey(m => m.ModuleLessonId)
            .OnDelete(DeleteBehavior.SetNull);

        // `SenderId` uchun FK YO'Q — u har doim `StudentId` yoki
        // `StaffId` dan biri (Domain kafolatlaydi), ya'ni yaxlitlik
        // allaqachon ikki FK bilan ta'minlangan. Uchinchi FK faqat
        // yozuv narxini oshirardi.

        // ============================================================
        // ★ ASOSIY INDEKS: (StudentId, StaffId, Id)
        //
        // Yozishmani o'qish so'rovi AYNAN shunday:
        //   WHERE StudentId = @s AND StaffId = @c AND Id < @cursor
        //   ORDER BY Id DESC LIMIT 51
        //
        // Kompozit indeks filtrni ham, tartibni ham, kursorni ham
        // qoplaydi — sahifalash uchun jadvalga umuman kirilmaydi.
        // ============================================================
        builder.HasIndex(m => new { m.StudentId, m.StaffId, m.Id })
            .HasDatabaseName("IX_DirectMessages_Student_Staff_Id");

        // ============================================================
        // ★ O'QILMAGANLAR INDEKSI — QISMAN (partial).
        //
        // "Nechta o'qilmagan xabarim bor?" savoli har ekran ochilganda
        // beriladi, lekin o'qilmagan xabar butun jadvalning juda kichik
        // ulushi: yozishmaning 99% i allaqachon o'qilgan.
        //
        // To'liq indeks o'qilgan MILLIONLAB qatorni ham saqlab, har
        // yozuvda yangilanardi. `WHERE`li qisman indeks esa faqat
        // o'qilmaganlarni ushlaydi — u kichik, issiq (keshda turadi) va
        // xabar o'qilgan deb belgilanishi bilan indeksdan CHIQIB ketadi.
        // ============================================================
        builder.HasIndex(m => new { m.StaffId, m.StudentId })
            .HasFilter("""NOT "ReadByStaff" """)
            .HasDatabaseName("IX_DirectMessages_UnreadByStaff");

        builder.HasIndex(m => new { m.StudentId, m.StaffId })
            .HasFilter("""NOT "ReadByStudent" """)
            .HasDatabaseName("IX_DirectMessages_UnreadByStudent");

        /* ===== R40 · DARS SAVOLLARI NAVBATI ===== */

        // ============================================================
        // ★ INDEKS 1 — XODIMNING DARS SAVOLLARI NAVBATI (QISMAN).
        //
        // So'rov: "menga kelgan, DARSGA bog'langan savollar, vaqt
        // bo'yicha" —
        //   WHERE "StaffId" = @s AND "ModuleLessonId" IS NOT NULL
        //   ORDER BY "Id" DESC LIMIT 50
        //
        // Bu ustun bugungacha HECH QAYERDA filtrlanmagan va shuning
        // uchun indekssiz edi (`docs/ISH_REJASI_2026-08-13.md` §4.6).
        // Indekssiz bu so'rov butun yozishma jadvalini skanerlardi —
        // u esa markazdagi eng katta jadvallardan biri.
        //
        // QISMAN (partial): xabarlarning KATTA QISMI umumiy savol
        // (`ModuleLessonId IS NULL`) va ular bu navbatga hech qachon
        // kirmaydi. Filtr ularni indeksdan butunlay chiqarib tashlaydi
        // — indeks kichik va issiq bo'lib qoladi (ayni sabab
        // yuqoridagi "o'qilmaganlar" indekslarida ham).
        // ============================================================
        builder.HasIndex(m => new { m.StaffId, m.Id })
            .HasFilter(""" "ModuleLessonId" IS NOT NULL""")
            .HasDatabaseName("IX_DirectMessages_LessonQuestions");

        // ============================================================
        // ★ INDEKS 2 — `ModuleLessonId` NING O'ZI (to'liq).
        //
        // IKKI ish qiladi va ikkinchisi muhimroq:
        //   1) "shu dars bo'yicha savollar" (dars kartochkasi);
        //   2) 🔴 `ON DELETE SET NULL` — dars o'chirilganda Postgres unga
        //      ishora qiluvchi HAR BIR qatorni izlaydi. Indekssiz bitta
        //      darsni o'chirish butun `DirectMessages` jadvali bo'ylab
        //      ketma-ket skan qilardi va o'quv bo'limi darsni o'chirmoqchi
        //      bo'lganda so'rov qotib qolardi.
        //
        // QISMAN QILINMADI (1-indeksdan farqi): FK tekshiruvi `NULL`
        // qatorlarni ham ko'rishi mumkin bo'lgan yo'lda ishlaydi va
        // qisman indeksga tayanmaydi.
        // ============================================================
        builder.HasIndex(m => m.ModuleLessonId)
            .HasDatabaseName("IX_DirectMessages_ModuleLessonId");

        /* ===== /R40 ===== */
    }
}
