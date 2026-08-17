using Zinnur.Application.TeacherAvailability.Dtos;

namespace Zinnur.Application.TeacherAvailability.Services;

/// <summary>
/// Kunlik "darsga o'ta olasizmi?" tasdiqlash + o'rinbosar ustoz tizimi
/// (2026-08-17, birinchi versiya). To'liq kontekst va oqim tavsifi
/// loyihaning tasdiqlangan rejasida.
///
/// ★ BITTA COHESIVE SERVIS — ALOHIDA <c>IFreeTeacherFinder</c>/
/// <c>ISubstituteOfferService</c> INTERFEYSLARI EMAS. Ikkalasi ham
/// FAQAT shu domenning ICHKI qadamlari (bo'sh ustoz qidirish, taklifga
/// javob) — mustaqil chaqiriladigan alohida use-case emas. Loyihada
/// interfeys-har-yordamchiga emas, domen bo'yicha BITTA xizmat sinfiga
/// ustunlik berilgan (masalan <c>GroupService</c>, <c>AssignmentService</c>).
/// </summary>
public interface ITeacherAvailabilityService
{
    /// <summary>
    /// Bugungi (mahalliy sana) darsi bor va hali savol yuborilmagan hamda
    /// joriy "necha kunga yo'q" oynasi bilan qamrab OLINMAGAN ustozlarga
    /// savol yuboradi. <c>TeacherMorningCheckinJob</c> dan chaqiriladi.
    /// </summary>
    /// <returns>Nechta ustozga savol yuborilgani.</returns>
    Task<int> RequestConfirmationsAsync(CancellationToken ct = default);

    /// <summary>
    /// Telegram <c>callback_query</c>sini ishlaydi (<c>av:*</c> yoki
    /// <c>of:*</c> prefiksli <paramref name="data"/>).
    /// </summary>
    /// <param name="senderTelegramId">Tugmani bosgan odamning Telegram ID'si.</param>
    /// <param name="data">Bosilgan tugmaning <c>callback_data</c>si.</param>
    /// <returns>
    /// <c>answerCallbackQuery</c>da ko'rsatiladigan qisqa "toast" matni
    /// (bo'lmasa <c>null</c> — sukut bo'yicha jimgina tasdiqlanadi).
    /// </returns>
    Task<string?> HandleCallbackAsync(long senderTelegramId, string data, CancellationToken ct = default);

    /// <summary>
    /// Erkin matnni (sabab yoki kun soni) shu ustozning KUTAYOTGAN
    /// checkin bosqichiga qo'llashga urinadi.
    /// </summary>
    /// <returns><c>true</c> — matn shu oqim uchun edi va ISHLANDI (chaqiruvchi "yordam" javobini bermasin).</returns>
    Task<bool> HandleFreeTextAsync(long senderTelegramId, string text, CancellationToken ct = default);

    /// <summary>O'quv bo'limi paneli uchun — bugungi holat, barcha ustozlar bo'yicha.</summary>
    Task<IReadOnlyList<TeacherAvailabilityTodayDto>> GetTodayAsync(CancellationToken ct = default);
}
