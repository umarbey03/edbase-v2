namespace Zinnur.Application.Search.Dtos;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// GLOBAL QIDIRUV (2026-08-18)
/// ════════════════════════════════════════════════════════════════════════
///
/// Loyiha egasi: *"platformani yuqori qismidagi navbarda turishi kerak va
/// bu qismdan platformadagi barcha ma'lumotlarni qidirish imkoni bo'lishi
/// kerak"*.
///
/// ★ BITTA SO'ROV, KO'P TUR: har bo'lim uchun alohida so'rov yuborilsa,
/// klaviaturaning har bosilishida 5 ta HTTP so'rov ketardi va ular
/// TARTIBSIZ qaytib, natijalar sakrab turardi.
///
/// ★ HAR TUR O'Z XATOSINI OLIB YURADI (<see cref="SearchGroupDto.Error"/>):
/// bitta turning so'rovi yiqilsa (masalan ruxsat tekshiruvi), QOLGANLARI
/// baribir ko'rsatiladi. Yassi ro'yxatda bitta nosozlik butun qidiruvni
/// o'chirib qo'yardi.
/// </summary>
/// <param name="Q">Qidiruv matni. 2 belgidan qisqa bo'lsa bo'sh natija.</param>
/// <param name="Limit">Har tur uchun eng ko'p natija (1–20).</param>
/// <param name="Type">
/// Faqat shu turda qidirish (<c>users</c>, <c>groups</c>, <c>courses</c>,
/// <c>tests</c>, <c>assignments</c>). Bo'sh — hammasi.
/// </param>
public sealed record GlobalSearchQuery(string? Q = null, int Limit = 5, string? Type = null);

/// <param name="Type">Tur kaliti — frontend shu bo'yicha marshrutni tanlaydi.</param>
/// <param name="Subtitle">Ikkinchi qator: telefon, ustoz nomi, kurs nomi va h.k.</param>
/// <param name="Meta">O'ng chekkadagi qisqa belgi: rol, holat, a'zolar soni.</param>
/// <param name="Score">
/// Saralash og'irligi (katta — yuqori). Boshlanishiga mos kelgan natija
/// o'rtasiga mos kelganidan doim ustun turadi.
/// </param>
public sealed record SearchHitDto(
    string Type,
    long Id,
    string Title,
    string? Subtitle,
    string? Meta,
    int Score);

/// <param name="Label">Ko'rinadigan bo'lim nomi ("Foydalanuvchilar").</param>
/// <param name="Total">Shu turdagi jami mos natijalar (limitdan oldin).</param>
/// <param name="Error">Shu tur yiqilgan bo'lsa — sababi; aks holda <c>null</c>.</param>
public sealed record SearchGroupDto(
    string Type,
    string Label,
    IReadOnlyList<SearchHitDto> Items,
    int Total,
    string? Error);

/// <param name="TopHit">
/// Barcha turlar bo'ylab eng mos natija.
///
/// ★ NEGA ALOHIDA: natijalar tur bo'yicha guruhlanadi, ya'ni o'quvchining
/// ISMI bilan AYNAN mos kelgan natija guruh nomiga qisman mos kelgan
/// natijadan PASTDA qolishi mumkin edi. Enter bosilganda aynan shu
/// ochiladi.
/// </param>
public sealed record GlobalSearchResultDto(
    string Query,
    SearchHitDto? TopHit,
    IReadOnlyList<SearchGroupDto> Groups);
