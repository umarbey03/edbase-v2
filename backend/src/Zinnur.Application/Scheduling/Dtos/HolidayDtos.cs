namespace Zinnur.Application.Scheduling.Dtos;

/// <summary>Bayram kalendari yozuvi.</summary>
public sealed record HolidayDto(
    long Id,
    DateOnly Date,
    string Label,
    long CreatedById,
    string? CreatedByName,
    DateTimeOffset CreatedAt);

/// <summary>
/// Bayram e'lon qilish so'rovi. <paramref name="EndDate"/> — sana oralig'i
/// (2026-08-16, loyiha egasi: "bayram kunlari kiritishda date range qilish
/// imkoni bo'lishi kerak"); bitta kunlik bayram uchun ikkalasi teng
/// yuboriladi. Har bir kun uchun alohida <c>Holiday</c> qatori yaratiladi —
/// entity darajasida "oraliq" tushunchasi yo'q, faqat so'rov darajasida.
/// </summary>
public sealed record CreateHolidayRequest(DateOnly StartDate, DateOnly EndDate, string Label);

/// <summary>
/// Bayram e'lon qilingandan keyingi ta'sir — xodim "nechta guruhga tegdi"
/// degan savolga DARHOL javob olishi uchun (`HolidayService.CreateAsync`
/// sinxron ishlaydi, izohi shu servis faylida). Oraliq bir nechta kunni
/// qamrab olishi mumkin — shuning uchun <paramref name="Holidays"/> RO'YXAT.
/// </summary>
/// <param name="Holidays">Yaratilgan har bir kun uchun bitta yozuv.</param>
/// <param name="SkippedCount">
/// Oraliqda ALLAQACHON bayram sifatida belgilangan (shu sabab o'tkazib
/// yuborilgan) kunlar soni. Butun oraliq mavjud bo'lsa — bu holat
/// <c>ConflictException</c> bilan qaytadi (`CreateAsync` izohi).
/// </param>
public sealed record HolidayImpactDto(
    IReadOnlyList<HolidayDto> Holidays,
    int SkippedCount,
    int AffectedGroupCount,
    int CancelledSessionCount);
