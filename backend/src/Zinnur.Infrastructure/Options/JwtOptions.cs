using System.ComponentModel.DataAnnotations;

namespace Zinnur.Infrastructure.Options;

/// <summary>
/// <c>Jwt</c> bo'limidan bog'lanadigan sozlamalar (SPEC 8-bo'lim).
///
/// NIMA UCHUN STRONGLY TYPED: eski tizimda kalitlar
/// <c>configuration["Jwt:Secret"]</c> ko'rinishida joy-joyda o'qilardi va
/// nom xato yozilganda dastur JIM ishlab ketardi — token bo'sh kalit bilan
/// imzolanardi. Bu yerda esa <c>ValidateOnStart()</c> tufayli konteyner
/// umuman ko'tarilmaydi.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>appsettings / env dagi bo'lim nomi: <c>Jwt__...</c>.</summary>
    public const string SectionName = "Jwt";

    /// <summary>HS256 uchun kalitning MINIMAL uzunligi (bayt).</summary>
    /// <remarks>
    /// HMAC-SHA256 blok kaliti 256 bit. Undan qisqa kalit brute-force uchun
    /// ochiq qoladi va <c>SymmetricSecurityKey</c> ham xato beradi.
    /// </remarks>
    public const int MinSecretLength = 32;

    [Required(AllowEmptyStrings = false, ErrorMessage = "Jwt:Issuer to'ldirilishi shart.")]
    public string Issuer { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "Jwt:Audience to'ldirilishi shart.")]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Imzo kaliti. Kodda EMAS — faqat konfiguratsiya/env orqali (SPEC 9.8).</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Jwt:Secret to'ldirilishi shart.")]
    [MinLength(MinSecretLength, ErrorMessage = "Jwt:Secret kamida 32 belgi bo'lishi shart.")]
    public string Secret { get; set; } = string.Empty;

    /// <summary>Kirish tokeni umri (daqiqa). SPEC: 15.</summary>
    [Range(1, 24 * 60, ErrorMessage = "Jwt:AccessMinutes 1..1440 oralig'ida bo'lishi kerak.")]
    public int AccessMinutes { get; set; } = 15;

    /// <summary>
    /// Yangilash tokeni umri (kun).
    ///
    /// SPEC'da 14 edi, 7 ga QISQARTIRILDI (qaror, 2026-07-30). Sabab:
    /// rotatsiya bor, lekin ESKI refresh token bekor qilinmaydi (`jti`
    /// saqlanmagani uchun qayta ishlatishni aniqlab bo'lmaydi) — ya'ni
    /// o'g'irlangan token o'z muddatigacha ishlayveradi. Muddatni ikki
    /// barobar qisqartirish shu oynani kamaytiradi. To'liq yechim
    /// (`jti` ro'yxati + reuse detection) hamon ochiq.
    /// </summary>
    [Range(1, 365, ErrorMessage = "Jwt:RefreshDays 1..365 oralig'ida bo'lishi kerak.")]
    public int RefreshDays { get; set; } = 7;
}
