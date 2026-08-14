namespace Zinnur.Application.Auth.Services;

/// <summary>
/// ========================================================================
/// BIR MARTALIK KIRISH KODINING SAQLASH JOYI (Redis)
/// ========================================================================
///
/// ★ NIMA UCHUN BAZADA EMAS, REDIS'DA:
///   1) Kodning umri 5 daqiqa. Baza jadvali bo'lsa unga migratsiya, indeks
///      va TOZALASH vazifasi kerak bo'lardi — ya'ni doimiy saqlash uchun
///      qurilgan idishga o'tkinchi ma'lumot solingan bo'lardi. Redis'da
///      TTL — kalitning O'Z xossasi, tozalash vazifasi umuman kerak emas.
///   2) Urinishlar hisoblagichi ATOMAR oshirilishi shart
///      (<c>ICacheService.IncrementAsync</c> — Lua skript). Bazada bu
///      qator qulfini talab qilardi.
///
/// ══════════════════════════════════════════════════════════════════════
/// 🔴 KODNING O'ZI HECH QAYERGA YOZILMAYDI — FAQAT HASH'I
///
/// Redis'ni o'qiy olgan odam (dump, `MONITOR`, xotira nusxasi) kodni
/// KO'RA OLMASLIGI kerak: aks holda u istalgan raqam uchun kirish kodini
/// o'qib, hisobni egallab olardi — ya'ni butun oqim "Telegram egaligi"
/// o'rniga "Redis'ga kirish" bilan himoyalangan bo'lib qolardi.
///
/// Har kod uchun ALOHIDA tasodifiy tuz (salt) ishlatiladi. Kod atigi 6
/// xonali, ya'ni tuzsiz SHA-256 ni oldindan hisoblab qo'yilgan jadval
/// (rainbow table) bir zumda ochardi — bir million variant.
/// ══════════════════════════════════════════════════════════════════════
///
/// ★ KALITLAR TELEFON RAQAMINING O'ZI BILAN EMAS, UNING HASH'I BILAN
/// yasaladi. Redis kaliti loglarda, `SCAN` chiqishida va monitoring
/// panellarida ochiq ko'rinadi — u yerda mijozlarning telefon raqamlari
/// turishi kerak emas.
///
/// ★ KALIT TELEFON BO'YICHA, FOYDALANUVCHI ID'SI BO'YICHA EMAS. Sabab
/// aynan hisob sanashga (enumeration) qarshi: raqam bazada BO'LMASA ham
/// hisoblagichlar oshadi, ya'ni "raqam bor" va "raqam yo'q" yo'llari
/// bir xil izlar qoldiradi.
/// </summary>
public interface IPhoneLoginCodeStore
{
    /// <summary>
    /// Yangi kodni saqlaydi (avvalgisini ALMASHTIRIB) va urinishlar
    /// hisoblagichini nolga qaytaradi.
    /// </summary>
    /// <param name="phoneNormalized">
    /// <c>User.NormalizePhone</c> natijasi. XOM ko'rinish BERILMAYDI —
    /// aks holda <c>+998 90 123 45 67</c> va <c>998901234567</c> ikki
    /// xil kalitga tushib, foydalanuvchi kodni tasdiqlay olmasdi.
    /// </param>
    /// <param name="userId">Kod AYNAN shu profil uchun berilgan.</param>
    /// <param name="code">Ochiq kod — SAQLANMAYDI, faqat hash'i yoziladi.</param>
    Task SaveAsync(string phoneNormalized, long userId, string code, CancellationToken ct = default);

    /// <summary>
    /// Kodni tekshiradi. TO'G'RI bo'lsa kodni DARHOL o'chiradi (bir martalik).
    /// </summary>
    /// <remarks>
    /// ★ TAQQOSLASH DOIMIY VAQTDA (<c>CryptographicOperations.FixedTimeEquals</c>).
    /// Oddiy <c>==</c> birinchi farq qilgan baytda to'xtaydi va javob vaqti
    /// orqali kodni bayt-bayt topish yo'lini ochib berardi.
    /// </remarks>
    Task<PhoneCodeCheck> ConsumeAsync(
        string phoneNormalized, string code, CancellationToken ct = default);

    /// <summary>
    /// Yangi kod yuborishga RUXSAT so'raydi va ruxsat berilsa oynani DARHOL
    /// yopadi (atomar). Chaqiruvchi buni kod yasashdan OLDIN chaqiradi.
    /// </summary>
    /// <remarks>
    /// ★ NIMA UCHUN "tekshir va yop" BITTA metodda: ikki alohida chaqiruv
    /// orasida poyga bo'lardi — ikkita parallel so'rov ikkalasi ham
    /// "ruxsat" javobini olib, foydalanuvchiga ikkita kod ketardi va
    /// birinchisi jimgina ishlamay qolardi.
    /// </remarks>
    Task<PhoneCodeQuota> TryReserveAsync(string phoneNormalized, CancellationToken ct = default);
}

/// <summary>Kod tekshiruvining natijasi.</summary>
public enum PhoneCodeCheck
{
    /// <summary>
    /// Kod mos kelmadi YOKI umuman yo'q (muddati o'tgan / bu raqamga
    /// so'ralmagan).
    ///
    /// ★ IKKI HOLAT ATAYLAB BIRLASHTIRILGAN: "kod muddati o'tgan" va "bu
    /// raqam uchun kod umuman so'ralmagan" ni ajratib ko'rsatish
    /// raqamning bazada BORLIGINI oshkor qilardi.
    /// </summary>
    Invalid = 0,

    /// <summary>Kod to'g'ri va ISHLATILDI (endi u yaroqsiz).</summary>
    Ok = 1,

    /// <summary>Urinishlar chegarasi tugadi — kod bekor qilindi.</summary>
    TooManyAttempts = 2,
}

/// <summary>
/// Kod so'rash kvotasining qarori.
/// </summary>
/// <param name="Allowed">Hozir yangi kod yuborish mumkinmi.</param>
/// <param name="RetryAfter">
/// Ruxsat bo'lmasa — qancha kutish kerak. Foydalanuvchiga aynan shu son
/// ko'rsatiladi; "biroz kuting" degan matn foydasiz ekani rate-limit
/// javoblarida allaqachon o'lchangan (<c>Program.cs</c>, <c>Retry-After</c>).
/// </param>
public readonly record struct PhoneCodeQuota(bool Allowed, TimeSpan RetryAfter)
{
    public static PhoneCodeQuota Pass { get; } = new(Allowed: true, TimeSpan.Zero);
}
