using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Zinnur.Application.Telegram;

/// <summary>
/// Telegram Mini App'ning <c>initData</c> satrini TEKSHIRADI (sof funksiya).
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN BU YERDA (Application), Infrastructure'da EMAS
///
/// Bu — bog'liqliksiz sof hisob: kiruvchi satr + bot tokeni + hozirgi vaqt
/// → qaror. Bazasi, HTTP'si, soati yo'q. Loyihada bunday qoidalar
/// (<c>LessonGate</c>, <c>PaymentAllocator</c>, <c>OutboxRetryPolicy</c>)
/// ATAYLAB Application/Domain'da turadi va unit testlarda mock'siz
/// sinaladi. Aynan shu sabab: imzo tekshiruvi — tizimning eng muhim
/// himoyasi va uni jonli Postgres ko'tarmasdan, o'nlab holatda sinash
/// mumkin bo'lishi SHART.
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★ QAYSI ALGORITM — CHALKASHTIRMASLIK SHART
///
/// Telegram'da IKKI xil imzo sxemasi bor va ular BIR-BIRIGA O'XSHAYDI:
///
///   A) Mini App `initData`  (BIZ SHUNI QILAMIZ):
///        secret_key = HMAC_SHA256(key: "WebAppData", data: bot_token)
///        hash       = HMAC_SHA256(key: secret_key,   data: data_check_string)
///
///   B) Telegram Login Widget (BIZ BUNI QILMAYMIZ):
///        secret_key = SHA256(bot_token)
///        hash       = HMAC_SHA256(key: secret_key, data: data_check_string)
///
/// Ikkalasini almashtirib qo'yish IKKI XIL falokat beradi: yo hech qanday
/// haqiqiy foydalanuvchi kira olmaydi, yoki — battari — noto'g'ri tekshiruv
/// hech narsani himoya qilmaydi. Shuning uchun sxema shu yerda yozib
/// qo'yilgan va test uni AYNAN shu shaklda qo'riqlaydi
/// (<c>TelegramInitDataTests</c> da B sxemasi bilan yasalgan imzo RAD
/// ETILISHI tekshiriladi).
///
/// Hujjat: https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public static class TelegramInitData
{
    /// <summary>HMAC kalitini yasashda ishlatiladigan qat'iy o'zgarmas satr.</summary>
    private const string WebAppDataKey = "WebAppData";

    /// <summary>Imzoni saqlaydigan maydon nomi — u <c>data_check_string</c> ga KIRMAYDI.</summary>
    private const string HashField = "hash";

    /// <summary>Foydalanuvchi ma'lumoti JSON ko'rinishida shu maydonda keladi.</summary>
    private const string UserField = "user";

    /// <summary>Imzo qachon yasalgani (Unix sekund).</summary>
    private const string AuthDateField = "auth_date";

    /// <summary>
    /// Kelajakka yo'l qo'yiladigan farq. Telefon soati serverdan bir necha
    /// daqiqa oldinda bo'lishi ODATIY hol; busiz qonuniy foydalanuvchilar
    /// "kelajakdan kelgan imzo" bahonasida rad etilardi.
    /// </summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// <c>initData</c> ni tekshiradi.
    ///
    /// ISTISNO TASHLAMAYDI — natija <see cref="TelegramInitDataResult"/> da
    /// qaytadi. Sabab: "imzo mos kelmadi" bu bug emas, KUTILGAN natija
    /// (eskirgan ilova, qalbaki so'rov). Istisno bo'lganda har bunday holat
    /// Sentry'ni uyg'otardi.
    /// </summary>
    /// <param name="initData">Telegram bergan xom (URL-kodlangan) satr.</param>
    /// <param name="botToken">Bot tokeni — HMAC kaliti manbai.</param>
    /// <param name="now">Hozirgi vaqt (<c>TimeProvider</c> dan).</param>
    /// <param name="maxAge">Imzoning eng katta yoshi (odatda 24 soat).</param>
    public static TelegramInitDataResult Verify(
        string? initData, string? botToken, DateTimeOffset now, TimeSpan maxAge)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            return TelegramInitDataResult.Fail("Bot tokeni sozlanmagan.");

        if (string.IsNullOrWhiteSpace(initData))
            return TelegramInitDataResult.Fail("initData bo'sh.");

        if (initData.Length > MaxInitDataLength)
            return TelegramInitDataResult.Fail("initData haddan tashqari uzun.");

        // ── 1) Juftliklarga ajratamiz ────────────────────────────────────
        //
        // ★ `Uri.UnescapeDataString`, `HttpUtility.ParseQueryString` EMAS.
        //   Ikkinchisi `+` belgisini BO'SHLIQQA aylantiradi (eski HTML
        //   form-urlencoded qoidasi). Telegram esa `initData` ni
        //   `encodeURIComponent` bilan yasaydi — u bo'shliqni `%20` qiladi
        //   va `+` ni `%2B` ga o'giradi, ya'ni satrdagi yalang'och `+`
        //   HAQIQIY `+` bo'ladi (masalan telefon raqamida). Uni bo'shliqqa
        //   aylantirsak imzo hech qachon to'g'ri kelmasdi.
        //
        //   Har qanday holatda xato TO'SIQ TOMONGA ketadi: dekodlash
        //   noto'g'ri bo'lsa `data_check_string` o'zgaradi va imzo RAD
        //   ETILADI. "Ochiq qolish" varianti yo'q.
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string? receivedHash = null;

        foreach (var pair in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0) continue;

            string name;
            string value;

            try
            {
                name = Uri.UnescapeDataString(pair[..separator]);
                value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
            catch (UriFormatException)
            {
                return TelegramInitDataResult.Fail("initData kodlash xatosi.");
            }

            if (string.Equals(name, HashField, StringComparison.Ordinal))
            {
                receivedHash = value;
                continue;   // ★ `hash` data_check_string ga QO'SHILMAYDI
            }

            // Takroriy kalit — qalbakilashtirishga urinish belgisi.
            if (!fields.TryAdd(name, value))
                return TelegramInitDataResult.Fail("initData da takroriy maydon bor.");
        }

        if (string.IsNullOrEmpty(receivedHash))
            return TelegramInitDataResult.Fail("initData imzosi (hash) yo'q.");

        if (fields.Count == 0)
            return TelegramInitDataResult.Fail("initData bo'sh (imzodan boshqa maydon yo'q).");

        // ── 2) data_check_string: alifbo tartibida `key=value`, `\n` bilan ──
        var builder = new StringBuilder(initData.Length);

        foreach (var (name, value) in fields)
        {
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(name).Append('=').Append(value);
        }

        // ── 3) Imzoni hisoblaymiz (sxema A — yuqoridagi izoh) ────────────
        var secretKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(WebAppDataKey),
            Encoding.UTF8.GetBytes(botToken));

        var expected = HMACSHA256.HashData(
            secretKey,
            Encoding.UTF8.GetBytes(builder.ToString()));

        // ── 4) DOIMIY VAQTDA solishtiramiz ───────────────────────────────
        //
        // ★ Oddiy `==` (satr tengligi) birinchi farq qilgan baytda TO'XTAYDI.
        //   Hujumchi javob vaqtini o'lchab, imzoni bayt-bayt topa oladi
        //   (timing attack). `FixedTimeEquals` esa har doim bir xil vaqt
        //   sarflaydi.
        if (!TryParseHex(receivedHash, expected.Length, out var received))
            return TelegramInitDataResult.Fail("initData imzosi noto'g'ri shaklda.");

        if (!CryptographicOperations.FixedTimeEquals(expected, received))
            return TelegramInitDataResult.Fail("initData imzosi mos kelmadi.");

        // ── 5) ESKIRGANMI ────────────────────────────────────────────────
        //
        // ★ IMZO TO'G'RI BO'LSA HAM YETARLI EMAS. Bir marta o'g'irlangan
        //   `initData` (masalan qurilma logidan yoki brauzer tarixidan)
        //   imzosi bilan birga ABADIY yaroqli qolardi — ya'ni akkaunt
        //   umrbod egallab olinardi. Muddat shu teshikni yopadi.
        if (!fields.TryGetValue(AuthDateField, out var authDateText)
            || !long.TryParse(authDateText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var authDateUnix))
        {
            return TelegramInitDataResult.Fail("initData da auth_date yo'q.");
        }

        DateTimeOffset authDate;

        try
        {
            authDate = DateTimeOffset.FromUnixTimeSeconds(authDateUnix);
        }
        catch (ArgumentOutOfRangeException)
        {
            return TelegramInitDataResult.Fail("initData da auth_date yaroqsiz.");
        }

        var age = now - authDate;

        if (age > maxAge)
            return TelegramInitDataResult.Fail("initData muddati o'tgan.");

        if (age < -ClockSkew)
            return TelegramInitDataResult.Fail("initData sanasi kelajakdan.");

        // ── 6) Foydalanuvchi ─────────────────────────────────────────────
        if (!fields.TryGetValue(UserField, out var userJson))
            return TelegramInitDataResult.Fail("initData da user yo'q.");

        TelegramInitDataUser? user;

        try
        {
            user = JsonSerializer.Deserialize<TelegramInitDataUser>(userJson, UserJsonOptions);
        }
        catch (JsonException)
        {
            return TelegramInitDataResult.Fail("initData dagi user JSON yaroqsiz.");
        }

        if (user is null || user.Id <= 0)
            return TelegramInitDataResult.Fail("initData da Telegram ID yo'q.");

        // Bot hech qachon o'quvchi bo'la olmaydi.
        if (user.IsBot)
            return TelegramInitDataResult.Fail("Bot hisobi bilan kirish mumkin emas.");

        return TelegramInitDataResult.Success(user, authDate);
    }

    /// <summary>
    /// <c>initData</c> uchun oqilona chegara. Bunsiz istalgan hajmdagi satr
    /// HMAC hisobiga tushardi va bu arzon DoS vositasi bo'lardi.
    /// Haqiqiy <c>initData</c> odatda 300-800 belgi.
    /// </summary>
    public const int MaxInitDataLength = 8192;

    /// <summary>
    /// <c>user</c> maydonidagi JSON Telegram qoidasi bo'yicha
    /// <c>snake_case</c> da (<c>first_name</c>, <c>is_bot</c>) —
    /// shuning uchun global sozlamaga TAYANMAYMIZ (sabab: DTO fayli izohi).
    /// </summary>
    private static readonly JsonSerializerOptions UserJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>
    /// Hex satrni baytlarga o'giradi. <c>Convert.FromHexString</c> istisno
    /// tashlaydi, bu yerda esa noto'g'ri kiritish ODATIY hol — istisno
    /// oqimni boshqarish vositasi bo'lmasligi kerak.
    /// </summary>
    private static bool TryParseHex(string value, int expectedLength, out byte[] bytes)
    {
        bytes = [];

        if (value.Length != expectedLength * 2) return false;

        var buffer = new byte[expectedLength];

        for (var i = 0; i < expectedLength; i++)
        {
            if (!TryHexDigit(value[i * 2], out var high) || !TryHexDigit(value[(i * 2) + 1], out var low))
                return false;

            buffer[i] = (byte)((high << 4) | low);
        }

        bytes = buffer;
        return true;
    }

    private static bool TryHexDigit(char ch, out int value)
    {
        value = ch switch
        {
            >= '0' and <= '9' => ch - '0',
            >= 'a' and <= 'f' => ch - 'a' + 10,
            >= 'A' and <= 'F' => ch - 'A' + 10,
            _ => -1,
        };

        return value >= 0;
    }
}

/// <summary>
/// <c>initData</c> ichidan chiqqan foydalanuvchi.
/// FAQAT imzo tasdiqlangandan keyin ishonch bildiriladi.
/// </summary>
public sealed record TelegramInitDataUser
{
    public long Id { get; init; }

    public bool IsBot { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Username { get; init; }
}

/// <summary>
/// Tekshiruv natijasi.
///
/// ★ SABAB (<see cref="Reason"/>) FAQAT SERVER TOMONI UCHUN: u logga
/// yoziladi, foydalanuvchiga esa YAGONA umumiy xabar ko'rsatiladi.
/// "Imzo mos kelmadi" va "muddati o'tgan" ni ajratib ko'rsatish hujumchiga
/// nima to'g'ri, nima noto'g'ri ekanini aytib berardi.
/// </summary>
public sealed record TelegramInitDataResult
{
    private TelegramInitDataResult(
        bool isValid, TelegramInitDataUser? user, DateTimeOffset? authDate, string? reason)
    {
        IsValid = isValid;
        User = user;
        AuthDate = authDate;
        Reason = reason;
    }

    public bool IsValid { get; }

    public TelegramInitDataUser? User { get; }

    public DateTimeOffset? AuthDate { get; }

    /// <summary>Rad etish sababi (server logi uchun).</summary>
    public string? Reason { get; }

    /// <summary>Tasdiqlangan Telegram ID (faqat <see cref="IsValid"/> da ma'noli).</summary>
    public long TelegramUserId => User?.Id ?? 0;

    internal static TelegramInitDataResult Success(TelegramInitDataUser user, DateTimeOffset authDate) =>
        new(isValid: true, user, authDate, reason: null);

    internal static TelegramInitDataResult Fail(string reason) =>
        new(isValid: false, user: null, authDate: null, reason);
}
