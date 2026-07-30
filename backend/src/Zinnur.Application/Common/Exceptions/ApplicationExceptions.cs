namespace Zinnur.Application.Common.Exceptions;

/// <summary>So'ralgan resurs topilmadi -> HTTP 404.</summary>
public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} topilmadi (id: {key}).");

/// <summary>Foydalanuvchi autentifikatsiyadan o'tgan, lekin huquqi yo'q -> HTTP 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>Autentifikatsiya muvaffaqiyatsiz -> HTTP 401.</summary>
public sealed class UnauthorizedException(string message) : Exception(message);

/// <summary>Holat ziddiyati (masalan takror amal) -> HTTP 409.</summary>
public sealed class ConflictException(string message) : Exception(message);

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

/// <summary>Kiruvchi ma'lumot noto'g'ri -> HTTP 400.</summary>
public sealed class ValidationException(IDictionary<string, string[]> errors)
    : Exception("Kiritilgan ma'lumotlarda xatolik bor.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
