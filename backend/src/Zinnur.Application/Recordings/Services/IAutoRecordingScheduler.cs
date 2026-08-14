using Zinnur.Domain.Entities;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// AVTOMATIK YOZUV — DARS BOSHLANGANDA NAVBATGA QO'YISH
/// ════════════════════════════════════════════════════════════════════════
///
/// ┌───────────────────────────────────────────────────────────────────┐
/// │ ★★ QAROR (2026-08-13, loyiha egasi): YOZUV AVTOMATIK.             │
/// │    Guruh kaliti — <c>Group.RecordEnabled</c>.                     │
/// └───────────────────────────────────────────────────────────────────┘
///
/// To'liq qaror va uning eski (qo'lda boshlash) qarori bilan farqi
/// <see cref="IRecordingService"/> izohida. Bu yerda faqat MEXANIKA.
///
/// ── NIMA UCHUN "NAVBAT", "BOSHLASH" EMAS ────────────────────────────────
///
/// 🔴 EGRESS DARS BOSHLASH YO'LIDA CHAQIRILMAYDI. Bu — eski qarordan
/// SAQLANIB QOLGAN yagona muhim qoida va u bekor qilinmadi: Egress alohida
/// xizmat, u sekin javob berishi yoki umuman yo'q bo'lishi mumkin, dars
/// boshlash esa platformaning eng vaqt-tanqis amali. Uni kutib turish
/// "yozuv ishlamayapti" nosozligini "dars ochilmayapti" nosozligiga
/// aylantirardi.
///
/// Shuning uchun bu yerda BITTA arzon amal bajariladi: <c>Requested</c>
/// holatidagi qator <see cref="LiveSession"/> ni jonli qilayotgan AYNI
/// tranzaksiyaga qo'shiladi. Egress'ga murojaatni esa
/// <c>RecordingWatchdogJob</c> qiladi — u allaqachon qayta urinish,
/// muhlat va taslim bo'lish mantiqiga ega. Ya'ni:
///
///   <c>SessionRecordings</c> jadvali = NAVBAT, watchdog = uni bo'shatuvchi.
///
/// ★ NARXI OChIQ AYTILADI: yozuv dars boshlanishidan
/// <c>RecordingWatchdogSettings.Interval</c> gacha KECHIKADI (hozir 15 s).
/// Bu ongli almashuv — muqobili yo Egress'ni kutish (yuqoridagi 🔴), yo
/// <c>Task.Run</c> bilan "otib yuborish" bo'lardi. Ikkinchisi bu kod
/// bazasida YO'Q va ataylab qo'shilmadi: u scoped <c>DbContext</c> ni
/// jarayon tashqarisiga olib chiqadi, istisnosi hech qayerda ko'rinmaydi
/// va qayta urinishi yo'q — ya'ni watchdog'ning nusxasini, faqat yomonroq
/// qilib yozish bo'lardi.
///
/// ── TRANZAKSIYA CHAQIRUVCHINIKI ─────────────────────────────────────────
///
/// ⚠️ <c>SaveChanges</c> BU YERDA CHAQIRILMAYDI (<c>RecordingStarter</c>
/// bilan AYNI qoida). Qator darsni <c>Live</c> qilayotgan saqlash bilan
/// BIRGA yoziladi: dars jonli bo'lib, navbat qatori esa yo'qolgan holat
/// yuzaga kela olmaydi.
/// </summary>
public interface IAutoRecordingScheduler
{
    /// <summary>
    /// Dars uchun avtomatik yozuvni navbatga qo'yadi.
    ///
    /// HECH QACHON ISTISNO KO'TARMAYDI va HECH QACHON tashqi xizmatga
    /// bormaydi — u dars boshlash yo'lida turadi.
    /// </summary>
    /// <param name="session">
    /// AYNI <c>DbContext</c> da kuzatilayotgan (tracked) dars. Guruh
    /// <c>Include</c> qilingan bo'lsa qo'shimcha so'rov ketmaydi.
    /// </param>
    /// <returns>
    /// Navbatga qator qo'shildimi. <c>false</c> — guruhda yozuv o'chiq,
    /// yozuv allaqachon navbatda/ketmoqda yoki xizmat sozlanmagan.
    /// </returns>
    Task<bool> EnqueueAsync(LiveSession session, CancellationToken ct = default);
}
