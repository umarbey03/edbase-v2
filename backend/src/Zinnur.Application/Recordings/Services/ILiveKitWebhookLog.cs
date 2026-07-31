namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// Ishlangan LiveKit hodisalarining izi (IDEMPOTENTLIK).
///
/// ★ NIMA UCHUN KERAK: LiveKit webhook'dan 200 kutadi va javob kechiksa
/// yoki tarmoq uzilsa AYNI hodisani QAYTA yuboradi. Himoyasiz holatda
/// bitta <c>egress_ended</c> ikki marta ishlanardi.
///
/// ★ NIMA UCHUN XOTIRADA (yoki Redis'da) EMAS: API bir necha konteynerda
/// ishlaydi va takror hodisa BOSHQA instansiyaga tushishi mumkin. Bundan
/// tashqari bu yerda yozuv biznes o'zgarishi bilan BITTA tranzaksiyada
/// saqlanishi kerak — aks holda "belgilandi, lekin qayta ishlanmadi"
/// holati paydo bo'lardi.
///
/// ⚠️ Naqsh AYNAN <c>ITelegramUpdateLog</c> dagidek: port Application'da,
/// jadval Infrastructure'da (u BIZNES ma'lumoti emas, YETKAZIB BERISH
/// mexanizmi), <c>SaveChanges</c> chaqiruvchida.
/// </summary>
public interface ILiveKitWebhookLog
{
    /// <summary>
    /// Hodisani "ishlanmoqda" deb belgilaydi.
    /// </summary>
    /// <param name="eventId">
    /// LiveKit bergan hodisa Id'si. U bo'lmasa chaqiruvchi TANA XESHINI
    /// beradi — ya'ni bir xil tana ikki marta kelsa baribir to'siladi.
    /// </param>
    /// <returns>
    /// <c>true</c> — birinchi marta ko'ryapmiz; <c>false</c> — takror
    /// (chaqiruvchi hech narsa qilmaydi va LiveKit'ga 200 qaytaradi).
    /// </returns>
    Task<bool> TryBeginAsync(string eventId, CancellationToken ct = default);
}
