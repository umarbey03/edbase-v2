using Zinnur.Application.LiveSessions.Dtos;

namespace Zinnur.Application.LiveSessions.Services;

/// <summary>
/// ========================================================================
/// DARS BAHOSI (R24) — "baholar har bitta darsga qo'yiladi"
/// ========================================================================
///
/// Loyiha egasining talabi: baho VAZIFAGA emas, DARSGA qo'yilsin va guruh
/// o'quvchilarining baholari JADVAL ko'rinishida tursin.
///
/// ★ SERVIS <see cref="IAttendanceService"/> DAN ALOHIDA, lekin uning
/// AYNAN nusxasi bo'lgan ruxsat qoidasiga ega. Ikkalasi bir xil savolga
/// javob beradi ("bu xodim shu darsning varag'iga tega oladimi?") va
/// qoidani bitta joyga yig'ish o'rniga TAKRORLASH ataylab tanlandi: baho
/// qoidasi kelajakda ajralishi mumkin (masalan "yakunlangan darsni faqat
/// o'quv bo'limi tuzatsin"), umumiy bazaviy sinf esa o'sha o'zgarishni
/// davomatga ham majburan olib kirardi.
///
/// ★ NIMA UCHUN <see cref="Dtos.SessionLessonGradesDto"/> DARS KESIMIDA:
/// birlik — DARS (davomatdagi bilan aynan bir xil mulohaza). Matritsa
/// frontendda ko'rinib turgan ustunlardan yig'iladi, ya'ni 8 oylik
/// guruhning 69 ta darsi bitta javobga tiqilmaydi.
/// </summary>
public interface ILessonGradeService
{
    /// <summary>
    /// Dars bo'yicha baho varag'i: guruhning har bir o'quvchisi bitta
    /// qator (bahosi yo'q o'quvchi ham — <c>score: null</c>).
    ///
    /// RUXSAT: o'quv bo'limi/admin, guruh ustozi/kuratori, bog'langan
    /// kurator guruhining xodimi va darsning hosti. O'QUVCHI — 403.
    /// </summary>
    /// <exception cref="Common.Exceptions.NotFoundException">Dars yo'q.</exception>
    /// <exception cref="Common.Exceptions.ForbiddenException">Ruxsat yo'q.</exception>
    Task<SessionLessonGradesDto> GetSessionGradesAsync(
        long sessionId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Bitta o'quvchining shu darsdagi bahosini QO'YADI yoki QAYTA yozadi.
    ///
    /// Qator yo'q bo'lsa YARATILADI (upsert). Har chaqiruv AUDIT izi
    /// qoldiradi — shu jumladan "hech nima o'zgarmadi" holati ham: nizoda
    /// kim QARAGANI emas, kim TASDIQLAGANI muhim.
    /// </summary>
    /// <exception cref="Common.Exceptions.ValidationException">
    /// Ball berilmagan, manfiy, maxrajdan katta yoki izoh juda uzun.
    /// </exception>
    /// <exception cref="Common.Exceptions.NotFoundException">
    /// Dars yo'q yoki o'quvchi bu darsning guruhiga tegishli emas.
    /// </exception>
    /// <exception cref="Common.Exceptions.ForbiddenException">Ruxsat yo'q.</exception>
    /// <exception cref="Common.Exceptions.ConflictException">Dars bekor qilingan.</exception>
    Task<LessonGradeRowDto> UpsertAsync(
        long sessionId,
        long studentId,
        UpsertLessonGradeRequest request,
        long actorId,
        CancellationToken ct = default);

    /// <summary>
    /// Bahoni butunlay OLIB TASHLAYDI.
    ///
    /// ★ NIMA UCHUN "0 QO'YISH" YETARLI EMAS: 0 — reytingga to'liq
    /// kiradigan HAQIQIY baho ("bajarmadi"), o'chirilgan baho esa umuman
    /// hisobga olinmaydi. Bu amalsiz, adashib boshqa o'quvchiga qo'yilgan
    /// bahoni tuzatishning yagona yo'li unga 0 yozib qo'yish bo'lardi.
    ///
    /// IDEMPOTENT: bahosi yo'q qatorda ham 204 qaytaradi va audit yozmaydi
    /// (o'chirish tugmasini ikki marta bosish xato emas).
    /// </summary>
    /// <exception cref="Common.Exceptions.NotFoundException">Dars yo'q.</exception>
    /// <exception cref="Common.Exceptions.ForbiddenException">Ruxsat yo'q.</exception>
    Task DeleteAsync(
        long sessionId, long studentId, long actorId, CancellationToken ct = default);
}
