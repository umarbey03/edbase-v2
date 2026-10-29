using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Zinnur.Application.Interactive.Content;

/// <summary>
/// Darslik uchun server imzosi (HMAC) — 2026-10-29.
///
/// ★ PORT, chunki kalit Infrastructure'da (JWT siri) turadi, imzolanadigan
///   narsalar esa (audio belgisi, `ula` belgisi, audio chiptasi) Application
///   mantiqi. Unit testda qat'iy kalitli soxta imzo qo'yiladi.
/// </summary>
public interface IInteractiveSigner
{
    /// <summary>HMAC-SHA256(<paramref name="purpose"/> + "\n" + <paramref name="message"/>).</summary>
    byte[] Sign(string purpose, string message);
}

/// <summary>
/// OPAQUE AUDIO BELGISI (handle) — javob oqib ketmasligi uchun.
///
/// 🔴 MUAMMO (rejaning 0.1 va 3.4-bandi): `tovush` blokida ekrandagi so'zning
///   audiosi <c>"s02"</c>, eshitib-tanlash savolining audiosi ham <c>"s02"</c>.
///   Id ochiq yuborilsa, savolning javobini id dan topish mumkin edi.
///
/// ★ YECHIM: o'quvchiga id emas, <c>HMAC(versiya | id | kontekst)</c> dan
///   olingan qisqa belgi yuboriladi. Kontekst — <see cref="Display"/> (ko'rish)
///   yoki <see cref="Quiz"/> (tovush testi, mini-test tinglash savoli, diktant).
///   Bir xil fayl ikki kontekstda BOSHQA-BOSHQA belgiga ega.
///
/// ⚠️ QOLDIQ XAVF (qabul qilingan): ikki faylni bayt bo'yicha solishtirish —
///   amalda "eshitib solishtirish" bilan teng.
/// </summary>
public sealed class AudioHandleCodec(IInteractiveSigner signer)
{
    public const string Display = "d";
    public const string Quiz = "q";

    private const string Purpose = "zinnur:iaudio:v1";
    private const int HandleLength = 16;

    public string Handle(long versionId, string audioId, string context)
    {
        ArgumentNullException.ThrowIfNull(audioId);
        ArgumentNullException.ThrowIfNull(context);

        var mac = signer.Sign(
            Purpose,
            string.Create(CultureInfo.InvariantCulture, $"{versionId}|{audioId}|{context}"));

        return Base64Url.EncodeToString(mac)[..HandleLength];
    }

    /// <summary>
    /// Belgini audio id ga qaytaradi: versiyadagi barcha id × kontekst juftligi
    /// uchun belgi hisoblanadi (bir birlikda ~60–130 fayl — arzon).
    /// </summary>
    public string? Resolve(long versionId, IEnumerable<string> audioIds, string handle)
    {
        ArgumentNullException.ThrowIfNull(audioIds);

        if (string.IsNullOrEmpty(handle) || handle.Length != HandleLength) return null;

        foreach (var id in audioIds)
        {
            if (Same(Handle(versionId, id, Display), handle) || Same(Handle(versionId, id, Quiz), handle))
                return id;
        }

        return null;
    }

    private static bool Same(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}

/// <summary>
/// `ula` mashqidagi chap/o'ng element belgisi — juftlikni oshkor qilmasin.
///
/// ★ O'ng ustun belgilar bo'yicha saralanadi: tartib versiya ichida barqaror,
///   lekin manbadagi tartibdan (u kalitning o'zi) kelib chiqmaydi.
/// </summary>
public sealed class MatchTokenCodec(IInteractiveSigner signer)
{
    private const string Purpose = "zinnur:iula:v1";

    public string Token(long versionId, string itemKey, char side, int index)
    {
        var mac = signer.Sign(
            Purpose,
            string.Create(CultureInfo.InvariantCulture, $"{versionId}|{itemKey}|{side}|{index}"));

        return side + Base64Url.EncodeToString(mac)[..10];
    }
}

/// <summary>
/// AUDIO CHIPTASI — `&lt;audio src&gt;` `Authorization` sarlavhasini yubora
/// olmaydi (dars videosidagi `IMediaAccessTicketService` bilan AYNI muammo).
///
/// ★ FARQI: chipta BIRLIKKA bog'lanadi (<c>iunit:{unitId}</c>), bitta faylga
///   emas — 60 ta audio uchun 60 ta chipta so'rovi bo'lmasin.
/// 🔴 Chipta faqat "KIM" degan savolga javob beradi. "RUXSATMI" har audio
///   so'rovida qaytadan tekshiriladi (`InteractiveUnitService.OpenAudioAsync`).
/// </summary>
public sealed class UnitTicketCodec(IInteractiveSigner signer, TimeProvider clock)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private const string Purpose = "zinnur:iunit-ticket:v1";

    public (string Token, DateTimeOffset ExpiresAt) Issue(long unitId, long userId)
    {
        var expires = clock.GetUtcNow().Add(Ttl).ToUnixTimeSeconds();
        var token = string.Create(
            CultureInfo.InvariantCulture, $"1.{userId}.{expires}.{Signature(unitId, userId, expires)}");

        return (token, DateTimeOffset.FromUnixTimeSeconds(expires));
    }

    public long? TryResolveUserId(string? token, long unitId)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var parts = token.Split('.');
        if (parts.Length != 4 || !string.Equals(parts[0], "1", StringComparison.Ordinal)) return null;

        if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
            || userId <= 0)
        {
            return null;
        }

        if (!long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
            return null;

        if (clock.GetUtcNow().ToUnixTimeSeconds() >= expires) return null;

        var expected = Encoding.ASCII.GetBytes(Signature(unitId, userId, expires));
        var actual = Encoding.ASCII.GetBytes(parts[3]);

        return CryptographicOperations.FixedTimeEquals(expected, actual) ? userId : null;
    }

    private string Signature(long unitId, long userId, long expires) =>
        Base64Url.EncodeToString(signer.Sign(
            Purpose,
            string.Create(CultureInfo.InvariantCulture, $"iunit:{unitId}\n{userId}\n{expires}")));
}
