using Zinnur.Application.Progress.Dtos;

namespace Zinnur.Application.Progress.Services;

/// <summary>
/// Oylik reyting use-case'lari.
///
/// ★ QAMROV ATAYLAB FAQAT GURUH ICHIDA. Eski tizimda "umumiy reyting"
/// ham bor edi (barcha markaz o'quvchilari bitta jadvalda), lekin u
/// ikki muammo bilan keldi:
///
///   1) RUXSAT: o'quvchi butun markazdagi begona o'quvchilarning ismi va
///      natijasini ko'rardi. v2 qoidasi — o'quvchi faqat O'Z guruhini
///      ko'radi.
///
///   2) ADOLAT: turli kurslar, turli ustozlar va turli sur'atdagi
///      guruhlarni bitta jadvalga qo'yish taqqoslanmaydigan narsalarni
///      taqqoslash edi.
///
/// Umumiy reyting kerak bo'lsa u XODIM uchun alohida hisobot sifatida
/// qo'shiladi — o'quvchi ilovasida emas.
/// </summary>
public interface ILeaderboardService
{
    /// <summary>
    /// Guruhning bir oylik reyting jadvali.
    ///
    /// Ko'ra oladi: guruhning FAOL a'zosi, guruh ustozi/kuratori,
    /// o'quv bo'limi va admin. Boshqasi — 403.
    /// Arxivlangan guruh reytingi HECH KIMGA ko'rinmaydi.
    /// </summary>
    /// <param name="period"><c>YYYY-MM</c>; <c>null</c> — joriy oy (markaz vaqti).</param>
    Task<GroupLeaderboardDto> GetGroupBoardAsync(
        long groupId, long viewerId, string? period, CancellationToken ct = default);

    /// <summary>
    /// O'quvchining o'z o'rni (jadvalsiz). Guruh topilmasa
    /// <see cref="Dtos.MyRankDto.GroupId"/> — <c>null</c>.
    /// </summary>
    Task<MyRankDto> GetMyRankAsync(
        long studentId, string? period, CancellationToken ct = default);
}
