using System.ComponentModel.DataAnnotations;

namespace Zinnur.Infrastructure.Options;

/// <summary>
/// <c>LiveKit</c> bo'limidan bog'lanadigan sozlamalar (SPEC 8-bo'lim).
/// <c>ApiSecret</c> LiveKit serveridagi <c>LIVEKIT_KEYS</c> qiymati bilan
/// AYNAN bir xil bo'lishi shart, aks holda server tokenni rad etadi.
/// </summary>
public sealed class LiveKitOptions
{
    public const string SectionName = "LiveKit";

    /// <summary>LiveKit ham HS256 ishlatadi — kalit 32 baytdan qisqa bo'lmasin.</summary>
    public const int MinSecretLength = 32;

    /// <summary>
    /// ICHKI manzil — server-to-server (LiveKit HTTP API, webhook tekshiruvi).
    /// Docker tarmog'i ichida: <c>http://livekit:7880</c>.
    /// </summary>
    /// <remarks>
    /// NIMA UCHUN IKKI MANZIL: ilgari bitta <c>Url</c> ikki xil ish bajarardi
    /// va prod'da buzilardi. Sahifa HTTPS orqali berilganda brauzer
    /// <c>ws://</c> ulanishini "mixed content" deb TO'SIB QO'YADI — video umuman
    /// ochilmaydi. Ayni paytda backend konteynerga faqat ichki
    /// <c>http://livekit:7880</c> orqali kira oladi (tashqi domen konteyner
    /// tarmog'idan ko'rinmaydi). Bitta o'zgaruvchi ikkalasini ham qanoatlantira olmaydi.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "LiveKit:Url to'ldirilishi shart.")]
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// BRAUZERGA qaytariladigan manzil (<c>LiveKitJoinDto.ServerUrl</c>).
    /// Prod'da MAJBURIY <c>wss://livekit.domen.uz</c>; dev'da bo'sh
    /// qoldirilsa <see cref="Url"/> ishlatiladi.
    /// </summary>
    public string PublicUrl { get; init; } = string.Empty;

    /// <summary>JWT <c>iss</c> claim'iga tushadigan API kalit nomi (masalan <c>devkey</c>).</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "LiveKit:ApiKey to'ldirilishi shart.")]
    public string ApiKey { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "LiveKit:ApiSecret to'ldirilishi shart.")]
    [MinLength(MinSecretLength, ErrorMessage = "LiveKit:ApiSecret kamida 32 belgi bo'lishi shart.")]
    public string ApiSecret { get; init; } = string.Empty;

    /// <summary>
    /// Klientga beriladigan haqiqiy manzil: <see cref="PublicUrl"/>,
    /// u bo'sh bo'lsa <see cref="Url"/> (dev qulayligi).
    /// </summary>
    public string EffectivePublicUrl =>
        string.IsNullOrWhiteSpace(PublicUrl) ? Url : PublicUrl;

    /// <summary>Manzil absolyut va qo'llab-quvvatlanadigan sxemada ekanini tekshiradi.</summary>
    public static bool HasSupportedScheme(string? url, params string[] schemes)
    {
        ArgumentNullException.ThrowIfNull(schemes);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return Array.Exists(schemes, s => string.Equals(uri.Scheme, s, StringComparison.OrdinalIgnoreCase));
    }
}
