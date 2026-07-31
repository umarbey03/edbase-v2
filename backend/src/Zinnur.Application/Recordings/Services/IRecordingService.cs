using Zinnur.Application.Recordings.Dtos;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// DARS YOZUVI USE-CASE'LARI
/// ════════════════════════════════════════════════════════════════════════
///
/// ┌───────────────────────────────────────────────────────────────────┐
/// │ ★★ QAROR: YOZUV QO'LDA BOSHLANADI (ustoz tugma bosadi),          │
/// │    AVTOMATIK EMAS. Quyida sabab va eski tizim bilan farq.        │
/// └───────────────────────────────────────────────────────────────────┘
///
/// ── ESKI TIZIMDA QANDAY EDI ─────────────────────────────────────────────
///
/// Yozuv TO'LIQ AVTOMATIK edi: `POST /lessons/{id}/start` ichida guruhning
/// `record_enabled` bayrog'i tekshirilib, egress darhol boshlanardi. Ustozda
/// na tugma, na to'xtatish, na hattoki "yozuv ketmoqda" INDIKATORI bor edi —
/// ya'ni dars xonasidagi hech kim (o'quvchilar ham, ustozning o'zi ham)
/// yozib olinayotganini bilmasdi. Yagona boshqaruv — o'quv bo'limi guruh
/// formasidagi bitta checkbox.
///
/// ── NIMA UCHUN v2 DA BOSHQACHA ──────────────────────────────────────────
///
///  1) 🔴 ROZILIK VA JAVOBGARLIK. Yozuv — ishtirokchilarga (ko'pincha
///     bolalarga) tegishli amal. Uni BOShLAGAN ODAM bo'lishi kerak:
///     <c>SessionRecording.RequestedBy</c> aynan shuning uchun saqlanadi.
///     Tugma bosilishi bilan holat darsning o'zida ko'rinadi ("yozuv
///     ketmoqda"), ya'ni INDIKATOR muammosi qo'shimcha funksiya bilan emas,
///     TUZILISH bilan hal bo'ladi — uni unutib bo'lmaydi.
///
///  2) 🔴 YOZUV NOSOZLIGI DARSNI TO'XTATMASLIGI SHART. Egress — alohida
///     xizmat va u sekin javob berishi yoki umuman yo'q bo'lishi mumkin.
///     Uni darsni boshlash yo'liga ulash platformaning eng vaqt-tanqis
///     amaliga tashqi bog'liqlik qo'shardi. Eski tizim buni sezgan va
///     natijada AYNI ishni UCH joyda qilardi: dars boshlashda,
///     `room_started` webhook'ida va har 30 soniyalik watchdog'da — uch
///     nusxa, uchtasi ham bir-birini bilmaydi. Bu yerda BITTA yo'l bor,
///     watchdog esa faqat TUZATADI (yangi yozuv boshlamaydi).
///
///  3) HAR DARS YOZILISHI SHART EMAS: qayta o'tilgan dars, konsultatsiya,
///     texnik sinov. Guruh darajasidagi bitta checkbox bu farqni
///     ifodalay olmasdi.
///
///  4) EGRESS — ENG QIMMAT RESURS (transkodlash + tarmoq). Boshlanib,
///     lekin hech kim kelmagan darsni yozib o'tirish bekor sarf.
///
/// ⚠️ "USTOZ UNUTADI" XAVFI OCHIQ TAN OLINADI. U ikki narsa bilan
/// qoplanadi: (a) yozuv holati dars kartochkasida DOIM ko'rinadi, ya'ni
/// "yozuv o'chiq" degani ekranda turadi; (b) kelajakda guruh darajasidagi
/// "avtomatik yozish" bayrog'i shu servisdan foydalanib qo'shilishi mumkin —
/// model va watchdog buni allaqachon ko'taradi. Bu bosqichda u ATAYLAB
/// yo'q: avtomatik rejim rozilik masalasini qaytadan ochadi va u biznes
/// qarori, texnik qaror emas.
///
/// ── RUXSAT ──────────────────────────────────────────────────────────────
///
/// Ruxsat qoidasi BU YERDA TAKRORLANMAYDI: servis
/// <c>ILiveSessionService</c> ni chaqiradi va u darsga kirish huquqini
/// allaqachon tekshiradi (a'zolik, faol guruh, faol profil, host'lik).
/// Ikkinchi nusxa yozilsa, vaqt o'tib ular ajralib ketardi va bir yo'lda
/// zaifroq tekshiruv qolardi.
/// </summary>
public interface IRecordingService
{
    /// <summary>
    /// Yozuvni boshlaydi (faqat dars HOSTI, faqat JONLI dars).
    ///
    /// IDEMPOTENT: yozuv allaqachon ketayotgan bo'lsa AYNI qator qaytadi,
    /// ikkinchi egress boshlanmaydi.
    ///
    /// ⚠️ Egress javob bermasa metod ISTISNO TASHLAMAYDI: qator
    /// <c>Requested</c> holatida saqlanadi, xato matni <c>Error</c> da
    /// qaytadi va watchdog qayta uradi. Ustoz darsni odatdagidek davom
    /// ettiradi.
    /// </summary>
    Task<RecordingDto> StartAsync(long sessionId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Yozuvni to'xtatadi (faqat host). Fayl DARHOL tayyor bo'lmaydi —
    /// yakuniy holat webhook bilan keladi.
    /// </summary>
    Task<RecordingDto> StopAsync(long sessionId, long actorId, CancellationToken ct = default);

    /// <summary>Darsning barcha yozuv urinishlari (yangisi birinchi).</summary>
    Task<IReadOnlyList<RecordingDto>> ListForSessionAsync(
        long sessionId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// "Dars yozuvlari" bo'limi: sana oralig'idagi yozuvlar.
    ///
    /// ★ RO'YXAT QAMROVI KALENDAR ORQALI OLINADI
    /// (<c>ILiveSessionService.GetCalendarAsync</c>): u foydalanuvchi
    /// ko'ra oladigan darslarni ROL bo'yicha allaqachon filtrlaydi va bu
    /// testlar bilan qoplangan. Shu tufayli bu yerda ikkinchi (va albatta
    /// bir kun ajralib ketadigan) ruxsat so'rovi yozilmaydi.
    /// </summary>
    Task<IReadOnlyList<RecordingListItemDto>> ListAsync(
        long actorId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

    /// <summary>
    /// Ko'rish uchun MUDDATLI imzolangan havola.
    ///
    /// 🔴 TO'LOV DARVOZASI AYNAN SHU YERDA. Havola berilgandan keyin
    /// serverning "yo'q" deyishi mumkin emas — brauzer to'g'ridan-to'g'ri
    /// omborga boradi. Ya'ni tekshiruv HAVOLA BERILISHIDAN oldin bo'lishi
    /// shart (bu <c>ILiveSessionService.CreateJoinTokenAsync</c> dagi AYNI
    /// mulohaza: LiveKit tokeni ham shunday).
    /// </summary>
    Task<RecordingLinkDto> CreateViewLinkAsync(
        long recordingId, long actorId, CancellationToken ct = default);
}
