namespace Zinnur.Application.Common.Exceptions;

/// <summary>So'ralgan resurs topilmadi -> HTTP 404.</summary>
public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} topilmadi (id: {key}).");

/// <summary>Foydalanuvchi autentifikatsiyadan o'tgan, lekin huquqi yo'q -> HTTP 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>Autentifikatsiya muvaffaqiyatsiz -> HTTP 401.</summary>
public sealed class UnauthorizedException(string message) : Exception(message);

/// <summary>Holat ziddiyati (masalan takror amal) -> HTTP 409.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Ichki sabab bilan — bazadagi to'qnashuvni (unikal indeks buzilishi)
    /// 409 ga o'girishda ishlatiladi.
    ///
    /// NIMA UCHUN SABAB SAQLANADI: foydalanuvchi tushunarli xabar oladi,
    /// lekin Sentry'ga ASL istisno (SQL holati va stek bilan) tushadi.
    /// Ichki sabab tashlab yuborilsa, "409 keldi, lekin nima uchun?"
    /// degan savolga logdan javob topib bo'lmasdi.
    /// </summary>
    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Tashqi bog'liqlik (obyekt ombori, LiveKit) sozlanmagan yoki javob
/// bermayapti -> HTTP 503.
///
/// NIMA UCHUN ALOHIDA TUR: 500 ("bizda bug") va 503 ("xizmat vaqtincha
/// yo'q") boshqa-boshqa hodisa. Sozlanmagan omborni 500 bilan qaytarish
/// ogohlantirish tizimini bekordan uyg'otardi va foydalanuvchi "keyinroq
/// urinib ko'ring" degan to'g'ri maslahatni olmasdi.
/// </summary>
public sealed class ServiceUnavailableException(string message) : Exception(message);

/// <summary>
/// Foydalanuvchi ruxsat etilganidan tez-tez so'rov yubordi -> HTTP 429.
///
/// NIMA UCHUN ALOHIDA TUR (409 yoki 403 emas): tezlik chegarasi "sizda
/// huquq yo'q" ham, "holat ziddiyati" ham EMAS — u "hozir emas, biroz
/// keyin" degani. Klient uchun farq amaliy: 403 da u qayta urinmaydi va
/// foydalanuvchiga "ruxsat yo'q" deb ko'rsatadi, 429 da esa
/// <see cref="RetryAfterSeconds"/> ni o'qib kutadi.
///
/// ★ HTTP darajasidagi <c>AddRateLimiter</c> siyosatidan farqi: u IP
/// bo'yicha ishlaydi va butun endpointni yopadi. Bu istisno esa
/// FOYDALANUVCHI va OQIM bo'yicha, use-case ichida hisoblanadi — bitta
/// maktabning NAT IP'si orqasidagi 30 o'quvchi bir-birini bloklamasin.
/// </summary>
public sealed class TooManyRequestsException(string message, int retryAfterSeconds)
    : Exception(message)
{
    /// <summary>Klient shuncha sekunddan keyin qayta urinsin (<c>Retry-After</c>).</summary>
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>
/// Yuborilgan fayl ruxsat etilgan hajmdan katta -> HTTP 413.
///
/// ★ NIMA UCHUN 400 EMAS (ONGLI QAROR): AYNI shartni Kestrel ham
/// tekshiradi va u 413 qaytaradi — juda katta so'rov tanasi bizning
/// kodimizga umuman yetib kelmasligi mumkin. Agar o'z tekshiruvimizda 400
/// bersak, BITTA va AYNI nosozlik (fayl juda katta) chegaradan qanchalik
/// oshganiga qarab gohida 413, gohida 400 bo'lardi — frontend ikki
/// tarmoqli mantiq yozishga majbur bo'lardi va bittasini albatta unutardi.
///
/// ⚠️ FARQI <see cref="ValidationException"/> DAN: 400 — "ma'lumot
/// NOTO'G'RI" (masalan `.mp4` deb nomlangan fayl ichida PDF). 413 —
/// "ma'lumot to'g'ri, lekin KATTA". Foydalanuvchi uchun ham farq amaliy:
/// birinchisida boshqa fayl kerak, ikkinchisida o'sha faylni siqish yoki
/// qismlarga bo'lish kifoya.
/// </summary>
public sealed class PayloadTooLargeException(string message) : Exception(message);

/// <summary>
/// So'ralgan bayt oralig'i (`Range`) fayl chegarasidan tashqarida -> HTTP 416.
///
/// ★ NIMA UCHUN ALOHIDA TUR, 400 EMAS: 416 javobiga
/// <c>Content-Range: bytes */&lt;hajm&gt;</c> sarlavhasi QO'SHILISHI shart
/// (HTTP standarti talabi) — ya'ni javob yozadigan joyga faylning TO'LIQ
/// hajmi yetib borishi kerak. Oddiy 400 bilan bu ma'lumot yo'qolardi va
/// ba'zi video pleyerlar to'g'ri hajmni bilmagani uchun cheksiz qayta
/// urinaverardi.
/// </summary>
public sealed class RangeNotSatisfiableException(long totalLength)
    : Exception("So'ralgan oraliq fayl hajmidan tashqarida.")
{
    /// <summary>Faylning to'liq hajmi — <c>Content-Range: bytes */N</c> uchun.</summary>
    public long TotalLength { get; } = totalLength;
}

/// <summary>Kiruvchi ma'lumot noto'g'ri -> HTTP 400.</summary>
public sealed class ValidationException(IDictionary<string, string[]> errors)
    : Exception("Kiritilgan ma'lumotlarda xatolik bor.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
