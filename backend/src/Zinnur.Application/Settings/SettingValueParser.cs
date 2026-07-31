using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Zinnur.Application.Settings;

/// <summary>
/// Sozlama qiymatini TEKSHIRADI va KANONIK ko'rinishga keltiradi.
///
/// ★ NIMA UCHUN "tekshirish" va "normallashtirish" BITTA metodda: ular
/// bir-biridan ajratilsa, tekshiruvdan o'tgan-u normallashtirilmagan qiymat
/// bazaga tushishi mumkin bo'lardi (masalan <c>"video"</c> — qabul qilinadi,
/// lekin jadvalda <c>"Video"</c> turishi kerak). Bitta metod bu holatni
/// FIZIK jihatdan imkonsiz qiladi.
///
/// ★ XATO MATNI O'ZBEKCHA va ANIQ: u to'g'ridan-to'g'ri 400 javobning
/// <c>errors</c> maydoniga tushadi va foydalanuvchiga ko'rsatiladi.
/// "Invalid value" kabi matn foydalanuvchiga hech nima bermaydi.
/// </summary>
public static class SettingValueParser
{
    /// <summary>
    /// Kiritilgan qiymatni tekshiradi va saqlanadigan ko'rinishga keltiradi.
    /// </summary>
    /// <returns><c>true</c> — <paramref name="normalized"/> saqlashga tayyor.</returns>
    public static bool TryNormalize(
        SettingDefinition definition,
        string? input,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(definition);

        normalized = null;
        error = null;

        // Bo'shliqlar ATAYLAB kesiladi: nusxa-joylashtirishda oxiriga tushib
        // qolgan probel yoki yangi qator eng ko'p uchraydigan "ko'rinmas"
        // xato — token oxiridagi bitta probel butun integratsiyani buzardi.
        var value = (input ?? string.Empty).Trim();

        if (value.Length > definition.MaxLength)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Qiymat juda uzun: {definition.MaxLength} belgidan oshmasin.");
            return false;
        }

        // Eng qisqa uzunlik FAQAT to'ldirilgan qiymatga tegishli: bo'sh qiymat
        // "o'rnatilmagan" degani va uni ruxsat etish-etmaslikni turning o'zi
        // hal qiladi (sir bo'shni umuman qabul qilmaydi, ixtiyoriy matn esa
        // qabul qiladi). Aks holda "bo'sh" xatosi ikki xil matnda ikki joyda
        // chiqardi va foydalanuvchi qaysi biri to'g'ri ekanini tushunmasdi.
        if (value.Length > 0 && value.Length < definition.MinLength)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Qiymat juda qisqa: kamida {definition.MinLength} belgi bo'lsin.");
            return false;
        }

        switch (definition.Kind)
        {
            case SettingValueKind.Toggle:
                return TryToggle(value, out normalized, out error);

            case SettingValueKind.Number:
                return TryNumber(definition, value, out normalized, out error);

            case SettingValueKind.Money:
                return TryMoney(definition, value, out normalized, out error);

            case SettingValueKind.Choice:
                return TryChoice(definition, value, out normalized, out error);

            case SettingValueKind.Secret:
                return TrySecret(definition, value, out normalized, out error);

            case SettingValueKind.Text:
            default:
                return TryText(definition, value, out normalized, out error);
        }
    }

    /// <summary>
    /// Saqlangan qiymatni O'QIYDI. Buzuq bo'lsa <c>false</c> — chaqiruvchi
    /// standartga qaytadi.
    ///
    /// ★ NIMA UCHUN O'QISHDA XATO TASHLANMAYDI: bu qator qo'lda ham
    /// tahrirlanishi mumkin (yoki eski tizimdan ko'chirilgan bo'lishi).
    /// "Chegara satri buzuq" degan holat BUTUN platformani ishdan
    /// chiqarmasligi kerak — xavfsiz yo'nalish standart qiymat.
    /// </summary>
    public static bool TryReadDecimal(SettingDefinition definition, string? stored, out decimal value)
    {
        ArgumentNullException.ThrowIfNull(definition);

        value = 0m;

        if (!decimal.TryParse(stored, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return false;

        if (definition.Minimum is { } min && parsed < min)
            return false;

        if (definition.Maximum is { } max && parsed > max)
            return false;

        value = parsed;
        return true;
    }

    /// <summary>Mantiqiy qiymatni o'qiydi; buzuq bo'lsa <c>false</c> qaytadi.</summary>
    public static bool TryReadBool(string? stored, out bool value) =>
        bool.TryParse(stored, out value);

    /// <summary>
    /// Enum qiymatini o'qiydi. Registrga sezgir EMAS — eski tizimdan
    /// ko'chirilgan <c>"video"</c> ham to'g'ri o'qilishi kerak.
    /// </summary>
    public static bool TryReadEnum<TEnum>(string? stored, out TEnum value)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse(stored, ignoreCase: true, out value) && Enum.IsDefined(value))
            return true;

        value = default;
        return false;
    }

    private static bool TryToggle(
        string value,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error)
    {
        normalized = null;

        if (!bool.TryParse(value, out var parsed))
        {
            error = "Faqat `true` yoki `false` qabul qilinadi.";
            return false;
        }

        // Kanonik ko'rinish — kichik harfda ("True" emas): JSON va JavaScript
        // shu shaklni kutadi, bazani qo'lda ko'rgan odam ham shunga o'rgangan.
        normalized = parsed ? "true" : "false";
        error = null;
        return true;
    }

    private static bool TryNumber(
        SettingDefinition definition,
        string value,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error)
    {
        normalized = null;

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            error = "Butun son kiriting (masalan `15`).";
            return false;
        }

        if (!InRange(definition, parsed, out error))
            return false;

        normalized = parsed.ToString(CultureInfo.InvariantCulture);
        error = null;
        return true;
    }

    private static bool TryMoney(
        SettingDefinition definition,
        string value,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error)
    {
        normalized = null;

        // ★ `NumberStyles.Number` bo'shliqli guruhlarni ham qabul qilardi
        // ("540 000") — bu qulay ko'rinsa-da, xavfli: bazaga bo'shliqli satr
        // tushib, keyingi o'qishda buzuq deb standartga qaytardi. Shuning
        // uchun faqat oddiy o'nlik shakl qabul qilinadi.
        const NumberStyles Styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;

        if (!decimal.TryParse(value, Styles, CultureInfo.InvariantCulture, out var parsed))
        {
            error = "Son kiriting. O'nlik ajratgich — nuqta (masalan `540000` yoki `540000.50`).";
            return false;
        }

        if (!InRange(definition, parsed, out error))
            return false;

        // Baza ustuni `numeric(18,2)` — ikkitadan ortiq kasr raqami jimgina
        // yaxlitlanardi va foydalanuvchi kiritgan qiymat "yo'qolardi".
        if (decimal.Round(parsed, 2) != parsed)
        {
            error = "Kasr qismi ikki xonadan oshmasin.";
            return false;
        }

        normalized = parsed.ToString(CultureInfo.InvariantCulture);
        error = null;
        return true;
    }

    private static bool TryChoice(
        SettingDefinition definition,
        string value,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error)
    {
        normalized = null;

        // Registrga sezgir emas, lekin SAQLANADIGANI — ro'yxatdagi kanonik
        // yozuv. Shunda bazada "video" va "Video" yonma-yon turmaydi.
        foreach (var choice in definition.Choices)
        {
            if (!string.Equals(choice, value, StringComparison.OrdinalIgnoreCase))
                continue;

            normalized = choice;
            error = null;
            return true;
        }

        error = "Ruxsat etilgan qiymatlar: " + string.Join(", ", definition.Choices) + ".";
        return false;
    }

    private static bool TrySecret(
        SettingDefinition definition,
        string value,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error)
    {
        normalized = null;

        // ★ BO'SH SIR RAD ETILADI. Sabab: maskalangan maydonda foydalanuvchi
        // "hozircha tegmayman" deb bo'sh qoldirsa, uni saqlash mavjud sirni
        // JIMGINA o'chirib yuborardi va integratsiya keyinroq, sababsiz
        // ishdan chiqardi. Sirni o'chirish uchun oshkor "standartga
        // qaytarish" amali bor.
        if (value.Length == 0)
        {
            error = "Sir bo'sh bo'lmasin. O'chirish uchun \"standart qiymatga qaytarish\" dan foydalaning.";
            return false;
        }

        // ★ SIR HAM FORMAT TEKSHIRUVIDAN O'TADI. Ilgari o'tmasdi va bunga
        // ehtiyoj ham yo'q edi — sirlar faqat muhitdan kelardi. Endi bot
        // tokeni PANELDAN yoziladi, ya'ni buzuq shakl aynan shu yerda
        // to'silishi kerak: aks holda xato Telegram tomonida, bizga
        // ko'rinmaydigan joyda chiqardi.
        if (!TryFormat(definition, value, out error))
            return false;

        normalized = value;
        error = null;
        return true;
    }

    private static bool TryText(
        SettingDefinition definition,
        string value,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error)
    {
        normalized = null;

        if (!TryFormat(definition, value, out error))
            return false;

        normalized = value;
        error = null;
        return true;
    }

    /// <summary>
    /// Format qoidasi — matn uchun ham, sir uchun ham BITTA joyda.
    ///
    /// ★ NIMA UCHUN UMUMIY: qoida ikki joyda nusxalansa, keyingi format
    /// qo'shilganda bittasiga qo'shilib, ikkinchisiga qo'shilmay qolardi —
    /// va farq faqat "nega bu maydon tekshirilmadi?" degan savol bilan
    /// bilinardi.
    /// </summary>
    private static bool TryFormat(
        SettingDefinition definition,
        string value,
        [NotNullWhen(false)] out string? error)
    {
        error = null;

        switch (definition.Format)
        {
            case SettingFormat.Url when value.Length > 0 && !IsSupportedUrl(value):
                error = "Manzil to'liq bo'lishi kerak: `http://`, `https://`, `ws://` yoki `wss://` bilan boshlansin.";
                return false;

            case SettingFormat.TimeZone when value.Length == 0:
                error = "Vaqt zonasi to'ldirilishi shart (masalan `Asia/Tashkent`).";
                return false;

            case SettingFormat.TelegramToken when value.Length > 0 && !IsTelegramToken(value):
                error = "Bot tokeni `123456789:AA...` ko'rinishida va bo'shliqsiz bo'lishi kerak.";
                return false;

            case SettingFormat.TelegramSecret when value.Length > 0 && !IsTelegramSecret(value):
                error = "Webhook siri faqat `A-Z a-z 0-9 _ -` belgilaridan iborat bo'lishi kerak (Telegram talabi).";
                return false;

            case SettingFormat.None:
            default:
                break;
        }

        return true;
    }

    /// <summary>
    /// Token shakli oqilonami. Tekshiruv ATAYLAB YUMSHOQ — Telegram formatni
    /// kelajakda o'zgartirsa, ishlab turgan tizim shu sababdan tokenni qabul
    /// qilmay qolmasin. Maqsad — "bo'sh joy qo'shib qo'yildi" va "yarim
    /// nusxalandi" turkumidagi xatoni tutish.
    /// <c>TelegramOptions.HasValidBotToken</c> bilan AYNI qoida.
    /// </summary>
    private static bool IsTelegramToken(string value) =>
        value.Length >= MinBotTokenLength
        && value.Contains(':', StringComparison.Ordinal)
        && !value.Any(char.IsWhiteSpace);

    /// <summary><c>TelegramOptions.HasValidWebhookSecret</c> bilan AYNI qoida.</summary>
    private static bool IsTelegramSecret(string value) =>
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_' || ch == '-');

    /// <summary>Eng qisqa oqilona token uzunligi (<c>12345:AA...</c>).</summary>
    private const int MinBotTokenLength = 20;

    /// <summary>
    /// Manzil ABSOLYUT va qo'llab-quvvatlanadigan sxemada ekanini tekshiradi.
    /// Nisbiy manzil (<c>/api</c>) yoki sxemasiz host (<c>livekit:7880</c>)
    /// rad etiladi — ular ish paytida "ulanib bo'lmadi" bo'lib chiqardi.
    /// </summary>
    private static bool IsSupportedUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme is "http" or "https" or "ws" or "wss";
    }

    private static bool InRange(
        SettingDefinition definition,
        decimal value,
        [NotNullWhen(false)] out string? error)
    {
        if (definition.Minimum is { } min && value < min)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Qiymat {min} dan kichik bo'lmasin.");
            return false;
        }

        if (definition.Maximum is { } max && value > max)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Qiymat {max} dan katta bo'lmasin.");
            return false;
        }

        error = null;
        return true;
    }
}
