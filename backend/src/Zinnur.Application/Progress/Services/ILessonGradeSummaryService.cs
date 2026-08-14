using Zinnur.Application.Progress.Dtos;

namespace Zinnur.Application.Progress.Services;

/// <summary>
/// ========================================================================
/// O'QUVCHINING O'Z DARS BAHOLARI (R24 — o'quvchi tomoni)
/// ========================================================================
///
/// ★ NIMA UCHUN BU SERVIS KERAK BO'LDI: R24 bilan <c>LessonGrade</c>
/// qurildi va ustoz baho qo'yadi, lekin O'QUVCHI uchun o'z bahosini
/// o'qiydigan endpoint YO'Q edi — u faqat reyting ekranidagi yig'ma
/// <c>lessonPercent</c> ni ko'rardi. Ya'ni "4 ball qayerdan chiqdi?"
/// degan savolga tizim javob bera olmasdi.
///
/// ★ NIMA UCHUN <see cref="LiveSessions.Services.ILessonGradeService"/> GA
/// METOD QO'SHILMADI: u XODIM servisi — har bir yo'li
/// <c>LoadAndAuthorizeAsync</c> dan o'tadi va u yerdagi ruxsat ro'yxatida
/// o'quvchi ATAYLAB yo'q. O'quvchi metodini o'sha sinfga qo'shish o'sha
/// darvozani "ba'zan o'tkazadigan" qilardi. Bu yerdagi ajratish
/// <c>AttendanceService</c> (xodim) ↔ <see cref="IAttendanceSummaryService"/>
/// (o'quvchi) juftligining AYNAN nusxasi.
/// </summary>
public interface ILessonGradeSummaryService
{
    /// <summary>
    /// ★ FAQAT O'ZINIKI. Metod boshqa o'quvchining Id'sini QABUL QILMAYDI —
    /// <c>studentId</c> DOIM tokendan keladi, ya'ni "begona o'quvchining
    /// baholarini so'rash" degan xato yozilishi MUMKIN EMAS
    /// (<see cref="IAttendanceSummaryService.GetMySummaryAsync"/> dagi
    /// AYNI himoya).
    /// </summary>
    /// <param name="groupId">
    /// <c>null</c> — o'quvchining BARCHA faol guruhlari birga.
    /// Berilsa — o'quvchi o'sha guruhning faol a'zosi bo'lishi shart
    /// (aks holda 403).
    /// </param>
    /// <param name="fromDate">Mahalliy sana, KIRADI. <c>null</c> — cheklovsiz.</param>
    /// <param name="toDate">Mahalliy sana, KIRADI. <c>null</c> — cheklovsiz.</param>
    Task<MyLessonGradesDto> GetMyGradesAsync(
        long studentId,
        long? groupId,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken ct = default);
}
