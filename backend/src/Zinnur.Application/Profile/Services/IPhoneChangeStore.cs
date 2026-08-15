namespace Zinnur.Application.Profile.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TELEFON ALMASHTIRISH NIYATI — "KUTAYOTGAN O'ZGARISH" OMBORI
/// ════════════════════════════════════════════════════════════════════════
///
/// Loyiha egasi (2026-08-15): *"nomerini alishtirish imkoniyati ham
/// bo'lsin, lekin bunda ham registerdagi kabi telegram orqali tasdiqlash
/// majburiy bo'lishi shart"*.
///
/// ── NIMA UCHUN UMUMAN "NIYAT" KERAK ────────────────────────────────────
///
/// Kirishda (`PhoneLoginService`) kod YO'LI oddiy: raqam bazada bor,
/// unga bog'langan Telegram ham bor — kod o'sha manzilga ketadi.
///
/// ALMASHTIRISHDA esa YANGI raqam hech kimga tegishli EMAS va unga
/// bog'langan Telegram hisobi ham YO'Q. Ya'ni kodni yuboradigan manzil
/// mavjud emas. Yagona yo'l — foydalanuvchi o'sha raqamdan botga
/// «Raqamni ulashish» yuborsin, shunda bot uning Telegram hisobini
/// KO'RADI va kodni o'sha yerga yuboradi.
///
/// Bot esa "bu raqam nima uchun kelyapti?" degan savolga javob topishi
/// kerak — hozirgi qoida bo'yicha u notanish raqamni RAD ETADI
/// (`TelegramUpdateHandler`: "AKKAUNT YARATILMAYDI"). Shu ombor aynan
/// o'sha javobni beradi: *"bu raqamni falon foydalanuvchi o'ziga
/// biriktirmoqchi"*.
///
/// ── NIMA UCHUN REDIS, NEGA JADVAL EMAS ─────────────────────────────────
///
/// Yozuv QISQA UMRLI (<see cref="Ttl"/>) va tugagach hech kimga kerak
/// emas — bu biznes ma'lumoti emas, oqimning oraliq holati.
/// `PhoneLoginCodeStore` ham AYNI sababga ko'ra Redis'da. Jadval bo'lsa
/// unga tozalash vazifasi ham kerak bo'lardi.
///
/// 🔴 KODNING O'ZI BU YERDA SAQLANMAYDI. Kod — `IPhoneLoginCodeStore`
/// ning ishi (hash + tuz + urinishlar chegarasi + TTL) va u YANGI raqam
/// bo'yicha kalitlanadi. Ikkinchi kod mexanizmi yozilsa, uning
/// chegaralari birinchisidan asta ajralib ketardi.
/// </summary>
public interface IPhoneChangeStore
{
    /// <summary>
    /// Niyat qancha yashaydi.
    ///
    /// ★ 15 DAQIQA — kod umridan (5 daqiqa) UZUNROQ va bu ataylab: bu
    /// oraliqda foydalanuvchi ilovadan chiqib, Telegram'ni ochib, botni
    /// topib, «Raqamni ulashish» tugmasini bosishi kerak. 5 daqiqa
    /// bunga yetmasdi — ayniqsa bot bilan birinchi marta muloqot
    /// qilayotgan odamga.
    /// </summary>
    static TimeSpan Ttl => TimeSpan.FromMinutes(15);

    /// <summary>Niyatni saqlaydi (ikkala yo'nalish bo'yicha ham topiladi).</summary>
    Task SaveAsync(PendingPhoneChange pending, CancellationToken ct = default);

    /// <summary>
    /// YANGI raqam bo'yicha topadi — BOT shu yo'ldan yuradi (u faqat
    /// kontaktdagi raqamni biladi, foydalanuvchi Id'sini emas).
    /// </summary>
    Task<PendingPhoneChange?> FindByPhoneAsync(
        string phoneNormalized, CancellationToken ct = default);

    /// <summary>
    /// Foydalanuvchi bo'yicha topadi — ILOVA shu yo'ldan yuradi (u
    /// tokendan `userId` ni biladi va foydalanuvchi faqat kodni kiritadi,
    /// raqamni qaytadan yozmaydi).
    /// </summary>
    Task<PendingPhoneChange?> FindByUserAsync(long userId, CancellationToken ct = default);

    /// <summary>Niyatni o'chiradi (tasdiqlangach yoki bekor qilinganda).</summary>
    Task RemoveAsync(PendingPhoneChange pending, CancellationToken ct = default);
}

/// <summary>
/// Kutayotgan telefon almashtirish.
/// </summary>
/// <param name="UserId">Kim almashtirmoqchi (TOKENDAN olingan).</param>
/// <param name="PhoneNormalized">YANGI raqam — <c>+998901234567</c> ko'rinishida.</param>
/// <param name="TelegramId">
/// Raqamni botga ULASHGAN Telegram hisobi. Bot kontaktni qabul qilgunga
/// qadar <c>null</c>.
///
/// 🔴 TASDIQLANGANDAN KEYIN PROFIL AYNAN SHU HISOBGA BOG'LANADI. Sabab:
/// kirish kodi HAR DOIM `User.TelegramId` ga yuboriladi — agar profil
/// eski hisobda qolsa, foydalanuvchi yangi raqamini kiritib kirmoqchi
/// bo'lganda kod ESKI Telegram'ga ketardi va u tizimga kira olmasdi.
/// </param>
/// <param name="TelegramUsername">Ko'rsatish uchun (@ belgisiz).</param>
public sealed record PendingPhoneChange(
    long UserId,
    string PhoneNormalized,
    long? TelegramId = null,
    string? TelegramUsername = null)
{
    /// <summary>Bot kontaktni qabul qilib, kod yuborganmi.</summary>
    public bool CodeSent => TelegramId is not null;
}
