using Zinnur.Application.LiveSessions.Dtos;

namespace Zinnur.Application.LiveSessions.Services;

/// <summary>
/// ========================================================================
/// DAVOMATNI QO'LDA TUZATISH (FAZA 3.5 qoldig'i)
/// ========================================================================
///
/// `Attendance` jadvali SignalR hodisalari bilan AVTOMATIK to'ladi
/// (<see cref="ILiveSessionService.RegisterJoinAsync"/>), lekin o'lchov
/// har doim ham haqiqatni aytmaydi: interneti uzilgan o'quvchi darsni
/// telefonda tinglab o'tirgan bo'lishi, yoki brauzer yopilib qolgani
/// uchun "kelmagan" bo'lib qolishi mumkin. Eski tizimdagi ustoz panelining
/// "Davomat" tabi aynan shu tuzatish uchun edi.
///
/// ★ SERVIS ALOHIDA (<see cref="ILiveSessionService"/> ga qo'shilmadi):
/// u JONLI oqim servisi — uni SignalR hub'i har ulanish/uzilishda
/// chaqiradi. Bu esa MA'MURIY amal: boshqa rollar, boshqa chaqiruv
/// chastotasi va boshqa ruxsat qoidasi. Bitta servisga qo'shilsa, jonli
/// oqim yo'lidagi o'zgarish ma'muriy ruxsatga tegib ketishi mumkin edi.
/// </summary>
public interface IAttendanceService
{
    /// <summary>
    /// Dars bo'yicha davomat varag'i.
    ///
    /// RUXSAT: o'quv bo'limi/admin, guruh ustozi/kuratori, bog'langan
    /// kurator guruhining xodimi va darsning hosti. O'QUVCHI — 403
    /// (u o'z davomatini <c>GET /api/v1/progress/attendance</c> va
    /// kalendardan ko'radi).
    /// </summary>
    /// <exception cref="Common.Exceptions.NotFoundException">Dars yo'q.</exception>
    /// <exception cref="Common.Exceptions.ForbiddenException">Ruxsat yo'q.</exception>
    Task<SessionAttendanceDto> GetSessionAttendanceAsync(
        long sessionId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Bitta o'quvchining shu darsdagi bahosini QO'LDA tuzatadi.
    ///
    /// Qator yo'q bo'lsa YARATILADI (o'quvchi xonaga umuman kirmagan
    /// bo'lsa ham ustoz uni "kelgan" deb belgilay olishi kerak).
    ///
    /// ★ Vaqt o'lchovlariga (kirish/chiqish/davomiylik) TEGILMAYDI —
    /// sababi <c>Attendance.ApplyManual</c> izohida.
    ///
    /// IDEMPOTENT EMAS, lekin xavfsiz: har chaqiruv audit yozuvi qoldiradi,
    /// shu jumladan "hech nima o'zgarmadi" holati ham — kim qaraganini
    /// emas, kim TASDIQLAGANINI bilish nizoda muhim.
    /// </summary>
    /// <exception cref="Common.Exceptions.ValidationException">
    /// Holat berilmagan yoki sabab juda uzun.
    /// </exception>
    /// <exception cref="Common.Exceptions.NotFoundException">
    /// Dars yo'q yoki o'quvchi bu darsning guruhiga tegishli emas.
    /// </exception>
    /// <exception cref="Common.Exceptions.ForbiddenException">Ruxsat yo'q.</exception>
    Task<AttendanceRowDto> UpdateAsync(
        long sessionId,
        long studentId,
        UpdateAttendanceRequest request,
        long actorId,
        CancellationToken ct = default);
}
