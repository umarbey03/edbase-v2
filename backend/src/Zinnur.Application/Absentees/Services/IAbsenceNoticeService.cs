using Zinnur.Application.Absentees.Dtos;
using Zinnur.Application.Common.Models;

namespace Zinnur.Application.Absentees.Services;

/// <summary>
/// KELMAGANLARGA XABAR (2026-08-18) — yuborish va tarix.
///
/// ★ HAR OLUVCHIGA ALOHIDA YOZUV: mavjud guruh xabarnomasi (`GroupBroadcast`)
/// bitta qatorda butun guruhni ifodalaydi va "Doniyorga xabar bordimi?"
/// degan savolga javob bera olmaydi. Kelmaganlar bilan ishlashda savol
/// AYNAN shunday.
///
/// ★ YETKAZILISH HOLATI HAQIQIY: navbatdan (`IOutboxStatusReader`) o'qiladi,
/// ya'ni "yuborildi" deb yozib qo'yilmaydi. Telegram rad etgan bo'lsa
/// (bot bloklangan, chat topilmadi) kurator buni ko'radi va qo'ng'iroq
/// qiladi.
/// </summary>
public interface IAbsenceNoticeService
{
    /// <summary>
    /// Tanlangan o'quvchilarga xabar yozadi va Telegram navbatiga qo'yadi.
    ///
    /// Telegrami ulanmagan o'quvchi O'TKAZIB YUBORILMAYDI — yozuv baribir
    /// yaratiladi (holati <c>NoTelegram</c>), chunki kuratorga aynan
    /// shunday o'quvchilar ro'yxati kerak.
    /// </summary>
    Task<SendAbsenceNoticeResultDto> SendAsync(
        SendAbsenceNoticeRequest request, long actorId, CancellationToken ct = default);

    /// <summary>Yuborilgan xabarlar tarixi — "Xabarlar" panelidagi alohida tab.</summary>
    Task<PagedResult<AbsenceNoticeRowDto>> ListAsync(
        AbsenceNoticeListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>AYNI filtrga mos butun to'plam bo'yicha yig'ma.</summary>
    Task<AbsenceNoticeSummaryDto> GetSummaryAsync(
        AbsenceNoticeListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Berilgan darslar bo'yicha ALLAQACHON xabar olgan o'quvchilar.
    ///
    /// Kelmaganlar ro'yxatida "yuborilgan" belgisini chizish uchun —
    /// kurator bir odamga ikki marta yozmasin.
    /// </summary>
    Task<IReadOnlyList<AbsenceNoticeTarget>> GetSentTargetsAsync(
        IReadOnlyCollection<long> sessionIds, long actorId, CancellationToken ct = default);
}
