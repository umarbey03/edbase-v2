using Zinnur.Application.Common.Models;
using Zinnur.Application.Enrollment.Dtos;

namespace Zinnur.Application.Enrollment.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// KURSGA ARIZA (2026-08-28) — landing sahifadagi forma
/// ════════════════════════════════════════════════════════════════════════
///
/// 🔴 BU RO'YXATDAN O'TISH SERVISI EMAS. Yagona yozish amali
/// (<see cref="SubmitAsync"/>) <c>Users</c> jadvaliga UMUMAN tegmaydi va
/// hech qanday kirish huquqi bermaydi.
///
/// Loyiha egasining qarori (2026-08-28): saytda "kursga yozilish" bo'lsin,
/// lekin o'z-o'zidan hisob ochish BO'LMASIN. Bu bot uchun allaqachon
/// qabul qilingan qoidaning aynan o'zi ("bot AKKAUNT YARATMAYDI" —
/// <c>TelegramUpdateHandler.HandleContactAsync</c>), va uni saytdan qayta
/// ochish o'sha qarorni bekor qilardi.
/// ════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IEnrollmentApplicationService
{
    /// <summary>
    /// Ariza qabul qiladi. ANONIM chaqiruv.
    /// </summary>
    /// <remarks>
    /// ══════════════════════════════════════════════════════════════════
    /// 🔴 JAVOB HECH NARSA QAYTARMAYDI — VA BU ATAYLAB.
    ///
    /// Ariza yaratilganmi, bu raqam allaqachon o'quvchimi, avval ariza
    /// qoldirganmi — HECH BIRI aytilmaydi. Aks holda forma "bu raqam
    /// markazda o'qiydimi?" degan savolga javob beradigan ochiq qidiruv
    /// vositasiga aylanardi, ya'ni kirish oqimida ataylab yopilgan
    /// hisob sanash yo'li (<c>IPhoneLoginService</c>) shu yerdan qaytib
    /// kelardi.
    ///
    /// ★ ISTISNOLAR:
    ///   • <c>ValidationException</c> — ism yoki raqam yaroqsiz. Bu hech
    ///     nima oshkor qilmaydi: yaroqsizlik foydalanuvchining O'ZIGA
    ///     ham ko'rinib turibdi;
    ///   • <c>TooManyRequestsException</c> — kvota (raqam bo'yicha).
    /// ══════════════════════════════════════════════════════════════════
    /// </remarks>
    Task SubmitAsync(CreateEnrollmentApplicationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Arizalar ro'yxati — o'quv bo'limi va admin uchun.
    /// </summary>
    /// <remarks>
    /// 🔴 CHAQIRUVCHI ROLNI ALLAQACHON TEKSHIRGAN bo'lishi SHART: bu
    /// yerda telefon raqamlari qaytadi (R27) va ular ustozga ochilmaydi.
    /// Tekshiruv controller darajasida, <c>[Authorize(Roles = ...)]</c>
    /// bilan.
    /// </remarks>
    Task<PagedResult<EnrollmentApplicationDto>> ListAsync(
        EnrollmentApplicationListParams request, CancellationToken ct = default);

    /// <summary>
    /// Holatni va operator izohini yangilaydi.
    /// </summary>
    /// <param name="actorId">
    /// Amalni bajargan xodim — SERVERDA, tokendan olinadi. So'rov
    /// tanasidan HECH QACHON kelmaydi.
    /// </param>
    Task<EnrollmentApplicationDto> UpdateAsync(
        long id, UpdateEnrollmentApplicationRequest request, long actorId, CancellationToken ct = default);
}
