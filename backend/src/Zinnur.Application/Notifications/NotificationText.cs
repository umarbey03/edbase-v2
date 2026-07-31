using System.Text;

namespace Zinnur.Application.Notifications;

/// <summary>
/// Xabar matnini kanal talab qilgan ko'rinishga keltiradi.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ HTML ESCAPE QAYERDA BAJARILADI — QAROR (2026-07-30)
///
/// Telegram <c>parse_mode=HTML</c> bilan yuborilgan xabarda tekshirilmagan
/// <c>&lt;</c> belgisi butun so'rovni yiqitadi: API <c>400 Bad Request:
/// can't parse entities</c> qaytaradi va xabar UMUMAN yetib bormaydi.
/// O'quvchi ismida <c>&lt;</c> bo'lishi kamdan-kam, lekin vazifa sarlavhasi
/// yoki ustoz yozgan izohda bemalol uchraydi.
///
/// Escape MATN YASALAYOTGAN paytda, HAR PARAMETRGA ALOHIDA qo'llanadi —
/// yuboruvchi (sender) ichida BUTUN matnga emas.
///
/// SABAB: shablonning O'ZIDA belgilash bo'ladi (<c>&lt;b&gt;Dars&lt;/b&gt;</c>).
/// Agar escape yuborish paytida butun matnga qo'llansa, shablonning
/// teglari ham "matn"ga aylanib, foydalanuvchi ekranida <c>&lt;b&gt;</c>
/// so'zma-so'z ko'rinardi. Qaysi qism BELGILASH va qaysi qism MA'LUMOT
/// ekanini faqat matn yasalayotgan joy biladi.
///
/// Shuning uchun port kelishuvi (<c>IMessageSender</c>) qat'iy:
/// <c>Body</c> — YUBORISHGA TAYYOR matn, sender uni QAYTA ISHLAMAYDI.
/// FAZA 5.1 dagi Telegram implementatsiyasi uni o'zgartirmasdan yuboradi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public static class NotificationText
{
    /// <summary>
    /// Telegram <c>sendMessage</c> ning matn chegarasi.
    /// Bazadagi ustun ham AYNAN shuncha — undan uzun matn navbatga umuman
    /// TUSHMAYDI. Bu yerda kesish YO'Q, RAD ETISH bor (<c>INotificationOutbox</c>):
    /// tayyor matnni oxiridan kesish ochiq qolgan <c>&lt;b&gt;</c> tegini
    /// qoldirib, xabarni Telegram uchun umuman yaroqsiz qilardi. Parametr
    /// esa kesiladi — <see cref="Parameter"/>.
    /// </summary>
    public const int MaxBodyLength = 4096;

    /// <summary>Bitta parametr uchun oqilona chegara (ism, sarlavha, guruh nomi).</summary>
    public const int DefaultParameterLength = 200;

    /// <summary>
    /// Foydalanuvchi ma'lumotini shablonga qo'yishga tayyorlaydi:
    /// bo'shliqni kesadi, uzunini xavfsiz qirqadi va HTML uchun ekranlaydi.
    ///
    /// ★ UCHALASI BITTA METODDA — ataylab. Alohida bo'lganda kimdir
    /// escape'ni chaqirib, qirqishni unutardi (yoki aksincha), va xato
    /// faqat aynan uzun/belgili qiymatda — ya'ni produksiyada — chiqardi.
    /// </summary>
    public static string Parameter(string? value, int maxLength = DefaultParameterLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        return Escape(Truncate((value ?? string.Empty).Trim(), maxLength));
    }

    /// <summary>
    /// Telegram HTML uchun ekranlash: <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>.
    ///
    /// ★ NIMA UCHUN <c>WebUtility.HtmlEncode</c> EMAS: u ASCII'dan tashqari
    /// belgilarni ham raqamli entity'ga o'giradi. O'zbekcha/arabcha matnda
    /// bu har harfni <c>&amp;#1234;</c> ga aylantirib, 4096 belgilik
    /// chegarani bir necha barobar tezroq tugatardi va bazadagi matnni
    /// o'qib bo'lmaydigan holga keltirardi. Telegram hujjati esa AYNAN shu
    /// uchta belgini talab qiladi.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // Tez yo'l: ekranlanadigan belgi yo'q bo'lsa — yangi satr yasalmaydi
        // (xabarlarning katta qismi shu holatda).
        if (value.AsSpan().IndexOfAny('&', '<', '>') < 0) return value;

        var builder = new StringBuilder(value.Length + 16);

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                default: builder.Append(ch); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Matnni <paramref name="maxLength"/> gacha XAVFSIZ qirqadi —
    /// surrogat juftlik (emoji) o'rtasidan kesilmaydi.
    ///
    /// Sabab <see cref="Zinnur.Domain.Common.MessageText"/> da batafsil
    /// yozilgan: yolg'iz surrogat Postgres'ga yozilganda buziladi. Bu yerda
    /// alohida nusxa bor, chunki u yerdagi metod BO'SH matnni istisno bilan
    /// rad etadi — parametr esa bo'sh bo'lishi mumkin (masalan izoh yo'q).
    /// </summary>
    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;

        var cut = maxLength;
        if (char.IsHighSurrogate(value[cut - 1])) cut--;

        return value[..cut];
    }
}
