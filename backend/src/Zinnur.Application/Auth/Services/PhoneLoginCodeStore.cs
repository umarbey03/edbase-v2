using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Zinnur.Application.Common.Interfaces;

namespace Zinnur.Application.Auth.Services;

/// <summary>
/// <see cref="IPhoneLoginCodeStore"/> — Redis (<see cref="ICacheService"/>) ustida.
///
/// ★ NIMA UCHUN <c>ICacheService</c> ORQALI, <c>IConnectionMultiplexer</c>
/// GA TO'G'RIDAN-TO'G'RI EMAS: Application qatlami Redis kutubxonasini
/// ko'rmasligi kerak, va kalit MAKONI (`Redis:KeyPrefix`) aynan shu port
/// ichida qo'yiladi — bitta Redis'ni bo'lishadigan dev/staging va
/// integratsiya testlari bir-birining kodini o'chirib yubormasin.
/// </summary>
public sealed class PhoneLoginCodeStore(ICacheService cache, TimeProvider clock)
    : IPhoneLoginCodeStore
{
    /// <summary>
    /// Kodning umri.
    ///
    /// ★ 5 DAQIQA — IKKI TOMONLAMA KELISHUV. Qisqaroq bo'lsa Telegram
    /// yetkazishi sekinlashgan paytda (outbox worker + Bot API) kod
    /// foydalanuvchi ko'rgunicha o'lardi. Uzunroq bo'lsa o'g'irlangan
    /// telefon ekranidagi bildirishnoma bilan kirish oynasi cho'zilardi.
    /// </summary>
    public static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Ikki kod so'rovi orasidagi eng qisqa oraliq.
    ///
    /// ★ BU RATE-LIMIT SIYOSATINING O'RNINI BOSMAYDI, USTIGA QO'YILADI.
    /// HTTP darajasidagi cheklov IP bo'yicha bo'linadi va proksi ortida
    /// hamma bitta bo'limga tushadi (`Program.cs` dagi ogohlantirish).
    /// Bu esa RAQAM bo'yicha: bitta odamning telefoniga xabar yog'dirish
    /// yo'lini yopadi, va u qaysi IP'dan kelishidan qat'i nazar ishlaydi.
    /// </summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Bitta raqamga sutkada yuboriladigan kodlar chegarasi.
    ///
    /// 🔴 BUSIZ 60 SEKUNDLIK OYNA HUJUMNI FAQAT SEKINLASHTIRARDI: sutkada
    /// 1440 ta xabar — bu foydalanuvchi uchun "bot meni bezovta qilyapti"
    /// degani va u botni BLOKLAB qo'yardi, ya'ni hujumchi maqsadiga
    /// erishardi (qurbon endi umuman kira olmaydi).
    /// </summary>
    public const int MaxCodesPerDay = 10;

    /// <summary>
    /// Bitta kod uchun ruxsat etilgan tekshiruv urinishlari.
    ///
    /// 6 xonali kod = 1 000 000 variant. 5 urinish bilan tasodifan topish
    /// ehtimoli 1:200 000 — bu kod umri (5 daqiqa) davomida amalda
    /// erishib bo'lmaydigan qiymat.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <inheritdoc />
    public async Task SaveAsync(
        string phoneNormalized, long userId, string code, CancellationToken ct = default)
    {
        var slug = Slug(phoneNormalized);

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Hash(salt, code);

        var record = new StoredCode(
            Convert.ToHexString(salt),
            Convert.ToHexString(hash),
            userId,
            clock.GetUtcNow());

        // Urinishlar hisoblagichi AVVAL tozalanadi: eski koddan qolgan
        // "4 ta urinish" yangi kodni bir urinishda o'ldirib qo'yardi va
        // foydalanuvchi sababini bilmasdi.
        await cache.RemoveAsync(AttemptKey(slug), ct).ConfigureAwait(false);
        await cache.SetAsync(CodeKey(slug), record, CodeTtl, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PhoneCodeCheck> ConsumeAsync(
        string phoneNormalized, string code, CancellationToken ct = default)
    {
        var slug = Slug(phoneNormalized);

        // ★ URINISH HISOBLAGICHI KODNI O'QISHDAN OLDIN OSHIRILADI.
        //
        // Aks holda "kod yo'q" shoxi hisoblagichga umuman tegmasdi va
        // hujumchi mavjud bo'lmagan raqamlar bo'yicha cheksiz urinardi —
        // ya'ni chegara faqat haqiqiy foydalanuvchiga qo'llanardi.
        var attempts = await cache
            .IncrementAsync(AttemptKey(slug), CodeTtl, ct)
            .ConfigureAwait(false);

        if (attempts > MaxAttempts)
        {
            // Kod BEKOR QILINADI. Faqat "urinish ko'p" deb qaytarish
            // yetarli emas edi: chegara oynasi tugagach o'sha kod yana
            // yaroqli bo'lib qolardi va hujumchi 5 talik paketlar bilan
            // davom etaverardi.
            await cache.RemoveAsync(CodeKey(slug), ct).ConfigureAwait(false);
            return PhoneCodeCheck.TooManyAttempts;
        }

        var record = await cache.GetAsync<StoredCode>(CodeKey(slug), ct).ConfigureAwait(false);

        if (record is null)
            return PhoneCodeCheck.Invalid;

        var expected = Convert.FromHexString(record.Hash);
        var actual = Hash(Convert.FromHexString(record.Salt), code);

        // Doimiy vaqtli taqqoslash — sabab interfeys izohida.
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            return PhoneCodeCheck.Invalid;

        // BIR MARTALIK: kod ham, hisoblagich ham darhol o'chiriladi.
        //
        // ★ POYGA HAQIDA HALOL IZOH: ikki bir vaqtdagi TO'G'RI kodli so'rov
        //   ikkalasi ham o'tib ketishi mumkin (o'qish va o'chirish atomar
        //   emas). Bu XAVF EMAS: ikkala so'rov ham AYNI odamning AYNI
        //   kodidan kelgan va natija ikkita token — ya'ni "ikki qurilmada
        //   kirdim" holati. Atomar `GETDEL` qo'shish uchun `ICacheService`
        //   shartnomasini kengaytirish kerak bo'lardi va u faqat shu
        //   zararsiz holat uchun ishlatilardi.
        await cache.RemoveAsync(CodeKey(slug), ct).ConfigureAwait(false);
        await cache.RemoveAsync(AttemptKey(slug), ct).ConfigureAwait(false);

        return PhoneCodeCheck.Ok;
    }

    /// <inheritdoc />
    public async Task<PhoneCodeQuota> TryReserveAsync(
        string phoneNormalized, CancellationToken ct = default)
    {
        var slug = Slug(phoneNormalized);

        // ATOMAR: `INCR` + birinchi oshirishda `PEXPIRE` (Lua). Natija 1
        // bo'lsa — oyna endi ochildi, ya'ni ruxsat AYNAN shu chaqiruvga
        // tegishli. 2 va undan katta bo'lsa oyna allaqachon band.
        var recent = await cache
            .IncrementAsync(ResendKey(slug), ResendCooldown, ct)
            .ConfigureAwait(false);

        if (recent > 1)
            return new PhoneCodeQuota(Allowed: false, ResendCooldown);

        var daily = await cache
            .IncrementAsync(DailyKey(slug), TimeSpan.FromDays(1), ct)
            .ConfigureAwait(false);

        if (daily > MaxCodesPerDay)
            return new PhoneCodeQuota(Allowed: false, TimeSpan.FromDays(1));

        return PhoneCodeQuota.Pass;
    }

    // ================================================================ ichki

    /// <summary>Tuz uzunligi — 16 bayt (128 bit) oldindan hisoblashni ma'nosiz qiladi.</summary>
    private const int SaltLength = 16;

    private static byte[] Hash(byte[] salt, string code)
    {
        // Kod bo'sh yoki bo'shliqli bo'lsa ham hisoblanadi — chaqiruvchi
        // "bo'sh kod" ni alohida holat sifatida ushlamasin, u shunchaki
        // MOS KELMAYDIGAN kod bo'lsin (yagona javob yo'li).
        var payload = Encoding.UTF8.GetBytes(code ?? string.Empty);

        var buffer = new byte[salt.Length + payload.Length];
        salt.CopyTo(buffer, 0);
        payload.CopyTo(buffer, salt.Length);

        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Telefon raqamidan KALIT BO'LAGI yasaydi.
    ///
    /// 🔴 Raqamning O'ZI kalitga tushmaydi (sabab interfeys izohida).
    /// Qisqartirish 16 belgigacha — 64 bit, ya'ni tasodifiy to'qnashuv
    /// amalda mumkin emas, kalit esa qisqa qoladi.
    /// </summary>
    private static string Slug(string phoneNormalized)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(phoneNormalized ?? string.Empty));
        return Convert.ToHexString(digest)[..16];
    }

    private static string CodeKey(string slug) =>
        string.Create(CultureInfo.InvariantCulture, $"auth:phone:code:{slug}");

    private static string AttemptKey(string slug) =>
        string.Create(CultureInfo.InvariantCulture, $"auth:phone:attempt:{slug}");

    private static string ResendKey(string slug) =>
        string.Create(CultureInfo.InvariantCulture, $"auth:phone:resend:{slug}");

    private static string DailyKey(string slug) =>
        string.Create(CultureInfo.InvariantCulture, $"auth:phone:daily:{slug}");

    /// <summary>
    /// Redis'da yotadigan yozuv. <c>public</c> EMAS: kod hash'ining shakli
    /// bu sinfning ichki ishi.
    /// </summary>
    /// <param name="Salt">Hex ko'rinishdagi tuz.</param>
    /// <param name="Hash">Hex ko'rinishdagi SHA-256(tuz + kod).</param>
    /// <param name="UserId">Kod QAYSI profil uchun berilgani.</param>
    /// <param name="IssuedAt">Diagnostika uchun (nosozlikni tekshirishda).</param>
    private sealed record StoredCode(string Salt, string Hash, long UserId, DateTimeOffset IssuedAt);
}
