using Zinnur.Application.Progress.Dtos;

namespace Zinnur.Application.Progress.Services;

/// <summary>
/// Oylik reyting use-case'lari.
///
/// ── QAMROV: IKKITA — GURUH VA O'QUV MARKAZ ──────────────────────────────
///
/// ★ QAROR O'ZGARTIRILDI (2026-08-13, EGASINING KO'RSATMASI). Bu izoh
///   ilgari "qamrov ATAYLAB faqat guruh ichida" deb yozilgan edi va ikki
///   sabab keltirardi:
///
///     1) MAXFIYLIK — o'quvchi butun markazdagi begona o'quvchilarning
///        ismi va natijasini ko'rardi;
///     2) ADOLAT — turli kurs, turli ustoz va turli sur'atdagi guruhlarni
///        bitta jadvalga qo'yish taqqoslanmaydigan narsalarni taqqoslash edi.
///
///   Egasi bu qarorni BEKOR QILDI: *"leaderboardda butun o'quv markaz
///   bo'yicha va guruh bo'yicha bo'lishi kerak"*. Mahsulot qarori texnik
///   e'tirozdan ustun — reyting o'quvchini rag'batlantirish vositasi va
///   markaz miqyosidagi musobaqa aynan shu maqsadga xizmat qiladi.
///
///   Ikki e'tiroz YO'QOLMADI, ular JAVOB OLDI:
///
///     • MAXFIYLIK — markaz jadvali TO'LIQ yuborilmaydi: eng yaxshi
///       <c>CenterTopRows</c> ta qator + so'rovchining O'Z qatori. Ya'ni
///       3000 kishilik markazda o'quvchi 2999 ta begona ismni emas, 100 ta
///       eng yaxshi natijani ko'radi. Kim reyting cho'qqisida ekani
///       markaz ichida baribir ochiq ma'lumot (u devorga osiladi).
///
///     • ADOLAT — ball MUTLAQ emas, FOIZ (uch mezon o'rtachasi, har biri
///       0..100), ya'ni "20 dars o'tgan guruh" va "6 dars o'tgan guruh"
///       o'quvchisi bir xil shkalada o'lchanadi. Davomat maxraji esa har
///       o'quvchida O'Z GURUHINIKI — batafsil
///       <c>LeaderboardService.ComputeCenterAsync</c> izohida.
///
/// ── 🔴 "MARKAZ" ≠ "TIZIMDAGI HAMMA FOYDALANUVCHI" ──────────────────────
///
/// Egasining ikkinchi sharti: *"biz bu loyihani kengaytirib bir nechta
/// o'quv markazlar sotishimizni hisobga olganda umumiy rating faqat o'quv
/// markaz uchun amal qilishi kerak, ya'ni jami tizim foydalanuvchilari
/// uchun emas"*.
///
/// Bugun kodda `LearningCenter` (tenant) tushunchasi YO'Q va bitta
/// deployment bitta markazga xizmat qiladi — ya'ni ikki to'plam AYNAN bir
/// xil. Shuning uchun qamrov ALOHIDA NOMLANGAN tushuncha sifatida
/// yozildi: <c>ILearningCenterScope</c>. Ko'p-markazli o'zgarish o'sha
/// bitta joyga tushadi, bu servisga emas.
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
    /// BUTUN O'QUV MARKAZ bo'yicha jadval: eng yaxshi
    /// <c>LeaderboardService.CenterTopRows</c> ta qator + so'rovchining
    /// o'z qatori (u yuqori yuzlikka kirmasa ham).
    ///
    /// Ko'ra oladi: markazning HAR QANDAY faol foydalanuvchisi — o'quvchi,
    /// ustoz, kurator, o'quv bo'limi, admin. Qamrov (va shu bilan birga
    /// ruxsat) <c>ILearningCenterScope</c> da hal qilinadi.
    /// </summary>
    /// <param name="period"><c>YYYY-MM</c>; <c>null</c> — joriy oy (markaz vaqti).</param>
    Task<CenterLeaderboardDto> GetCenterBoardAsync(
        long viewerId, string? period, CancellationToken ct = default);

    /// <summary>
    /// O'quvchining o'z o'rni (jadvalsiz).
    ///
    /// <paramref name="scope"/> — <see cref="LeaderboardScope.Group"/>
    /// bo'lsa guruh ichidagi o'rin (guruh topilmasa
    /// <see cref="Dtos.MyRankDto.GroupId"/> — <c>null</c>);
    /// <see cref="LeaderboardScope.Center"/> bo'lsa butun markazdagi o'rin
    /// va <c>GroupId</c> HAR DOIM <c>null</c>.
    /// </summary>
    Task<MyRankDto> GetMyRankAsync(
        long studentId, LeaderboardScope scope, string? period, CancellationToken ct = default);
}
