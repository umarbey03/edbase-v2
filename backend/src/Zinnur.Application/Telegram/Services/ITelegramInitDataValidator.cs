namespace Zinnur.Application.Telegram.Services;

/// <summary>
/// Mini App <c>initData</c> sini tekshiruvchi PORT.
///
/// ★ NIMA UCHUN PORT KERAK, <see cref="TelegramInitData"/> ni to'g'ridan-
/// to'g'ri chaqirish yetarli emas: tekshiruv BOT TOKENI va HOZIRGI VAQTni
/// talab qiladi. Ikkalasi ham use-case'ning ishi emas — token
/// konfiguratsiyadan (Infrastructure), vaqt esa <c>TimeProvider</c> dan
/// keladi. Port shu ikki bog'liqlikni Application qatlamidan yashiradi.
///
/// Algoritmning O'ZI Application'da qoladi (sof funksiya, unit test bilan
/// qoplangan) — bu yerdagi implementatsiya faqat "token + vaqt" ni beradi.
/// </summary>
public interface ITelegramInitDataValidator
{
    /// <summary>
    /// Telegram integratsiyasi sozlanganmi (bot tokeni bor-yo'qligi).
    /// <c>false</c> bo'lsa Mini App kirishi 503 qaytaradi — bu 500 emas,
    /// chunki bu bizning bug'imiz emas, sozlanmagan xizmat
    /// (<c>StorageOptions</c> bilan bir xil falsafa).
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>Istisno TASHLAMAYDI — natija obyektida qaytadi.</summary>
    TelegramInitDataResult Validate(string? initData);
}
