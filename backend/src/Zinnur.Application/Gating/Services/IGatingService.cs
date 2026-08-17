using Zinnur.Application.Gating.Dtos;

namespace Zinnur.Application.Gating.Services;

/// <summary>
/// Sur'at nazorati (gating): qaysi kurs darsi o'quvchi uchun ochiq.
///
/// Qoida <see cref="LessonGate"/> da (sof funksiya), bu servis esa faqat
/// FAKTLARNI topadi, keshlaydi va keshni bekor qiladi.
/// </summary>
public interface IGatingService
{
    /// <summary>
    /// Butun kurs bo'yicha ochiqlik xaritasi. Keshdan o'qiladi va bir so'rov
    /// ichida KO'PI BILAN BIR MARTA hisoblanadi.
    /// </summary>
    Task<CourseGateDto> GetCourseGateAsync(long studentId, CancellationToken ct = default);

    /// <summary>
    /// BITTA darsning ochiqligi — ARZON yo'l. Kesh bo'sh bo'lsa ham butun
    /// daraxtni qurmaydi (faqat shu dars va undan oldingi dars faktlari).
    /// </summary>
    Task<LessonGateDto> GetLessonGateAsync(
        long studentId, long moduleLessonId, CancellationToken ct = default);

    /// <summary>
    /// Dars yopiq bo'lsa <see cref="Common.Exceptions.ForbiddenException"/> —
    /// sababi bilan. Vazifa topshirish va test boshlashda chaqiriladi.
    /// </summary>
    Task EnsureLessonUnlockedAsync(
        long studentId, long moduleLessonId, CancellationToken ct = default);

    /// <summary>
    /// Videoni ko'rilgan deb belgilaydi (idempotent) va keshni bekor qiladi.
    ///
    /// 🔴 DARS OCHIQ BO'LISHI SHART: aks holda o'quvchi yopiq darslarning
    /// Id'sini yuborib gating'ni o'zi ochib olardi. Yopiq bo'lsa —
    /// <see cref="Common.Exceptions.ForbiddenException"/>.
    /// </summary>
    Task<LessonGateDto> MarkVideoWatchedAsync(
        long studentId, long moduleLessonId, CancellationToken ct = default);

    /// <summary>Darsni qo'lda ochish/yopish (o'quv bo'limi istisnosi) + kesh bekor qilinadi.</summary>
    Task<LessonGateDto> SetOverrideAsync(
        long studentId,
        long moduleLessonId,
        bool unlocked,
        string? reason,
        long actorId,
        CancellationToken ct = default);

    /// <summary>
    /// Bitta o'quvchining keshini bekor qiladi. Progress o'zgargan HAR
    /// nuqtadan chaqiriladi: video ko'rildi, vazifa topshirildi, test yechildi,
    /// istisno qo'yildi.
    /// </summary>
    Task InvalidateAsync(long studentId, CancellationToken ct = default);

    /// <summary>
    /// GURUH kursda qayerga yetgani — xodim hisobotlari uchun (2026-08-17).
    ///
    /// ★ NIMA UCHUN SHU SERVISDA: "guruh nechta dars o'tgan" degan fakt
    /// gating'ning O'ZAK hisobi (<see cref="LessonGate"/> shunga qarab
    /// darsni ochadi) va u YAKUNLANGAN ustoz darslari sonidan hisoblanadi.
    /// Darslarning BARQAROR tartibi ham shu yerda (`OrderedLessons`).
    /// Ikkinchi joyda takrorlansa, ikki xil "guruh qayerda" javobi paydo
    /// bo'lardi — biri o'quvchiga dars ochadi, ikkinchisi hisobotda boshqa
    /// raqam ko'rsatardi.
    ///
    /// 🔴 KESHLANMAYDI: kesh o'quvchi bo'yicha kalitlangan va progress
    /// o'zgarganda bekor qilinadi. Bu esa xodim hisoboti — kamdan-kam
    /// so'raladi va har safar yangi ma'lumot berishi kerak.
    ///
    /// Guruh topilmasa yoki kursi bo'lmasa — <c>null</c>.
    /// </summary>
    Task<GroupPaceDto?> GetGroupPaceAsync(long groupId, CancellationToken ct = default);
}
