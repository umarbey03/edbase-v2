using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zinnur.Application.Media;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// MEDIA CHIPTASI — HMAC-SHA256, HOLATSIZ
/// ════════════════════════════════════════════════════════════════════════
///
/// Port va TANLASH SABABI: <see cref="IMediaAccessTicketService"/>.
/// Bu yerda faqat MEXANIKA.
///
/// ── SHAKLI ─────────────────────────────────────────────────────────────
///
///   <c>1.{userId}.{expiresUnixSeconds}.{base64url(imzo)}</c>
///
/// Barcha belgilar URL uchun XAVFSIZ (`0–9`, `.`, `_`, `-`, `A–Za-z`) —
/// query'ga foiz-kodlashsiz qo'yiladi. Bu MUHIM: `&lt;video src&gt;` manzilini
/// brauzer o'zi qayta-qayta ishlatadi (har `Range` so'rovida) va oradagi
/// noto'g'ri kodlash imzoni jimgina buzardi.
///
/// Boshidagi <c>1</c> — FORMAT versiyasi. Kelajakda imzo tarkibi
/// o'zgarsa, eski chiptalar (≤15 daqiqa) jimgina "yaroqsiz" bo'lib
/// qolmasin, ANIQ rad etilsin.
///
/// ── ★ NIMA UCHUN JWT EMAS ──────────────────────────────────────────────
///
///  1) 🔴 XAVFSIZLIK, ASOSIY SABAB. JWT bo'lsa uni `AddJwtBearer`
///     quvuri ham qabul qilardi (imzo va issuer/audience bir xil) —
///     ya'ni "faqat videoga" mo'ljallangan belgi ISTALGAN `[Authorize]`
///     endpointni ocholardi. Uni to'sish uchun `token_use` claim'ini
///     HAR YERDA tekshirish kerak bo'lardi va bitta unutilgan joy butun
///     API'ni ochib qo'yardi. JWT BO'LMAGAN format esa quvurga UMUMAN
///     kira olmaydi: himoya tuzilishda, ehtiyotkorlikda emas.
///
///  2) UZUNLIK. JWT ~300+ belgi; bu chipta ~60. Manzil brauzer tarixiga,
///     loglarga va `Referer` ga tushadi — qisqasi yaxshi.
///
/// ── ★ KALIT: `Jwt:Secret` QAYTA ISHLATILADI, LEKIN AJRATILGAN ──────────
///
/// Yangi sozlama qo'shilmadi: u deploy'da to'ldirilmay qolsa (yoki bo'sh
/// qiymat bilan) video jimgina ishlamay qolardi va sabab hech qayerda
/// ko'rinmasdi. `Jwt:Secret` esa `ValidateOnStart()` bilan MAJBURIY va
/// kamida 32 belgi (<see cref="JwtOptions.MinSecretLength"/>).
///
/// 🔴 SHU SABABLI MAQSAD AJRATISH (domain separation) SHART:
/// imzolanadigan matn <see cref="Purpose"/> bilan BOSHLANADI. Ya'ni bu
/// yerda yasalgan imzo boshqa hech qanday kontekstda (sessiya tokeni,
/// LiveKit) ma'noli qiymatga aylanmaydi va aksincha.
///
/// ── SINGLETON ──────────────────────────────────────────────────────────
///
/// Holat yo'q; <see cref="HMACSHA256"/> ni har chaqiruvda yasash o'rniga
/// STATIK metod (`HMACSHA256.HashData`) ishlatiladi — u thread-safe.
/// </summary>
public sealed class HmacMediaAccessTicketService(
    IOptions<JwtOptions> options, TimeProvider clock) : IMediaAccessTicketService
{
    /// <summary>
    /// Maqsad yorlig'i — imzolanadigan matnning BIRINCHI qatori.
    /// Kalit boshqa maqsadlarda ham ishlatilgani uchun MAJBURIY
    /// (izoh: sinf sarlavhasi).
    /// </summary>
    private const string Purpose = "zinnur:media-ticket:v1";

    /// <summary>Chipta formatining versiyasi (birinchi bo'lak).</summary>
    private const string Version = "1";

    /// <summary>Bo'laklar soni: versiya, foydalanuvchi, muddat, imzo.</summary>
    private const int PartCount = 4;

    private readonly byte[] _key =
        Encoding.UTF8.GetBytes(
            (options ?? throw new ArgumentNullException(nameof(options))).Value.Secret);

    public MediaAccessTicket Issue(long assetId, long userId)
    {
        // Sekundgacha aniqlik yetarli va u matnda QISQA — muddat imzoga
        // kirgani uchun uni "chiroyliroq" qilishning ma'nosi yo'q.
        var expiresAt = clock.GetUtcNow().Add(IMediaAccessTicketService.DefaultTtl);
        var expiresUnix = expiresAt.ToUnixTimeSeconds();

        var signature = Sign(assetId, userId, expiresUnix);

        var token = string.Create(
            CultureInfo.InvariantCulture,
            $"{Version}.{userId}.{expiresUnix}.{signature}");

        // ⚠️ `expiresAt` chiptadagi qiymatdan QAYTA hisoblanadi
        //    (`FromUnixTimeSeconds`), tashqaridagi `expiresAt` dan emas:
        //    aks holda klient sekund kasrlari tufayli imzo o'lgan paytdan
        //    KEYINGI vaqtni "hali yaroqli" deb bilardi.
        return new MediaAccessTicket(token, DateTimeOffset.FromUnixTimeSeconds(expiresUnix));
    }

    public long? TryResolveUserId(string? token, long assetId)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        // `Split` chegarasi bilan: ortiqcha nuqtali qiymat JIMGINA
        // kesilib, yaroqli bo'lib qolmasin.
        var parts = token.Split('.');

        if (parts.Length != PartCount) return null;

        if (!string.Equals(parts[0], Version, StringComparison.Ordinal)) return null;

        if (!long.TryParse(
                parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
            || userId <= 0)
        {
            return null;
        }

        if (!long.TryParse(
                parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresUnix))
        {
            return null;
        }

        // ---- MUDDAT ----
        //
        // ★ "Skew" (soat farqi) uchun kenglik ATAYLAB YO'Q: chiptani AYNI
        //   shu server yasagan va AYNI shu server tekshiradi, ya'ni
        //   taqqoslanadigan ikki soat BITTA.
        if (clock.GetUtcNow().ToUnixTimeSeconds() >= expiresUnix) return null;

        // ---- IMZO ----
        //
        // 🔴 SOLISHTIRISH DOIMIY VAQTDA (`CryptographicOperations.FixedTimeEquals`):
        //    oddiy satr solishtiruvi birinchi farqda to'xtaydi va javob
        //    vaqtidan imzoni bayt-bayt topib olish mumkin bo'lardi.
        //    `FixedTimeEquals` uzunligi teng bo'lmasa ham `false` beradi,
        //    ya'ni uzunlikni oldindan tekshirish shart emas.
        var expected = Encoding.ASCII.GetBytes(Sign(assetId, userId, expiresUnix));
        var actual = Encoding.ASCII.GetBytes(parts[3]);

        return CryptographicOperations.FixedTimeEquals(expected, actual) ? userId : null;
    }

    /// <summary>
    /// Imzo — <c>base64url</c> (`+/=` belgilarisiz).
    ///
    /// ⚠️ `assetId` imzoga KIRADI, lekin chiptaning O'ZIDA yo'q: u
    /// baribir manzil yo'lida turadi. Shu tufayli chipta qisqa qoladi va
    /// baribir FAQAT o'sha faylga yaraydi.
    /// </summary>
    private string Sign(long assetId, long userId, long expiresUnix)
    {
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"{Purpose}\n{assetId}\n{userId}\n{expiresUnix}");

        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(message));

        return Base64Url.EncodeToString(hash);
    }
}
