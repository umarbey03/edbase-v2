using Zinnur.Application.Search.Dtos;

namespace Zinnur.Application.Search.Services;

/// <summary>
/// GLOBAL QIDIRUV (2026-08-18) — navbardagi yagona qidiruv maydoni.
///
/// ★ NATIJALAR ROLGA QARAB FILTRLANADI: ustoz faqat O'Z guruhlarini va
/// o'sha guruhlardagi o'quvchilarni ko'radi. Aks holda qidiruv butun
/// bazani ochib beradigan teshikka aylanardi — ro'yxat ekranlarida
/// qat'iy ruxsat bo'lsa-yu, qidiruvda bo'lmasa, himoyaning ma'nosi
/// qolmasdi.
///
/// ★ FAQAT O'QIYDI.
/// </summary>
public interface IGlobalSearchService
{
    Task<GlobalSearchResultDto> SearchAsync(
        GlobalSearchQuery query, long actorId, CancellationToken ct = default);
}
