using Zinnur.Application.Progress.Dtos;

namespace Zinnur.Application.Progress.Services;

/// <summary>
/// O'quvchining davomat xulosasi — eski ilovadagi "doira" shu servisdan
/// oziqlanadi (qatnashgan / qoldirgan / jami + foiz).
/// </summary>
public interface IAttendanceSummaryService
{
    /// <summary>
    /// ★ FAQAT O'ZINIKI. Metod boshqa o'quvchining Id'sini QABUL QILMAYDI —
    /// shu tufayli "begona o'quvchining davomatini so'rash" degan xato
    /// yozilishi mumkin emas (ruxsatni unutib qo'yish imkoni yo'q).
    /// Xodim ko'rinishi kerak bo'lganda alohida, oshkora endpoint qo'shiladi.
    /// </summary>
    /// <param name="groupId">
    /// <c>null</c> — o'quvchining BARCHA faol guruhlari birga.
    /// Berilsa — o'quvchi o'sha guruhning faol a'zosi bo'lishi shart (aks holda 403).
    /// </param>
    /// <param name="fromDate">Mahalliy sana, KIRADI. <c>null</c> — cheklovsiz.</param>
    /// <param name="toDate">Mahalliy sana, KIRADI. <c>null</c> — cheklovsiz.</param>
    Task<AttendanceSummaryDto> GetMySummaryAsync(
        long studentId,
        long? groupId,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken ct = default);
}
