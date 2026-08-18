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
    /// Berilgan darslar bo'yicha ALLAQACHON xabar olgan o'quvchilar va
    /// ularning javob holati.
    ///
    /// Kelmaganlar ro'yxatida "yuborilgan" va "sabab keldi" belgilarini
    /// chizish uchun — kurator bir odamga ikki marta yozmasin va faqat
    /// JAVOB BERMAGANLARGA qo'ng'iroq qilsin.
    /// </summary>
    Task<IReadOnlyList<AbsenceNoticeStatusDto>> GetSentTargetsAsync(
        IReadOnlyCollection<long> sessionIds, long actorId, CancellationToken ct = default);

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// TELEGRAMDAN KELGAN SABABNI QABUL QILADI (2026-08-18)
    /// ════════════════════════════════════════════════════════════════
    ///
    /// Bot o'quvchidan matn olganda chaqiriladi: shu o'quvchining javob
    /// KUTAYOTGAN eng so'nggi xabari topilib, matn sabab sifatida
    /// yoziladi.
    ///
    /// ★ NEGA BOT TOMONDAN, ALOHIDA ENDPOINT EMAS: o'quvchi ilovaga
    /// kirmaydi — u xabarni Telegramda oladi va o'sha yerga javob
    /// yozadi. Boshqa yo'l talab qilinsa, javob berish darajasi keskin
    /// tushardi (aynan shu narsa kuratorning qo'ng'iroq ro'yxatini
    /// qisqartiradi).
    /// </summary>
    /// <param name="telegramUserId">Telegram foydalanuvchi ID'si.</param>
    /// <returns>
    /// <c>true</c> — matn sabab sifatida qabul qilindi (bot "rahmat"
    /// deb javob beradi); <c>false</c> — kutilayotgan xabar yo'q, matn
    /// odatiy yo'l bilan ishlanadi.
    /// </returns>
    Task<bool> TryCaptureReplyAsync(
        long telegramUserId, string? text, CancellationToken ct = default);

    /// <summary>
    /// Xabardagi TAYYOR SABAB tugmasi bosilganini ishlaydi (2026-08-18).
    ///
    /// ★ NEGA TUGMA KERAK: "sababini shu yerga yozing" degan yo'riqni
    /// o'qish va bajarish — ikki qadam, va ko'p o'quvchi ikkinchisiga
    /// yetib bormaydi. Tugma esa bir bosishda tugaydi.
    /// </summary>
    /// <param name="data">
    /// <c>callback_data</c>: <c>ab:r:{noticeId}:{code}</c>. Kod
    /// <c>other</c> bo'lsa erkin matn so'raladi.
    /// </param>
    /// <returns>
    /// Telegramga ko'rsatiladigan qisqa xabar (toast), yoki bu callback
    /// bizga tegishli bo'lmasa <c>null</c>.
    /// </returns>
    Task<string?> HandleCallbackAsync(
        long telegramUserId, string? data, CancellationToken ct = default);

    /// <summary>
    /// "Qo'ng'iroq qilindi" izini yozadi.
    ///
    /// ★ NEGA KERAK: xabar yuborgan odam va qo'ng'iroq qilgan odam
    /// odatda BOSHQA (xabarni o'quv bo'limi yuboradi, qo'ng'iroqni
    /// kurator qiladi). Iz bo'lmasa, ikki kurator bir o'quvchiga ikki
    /// marta qo'ng'iroq qilardi.
    ///
    /// Ustoz va kurator ham bajara oladi — qo'ng'iroq amalda ularning ishi.
    /// </summary>
    Task<AbsenceNoticeRowDto> MarkCalledAsync(
        long noticeId, MarkCalledRequest request, long actorId, CancellationToken ct = default);
}
