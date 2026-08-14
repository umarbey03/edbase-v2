using Zinnur.Application.Auth.Dtos;

namespace Zinnur.Application.Auth.Services;

/// <summary>
/// ========================================================================
/// TELEFON + BIR MARTALIK KOD BILAN KIRISH (2026-08-13)
/// ========================================================================
///
/// ★ NIMA UCHUN BU OQIM UMUMAN QURILDI
///
/// Loyiha egasining qarori bilan email va parol bilan kirish BUTUNLAY
/// olib tashlandi. "Faqat telefon orqali" degan talabni mavjud Mini App
/// oqimi YOLG'IZ bajara olmasdi, chunki:
///   • xodimlar ish stolida, oddiy brauzerda ishlaydi;
///   • Mini App qobig'i o'quvchi shakliga qurilgan;
///   • Telegram Login Widget kod bazasida umuman yozilmagan.
///
/// Shuning uchun bu oqim HAR QANDAY brauzerda ishlaydi va egalikni
/// TELEGRAM tasdiqlaydi: kod faqat profilga BOG'LANGAN Telegram hisobiga
/// yuboriladi. Ya'ni "telefon raqamini bilish" yetarli emas — o'sha
/// raqamga bog'langan Telegram hisobiga KIRA OLISH kerak.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ ESKI TIZIMNING X-1 ZAIFLIGI BU YERDA QAYTMAYDI
///
/// Eski tizimda telefon raqam so'rov tanasidan olinardi va SHU BILAN
/// kirish berilardi — ya'ni admin raqamini yozgan odam admin bo'lardi.
/// Bu yerda ham raqam so'rov tanasidan keladi, LEKIN u faqat "kimga kod
/// yuborilsin" degan savolga javob beradi. Kirish esa KOD bilan bo'ladi,
/// kod esa hujumchi ko'ra olmaydigan kanalga — jabrlanuvchining Telegram
/// hisobiga — ketadi.
///
/// 🔴 SHU SABABLI TELEGRAM BOG'LANISHI MAJBURIY. Bog'lanmagan profil
/// uchun kod yuboriladigan joy yo'q, ya'ni oqim boshlanmaydi ham.
/// ══════════════════════════════════════════════════════════════════════
///
/// ★ TOKEN BU YERDA YASALMAYDI. Kod tekshirilgach ish
/// <see cref="IAuthService.LoginWithPhoneAsync"/> ga o'tadi — AYNI
/// <c>ITelegramMiniAppAuth</c> naqshi: modul EGALIKNI tekshiradi,
/// tokenni esa yagona joydan oladi. Ikkinchi, parallel token yo'li
/// yozilishi <c>IAuthService</c> izohida QAT'IY taqiqlangan.
/// </summary>
public interface IPhoneLoginService
{
    /// <summary>
    /// Kod so'raydi va uni Telegram orqali yuborish uchun navbatga qo'yadi.
    /// </summary>
    /// <remarks>
    /// 🔴 JAVOB HAR DOIM BIR XIL. Raqam bazada bo'lmasa ham, profil faol
    /// bo'lmasa ham, Telegram bog'lanmagan bo'lsa ham — AYNI
    /// <see cref="PhoneCodeResponse"/> qaytadi va istisno TASHLANMAYDI.
    ///
    /// Sabab: aks holda bu endpoint "bu raqam markazda o'qiydimi?" degan
    /// savolga javob beradigan ochiq qidiruv vositasi bo'lardi. O'zbekiston
    /// mobil raqamlari makoni kichik (9 xona) va uni to'liq skanerlash
    /// arzon — natijada butun mijozlar bazasi tiklanardi.
    ///
    /// ★ ISTISNO FAQAT IKKI HOLATDA:
    ///   • <c>TooManyRequestsException</c> — kvota (raqam bo'yicha, mavjud
    ///     bo'lmagan raqamlarga ham bir xil qo'llanadi, ya'ni oshkor
    ///     qilmaydi);
    ///   • <c>ServiceUnavailableException</c> — Telegram umuman sozlanmagan
    ///     (kodni yuboradigan kanal yo'q). Bu ham raqamga bog'liq emas.
    /// </remarks>
    Task<PhoneCodeResponse> RequestCodeAsync(
        PhoneCodeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Kodni tekshiradi va to'g'ri bo'lsa SESSIYA ochadi.
    /// Xato kodda <c>UnauthorizedException</c> (401).
    /// </summary>
    Task<AuthResponse> VerifyAsync(PhoneVerifyRequest request, CancellationToken ct = default);
}
