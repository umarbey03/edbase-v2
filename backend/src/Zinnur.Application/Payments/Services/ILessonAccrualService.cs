namespace Zinnur.Application.Payments.Services;

/// <summary>
/// BOSQICHMA-BOSQICH HISOBLASH (2026-08-16) — bir dars YAKUNLANGANDA
/// ("Yakunlash" tugmasi yoki avto-yakunlash fon vazifasi orqali) shu
/// darsdagi har o'quvchining oylik to'lov yozuviga bitta dars ulushini
/// qo'shadi. Chaqiruvchi (<c>LiveSessionService.EndAsync</c>) — YAGONA
/// chaqiruv nuqtasi (izoh: <c>LessonAccrualService</c>).
/// </summary>
public interface ILessonAccrualService
{
    /// <summary>
    /// Darsni hisoblaydi. IDEMPOTENT — shu darsga allaqachon hisoblangan
    /// bo'lsa (<c>LessonCharge</c> bor), jimgina qaytadi.
    ///
    /// ⚠️ ISTISNO OTMAYDI: xato ICHKARIDA ushlanadi va logga yoziladi.
    /// Sabab — bu metod dars YAKUNLASH oqimining bir qismi va pul bilan
    /// bog'liq muvaffaqiyatsizlik "dars yakunlanmadi" degan noto'g'ri
    /// taassurot qoldirmasligi kerak (dars HAQIQATDA yakunlangan; hisoblash
    /// keyinroq qo'lda yoki keyingi urinishda tuzatilishi mumkin).
    /// </summary>
    Task AccrueForSessionAsync(long sessionId, long actorId, CancellationToken ct = default);
}
