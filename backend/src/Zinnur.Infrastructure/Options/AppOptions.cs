using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Zinnur.Infrastructure.Options;

/// <summary>
/// <c>App</c> bo'limi — ilovaning umumiy sozlamalari.
/// </summary>
public sealed class AppOptions
{
    /// <summary>appsettings / env dagi bo'lim nomi: <c>App__...</c>.</summary>
    public const string SectionName = "App";

    /// <summary>
    /// Standart zona. IANA identifikatori — Linux (konteyner) shu shaklni
    /// ishlatadi va Windows'da ham .NET 6+ dan boshlab tushunadi.
    /// </summary>
    public const string DefaultTimeZone = "Asia/Tashkent";

    /// <summary>
    /// Dars jadvali TUZILADIGAN vaqt zonasi.
    ///
    /// Guruhning <c>StartTime</c> qiymati (masalan 19:00) AYNAN shu zonaning
    /// devor-vaqti sifatida o'qiladi va aniq UTC instant'ga aylantiriladi.
    /// Konteyner UTC'da ishlaganidan bu qiymat MAJBURIY — `TimeZoneInfo.Local`
    /// ishlatilsa jadval besh soatga siljib ketardi.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "App:TimeZone to'ldirilishi shart.")]
    public string TimeZone { get; set; } = DefaultTimeZone;

    /// <summary>
    /// Zonani xavfsiz izlaydi.
    ///
    /// Alohida metod sifatida: bir joyda ishga tushishda TEKSHIRISH uchun
    /// (<c>ValidateOnStart</c>), boshqa joyda haqiqiy qiymatni olish uchun
    /// ishlatiladi — ikki xil mantiq bo'lib ketmasin.
    /// </summary>
    public static bool TryResolve(string? id, [NotNullWhen(true)] out TimeZoneInfo? timeZone)
    {
        timeZone = null;

        if (string.IsNullOrWhiteSpace(id)) return false;

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            // Konteynerda `tzdata` o'rnatilmagan yoki id xato yozilgan.
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            // Zona fayli buzilgan.
            return false;
        }
    }
}
