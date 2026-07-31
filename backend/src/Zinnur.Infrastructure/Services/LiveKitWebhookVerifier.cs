using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zinnur.Application.Recordings.Services;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 <see cref="ILiveKitWebhookVerifier"/> — IMZO VA TANA XESHI
/// ════════════════════════════════════════════════════════════════════════
///
/// Nima uchun bu tekshiruv umuman borligi va eski tizimda nima bo'lgani —
/// port izohida. Bu yerda FAQAT "qanday" savoliga javob.
///
/// ── TEKSHIRUV TARTIBI (hammasi MAJBURIY) ────────────────────────────────
///
///   1) sir sozlanganmi          -> yo'q bo'lsa chaqiruvchi 404 qiladi;
///   2) token uch bo'lakmi       -> JWT shakli;
///   3) <c>alg</c> = HS256       -> 🔴 `none` va RS256 QAT'IY rad etiladi;
///   4) IMZO                     -> HMAC-SHA256, `FixedTimeEquals`;
///   5) <c>iss</c> = ApiKey      -> boshqa loyihaning LiveKit'i emasmi;
///   6) <c>exp</c>/<c>nbf</c>    -> eski token qayta ishlatilmasin;
///   7) 🔴 <c>sha256</c> = TANA  -> `FixedTimeEquals`.
///
/// ── (3) NIMA UCHUN ALOHIDA AYTILADI ─────────────────────────────────────
///
/// Algoritm tokenning O'ZIDAN olinsa, hujumchi <c>{"alg":"none"}</c> yozib
/// imzoni umuman tashlab yuborardi — bu JWT'dagi eng mashhur zaiflik.
/// Shuning uchun bu yerda algoritm KUTILADI, o'qilmaydi: sarlavhadagi
/// qiymat faqat "kutilganiga mos keladimi" deb solishtiriladi.
///
/// ── (7) NIMA UCHUN (4) YETARLI EMAS ─────────────────────────────────────
///
/// Yaroqli token bir marta ushlansa (xato sozlangan proksi jurnalidan,
/// TLS'ni ochib ko'radigan korporativ vositadan), uni BOSHQA tana bilan
/// qayta yuborish mumkin bo'lardi: imzo to'g'ri, lekin u AYNI SHU MAZMUNGA
/// taalluqli emas. Tana xeshi tokenning ichida bo'lgani uchun uni
/// hujumchi o'zgartira olmaydi — imzo buziladi.
///
/// ── NIMA UCHUN JWT KUTUBXONASI ISHLATILMADI ─────────────────────────────
///
/// <c>JwtSecurityTokenHandler</c> o'zining <c>ClockSkew</c>, claim
/// XARITALASH va "issuer signing key resolver" siyosatlari bilan keladi va
/// ularning har biri jimgina yumshoq bo'lishi mumkin (masalan standart 5
/// daqiqalik skew). Bu yerda tekshiruvning HAR bandi ko'rinib turishi
/// kerak — <c>LiveKitTokenService</c> tokenni AYNAN shunday, qo'lda
/// yasagani bilan bir xil uslub.
///
/// ★ KALIT VA SIR HAR SO'ROVDA QAYTA O'QILADI
/// (<see cref="IRuntimeOptions{TOptions}"/>): ular AYLANTIRILADIGAN
/// ma'lumot va paneldan almashtirilishi mumkin. Kesim BIR MARTA olinadi —
/// <c>iss</c> ni bir juftlikdan, imzoni boshqasidan tekshirish mumkin emas.
/// </summary>
public sealed class LiveKitWebhookVerifier(
    IRuntimeOptions<LiveKitOptions> options, TimeProvider clock) : ILiveKitWebhookVerifier
{
    /// <summary>
    /// Soat farqiga yon berish.
    ///
    /// ★ 2 DAQIQA — ATAYLAB KICHIK (standart JWT kutubxonalarida 5). LiveKit
    /// ayni serverda turadi va tokenning umri baribir qisqa; katta oyna esa
    /// ushlangan tokenning yaroqlilik muddatini bekordan uzaytirardi.
    /// </summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    /// <summary>{"alg":"HS256","typ":"JWT"} — YAGONA qabul qilinadigan algoritm.</summary>
    private const string ExpectedAlgorithm = "HS256";

    /// <inheritdoc />
    public bool IsConfigured
    {
        get
        {
            var settings = options.Current;

            return !string.IsNullOrWhiteSpace(settings.ApiKey)
                && !string.IsNullOrWhiteSpace(settings.ApiSecret);
        }
    }

    /// <inheritdoc />
    public WebhookVerification Verify(string? authorizationHeader, ReadOnlySpan<byte> body)
    {
        // AMALNING BOSHIDA BIR MARTA: `iss` tekshiruvi va imzo AYNI
        // juftlikdan bo'lishi shart (izoh sinf tepasida).
        var settings = options.Current;

        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.ApiSecret))
            return WebhookVerification.Invalid("LiveKit kaliti sozlanmagan.");

        var token = StripBearer(authorizationHeader);

        if (string.IsNullOrEmpty(token))
            return WebhookVerification.Invalid("Authorization sarlavhasi yo'q.");

        // Tokenni bo'laklarga ajratamiz. `Split` o'rniga indeks: ortiqcha
        // massiv ajratilmasin va uchtadan KO'P nuqtali qiymat ham darhol
        // rad etilsin.
        var firstDot = token.IndexOf('.', StringComparison.Ordinal);

        if (firstDot <= 0)
            return WebhookVerification.Invalid("Token shakli JWT emas.");

        var secondDot = token.IndexOf('.', firstDot + 1);

        if (secondDot <= firstDot + 1 || secondDot == token.Length - 1)
            return WebhookVerification.Invalid("Token shakli JWT emas.");

        if (token.IndexOf('.', secondDot + 1) >= 0)
            return WebhookVerification.Invalid("Token shakli JWT emas.");

        var signingInput = token[..secondDot];
        var signature = token[(secondDot + 1)..];

        // `AsSpan` — ortiqcha satr nusxasi yasalmasin (CA1831).
        if (!TryDecodeBase64Url(token.AsSpan(0, firstDot), out var headerBytes)
            || !TryDecodeBase64Url(
                token.AsSpan(firstDot + 1, secondDot - firstDot - 1), out var payloadBytes)
            || !TryDecodeBase64Url(signature, out var signatureBytes))
        {
            return WebhookVerification.Invalid("Token base64url emas.");
        }

        // ---- (3) ALGORITM ---------------------------------------------
        if (!HasExpectedAlgorithm(headerBytes))
            return WebhookVerification.Invalid("Token algoritmi HS256 emas.");

        // ---- (4) IMZO --------------------------------------------------
        Span<byte> expected = stackalloc byte[HMACSHA256.HashSizeInBytes];

        HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(settings.ApiSecret),
            Encoding.UTF8.GetBytes(signingInput),
            expected);

        // ⚠️ `FixedTimeEquals` uzunliklar mos kelmasa DARHOL `false`
        // qaytaradi. Bu yerda bu xavfsiz: imzo uzunligi (32 bayt) OLDINDAN
        // ma'lum, ya'ni hech qanday sir sizib chiqmaydi.
        if (!CryptographicOperations.FixedTimeEquals(expected, signatureBytes))
            return WebhookVerification.Invalid("Imzo mos kelmadi.");

        // ---- (5)(6)(7) DA'VOLAR ----------------------------------------
        return VerifyClaims(payloadBytes, body, settings.ApiKey);
    }

    /// <summary>
    /// <c>iss</c>, <c>exp</c>/<c>nbf</c> va 🔴 TANA XESHI.
    ///
    /// Imzo ALLAQACHON tekshirilgan, ya'ni bu yerda o'qilayotgan JSON
    /// ishonchli manbadan. Shunga qaramay har maydon TURI bilan tekshiriladi:
    /// LiveKit versiyasi o'zgarsa istisno emas, tushunarli rad javobi
    /// chiqsin.
    /// </summary>
    private WebhookVerification VerifyClaims(
        byte[] payloadBytes, ReadOnlySpan<byte> body, string apiKey)
    {
        JsonDocument payload;

        try
        {
            payload = JsonDocument.Parse(payloadBytes);
        }
        catch (JsonException)
        {
            return WebhookVerification.Invalid("Token tarkibi JSON emas.");
        }

        using (payload)
        {
            var root = payload.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return WebhookVerification.Invalid("Token tarkibi obyekt emas.");

            // ---- (5) KIM IMZOLAGAN ------------------------------------
            //
            // Sir to'g'ri bo'lsa `iss` baribir to'g'ri chiqadi, lekin bu
            // tekshiruv ARZON va bitta amaliy holatni ushlaydi: dev va
            // staging BITTA LiveKit'ni baham ko'rganda kalit nomi bo'yicha
            // ajratish mumkin bo'ladi.
            if (root.TryGetProperty("iss", out var issuer)
                && (issuer.ValueKind != JsonValueKind.String
                    || !string.Equals(issuer.GetString(), apiKey, StringComparison.Ordinal)))
            {
                return WebhookVerification.Invalid("Token boshqa API kaliti bilan berilgan.");
            }

            var now = clock.GetUtcNow();

            // ---- (6) MUDDAT -------------------------------------------
            //
            // `exp` YO'Q bo'lsa ham rad etilmaydi: LiveKit versiyalarining
            // bir qismi uni umuman qo'ymaydi va bu bizning nazoratimizdan
            // tashqarida. Himoyaning ASOSI — imzo va tana xeshi; muddat
            // faqat qo'shimcha qatlam.
            if (UnixSeconds(root, "exp") is { } expiresAt && now > expiresAt + ClockSkew)
                return WebhookVerification.Invalid("Token muddati o'tgan.");

            if (UnixSeconds(root, "nbf") is { } notBefore && now + ClockSkew < notBefore)
                return WebhookVerification.Invalid("Token hali kuchga kirmagan.");

            // ---- (7) 🔴 TANA XESHI -------------------------------------
            //
            // Da'vo BO'LISHI SHART. "Yo'q bo'lsa o'tkazib yuboramiz" degan
            // yumshatish eski tizimning aynan shu joydagi teshigi edi
            // (`want = claims.get("sha256")` -> `if want:`): xesh
            // maydonisiz token bilan ISTALGAN tana o'tib ketardi.
            if (!root.TryGetProperty("sha256", out var claim) || claim.ValueKind != JsonValueKind.String)
                return WebhookVerification.Invalid("Tokenda tana xeshi yo'q.");

            if (!TryDecodeBase64(claim.GetString(), out var claimed))
                return WebhookVerification.Invalid("Tana xeshi base64 emas.");

            Span<byte> actual = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(body, actual);

            return CryptographicOperations.FixedTimeEquals(actual, claimed)
                ? WebhookVerification.Valid
                : WebhookVerification.Invalid("Tana xeshi mos kelmadi.");
        }
    }

    // ================================================================= yordamchilar

    /// <summary>
    /// <c>Bearer </c> prefiksini olib tashlaydi.
    ///
    /// ⚠️ LiveKit versiyalari IKKI XIL yuboradi: eskilari xom tokenni,
    /// yangilari <c>Bearer</c> bilan. Ikkalasi ham qabul qilinadi — aks
    /// holda server yangilangan kuni HAMMA webhook jimgina rad etilardi.
    /// </summary>
    private static string StripBearer(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return string.Empty;

        var value = header.Trim();

        const string Prefix = "Bearer ";

        return value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? value[Prefix.Length..].Trim()
            : value;
    }

    private static bool HasExpectedAlgorithm(byte[] headerBytes)
    {
        try
        {
            using var header = JsonDocument.Parse(headerBytes);

            return header.RootElement.ValueKind == JsonValueKind.Object
                && header.RootElement.TryGetProperty("alg", out var alg)
                && alg.ValueKind == JsonValueKind.String
                && string.Equals(alg.GetString(), ExpectedAlgorithm, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// UNIX sekund (<c>exp</c>, <c>nbf</c>). protojson bularni son qilib
    /// yozadi, ba'zi klientlar esa satr — ikkalasi ham qabul qilinadi.
    /// </summary>
    private static DateTimeOffset? UnixSeconds(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return null;

        var seconds = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(
                value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => (long?)null,
        };

        // Chegara: `FromUnixTimeSeconds` juda katta qiymatda istisno
        // tashlaydi va u tekshiruv o'rtasida 500 bo'lib chiqardi.
        if (seconds is not { } value2 || value2 is < 0 or > 253_402_300_799L)
            return null;

        return DateTimeOffset.FromUnixTimeSeconds(value2);
    }

    private static bool TryDecodeBase64Url(ReadOnlySpan<char> value, out byte[] bytes)
    {
        try
        {
            bytes = Base64Url.DecodeFromChars(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    /// <summary>
    /// Tana xeshi ODDIY base64'da keladi (base64url EMAS) — LiveKit uni
    /// Go'dagi <c>base64.StdEncoding</c> bilan yozadi.
    /// </summary>
    private static bool TryDecodeBase64(string? value, out byte[] bytes)
    {
        if (string.IsNullOrEmpty(value))
        {
            bytes = [];
            return false;
        }

        bytes = new byte[((value.Length + 3) / 4) * 3];

        if (Convert.TryFromBase64String(value, bytes, out var written))
        {
            Array.Resize(ref bytes, written);
            return true;
        }

        bytes = [];
        return false;
    }
}
