using System.Text;
using System.Text.Json;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// XATO JAVOBIDAN O'QILADIGAN MATN AJRATIB OLADI
/// ========================================================================
///
/// ★ NIMA UCHUN KERAK BO'LDI (haqiqiy tuzoq):
/// `JsonSerializer` standart holatda apostrofni `'` ga o'giradi
/// (XSS'ga qarshi qat'iy kodlash). Loyihaning xato xabarlari esa
/// O'ZBEKCHA va apostrof ular uchun oddiy harf: "qo'llab-quvvatlanmaydi",
/// "to'liq emas", "o'chiring".
///
/// Natijada XOM javob matni ustidagi
/// <c>body.Should().Contain("to'liq emas")</c> tekshiruvi HAR DOIM
/// yiqiladi — endpoint MUTLAQO to'g'ri ishlayotgan bo'lsa ham. Bu esa
/// eng yomon holatga olib keladi: dasturchi tasdiqni "ishlamadi" deb
/// o'chirib tashlaydi va tekshiruv butunlay yo'qoladi.
///
/// Bu yordamchi JSON'ni TAHLIL QILADI, ya'ni satrlar dekodlangan holda
/// qaytadi va apostrofli o'zbekcha matnni bemalol tekshirish mumkin.
///
/// ⚠️ MAVJUD TESTLAR TEGILMADI: ular apostrofsiz bo'lak tanlash bilan
/// chetlab o'tgan ("tegishli emas", "Storage:"). Bu ishlaydi, lekin
/// o'qilishi qiyin va yangi xabar yozganda tuzoq qaytadi.
/// </summary>
internal static class ProblemText
{
    /// <summary>
    /// Javob tanasidagi BARCHA satr qiymatlarini (rekursiv) bitta matnga
    /// qo'shadi: `title`, `detail` va `errors` ichidagi hamma xabar.
    ///
    /// Rekursiv: `errors` — lug'at, uning qiymatlari esa massiv. Faqat
    /// yuqori darajani o'qish `problem.errors["file"][0]` dagi haqiqiy
    /// sababni o'tkazib yuborardi.
    /// </summary>
    internal static async Task<string> ReadAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var json = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(json)) return string.Empty;

        var builder = new StringBuilder(json.Length);

        try
        {
            using var document = JsonDocument.Parse(json);

            Collect(document.RootElement, builder);
        }
        catch (JsonException)
        {
            // JSON emas (masalan bo'sh 413 javobi yoki HTML xato sahifasi) —
            // XOM matnni qaytaramiz, aks holda test sababsiz yiqilardi.
            return json;
        }

        return builder.ToString();
    }

    private static void Collect(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                builder.Append(element.GetString()).Append('\n');
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    // Maydon NOMI ham qo'shiladi: testlar "xato AYNAN
                    // `allowedFormats` ostida ko'rinsin" degan shartni ham
                    // tekshiradi.
                    builder.Append(property.Name).Append('\n');

                    Collect(property.Value, builder);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, builder);

                break;

            default:
                // Son, bool, null — xato MATNIDA qatnashmaydi.
                break;
        }
    }
}
