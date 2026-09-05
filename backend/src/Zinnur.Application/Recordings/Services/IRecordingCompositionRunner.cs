namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// Tungi yig'ishning BITTA aylanishi: navbatdan qator oladi, uni yig'adi,
/// natijani yozadi va xom fayllarni tozalaydi.
///
/// ★ NIMA UCHUN <c>BackgroundService</c> DAN AJRATILGAN —
/// <c>IOutboxDispatcher</c> dagi AYNI mulohaza: hosting (qachon uyg'onish,
/// qancha kutish, qanday to'xtash) WebApi qatlamining ishi; "qator qanday
/// egallanadi, nosozlikda nima bo'ladi, uzilishda nima bo'ladi" esa BIZNES
/// qoidasi va u shu yerda, haqiqiy baza bilan sinaladi. Test aylanishni
/// O'ZI chaqiradi va natijani darhol tekshiradi.
///
/// 🔴 TUNGI OYNA VA UMUMIY KALIT (<c>recordings.track_pipeline_enabled</c>)
/// BU YERDA TEKSHIRILMAYDI. Ular — "hozir umuman ishlaymizmi" degan
/// HOSTING savoli va ular <c>RecordingCompositionWorker</c> da, oynaning
/// o'zi esa sof funksiyada (<c>RecordingCompositionWindow</c>). Ya'ni bu
/// interfeys chaqirilgan bo'lsa, ishlash uchun ruxsat ALLAQACHON bor.
///
/// ⚠️ BEKOR QILISH SIGNALI — TUNGI OYNANING TUGASHI. Chaqiruvchi
/// <c>ct</c> ni oyna tugashiga moslab beradi; bu yerda u nosozlik EMAS,
/// "keyingi kechada davom etadi" degan ma'noni oladi va urinishlar
/// hisoblagichini SARFLAMAYDI.
/// </summary>
public interface IRecordingCompositionRunner
{
    /// <summary>Bitta aylanish. Navbat bo'sh bo'lsa arzon tugaydi.</summary>
    Task<CompositionCycleResult> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>Bitta aylanishning natijasi — hosting qatlami LOG uchun o'qiydi.</summary>
/// <param name="Outcome">Nima bo'lgani.</param>
/// <param name="RecordingId">Qaysi yozuv bilan ishlangani (bo'lsa).</param>
/// <param name="PurgedRecordings">
/// Bu aylanishda xom fayllari tozalangan yozuvlar soni. Tozalash faqat
/// navbat BO'SH bo'lganda qilinadi: u kechikishga chidaydi, kodlash esa
/// yo'q.
/// </param>
public readonly record struct CompositionCycleResult(
    CompositionCycleOutcome Outcome,
    long? RecordingId,
    int PurgedRecordings)
{
    public static CompositionCycleResult Idle(int purged = 0) =>
        new(CompositionCycleOutcome.Idle, null, purged);

    public static CompositionCycleResult For(CompositionCycleOutcome outcome, long recordingId) =>
        new(outcome, recordingId, 0);
}

/// <summary>Aylanishning yakuni.</summary>
public enum CompositionCycleOutcome
{
    /// <summary>Navbatda ish yo'q (yoki ombor sozlanmagan).</summary>
    Idle = 0,

    /// <summary>Tayyor mp4 omborga tushdi va yozuv yakunlandi.</summary>
    Completed = 1,

    /// <summary>Yiqildi, lekin urinish qoldi — qator navbatga qaytdi.</summary>
    Retrying = 2,

    /// <summary>Urinishlar tugadi — yozuv YAKUNIY xato.</summary>
    Failed = 3,

    /// <summary>
    /// Tungi oyna tugadi (yoki konteyner to'xtatilmoqda) — ish KEYINGI
    /// KECHAGA qoldirildi. Bu NOSOZLIK EMAS.
    /// </summary>
    Interrupted = 4,
}
