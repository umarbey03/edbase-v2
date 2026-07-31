using Zinnur.Application.Common.Export;
using Zinnur.Application.Payments.Dtos;

namespace Zinnur.Application.Payments.Services;

/// <summary>
/// MOLIYA YIG'MA HISOBOTI — faqat O'QISH.
///
/// ★ NIMA UCHUN <see cref="IPaymentService"/> DAN AYRIM:
///
///  1) BU SERVIS HECH NARSANI O'ZGARTIRMAYDI. Pul yozadigan servis bilan
///     bitta interfeysda tursa, hisobot uchun kerak bo'lgan yangi metod
///     har safar pul yozish kodining yonida paydo bo'lardi va bir kuni
///     hisobot metodida tasodifan `SaveChanges` chaqirilardi.
///
///  2) `PaymentService` allaqachon 1500 qatordan oshgan. Hisobot uning
///     ichiga qo'shilsa fayl o'qib bo'lmas holga kelardi.
///
///  3) Kelajakda hisobot keshlanadigan yoki o'qish replikasiga
///     yo'naltiriladigan bo'lsa, buni ALOHIDA turda qilish oson.
///
/// RUXSAT: faqat <c>Academic</c> va <c>Admin</c>. Qoida QAYTA YOZILMAYDI —
/// <see cref="IPaymentService.EnsureCanManageFinanceAsync"/> chaqiriladi,
/// ya'ni butun moliyada YAGONA ruxsat qoidasi ishlaydi.
/// </summary>
public interface IPaymentSummaryService
{
    /// <summary>
    /// KPI, qarz yoshi, oxirgi 12 oy dinamikasi va kesimlar — BITTA javobda.
    ///
    /// Barcha yig'indi SQL tomonda hisoblanadi: C# xotirasiga faqat
    /// agregat natijalar (o'nlab qator) keladi, minglab to'lov qatori EMAS.
    /// </summary>
    Task<PaymentSummaryDto> GetSummaryAsync(
        PaymentSummaryQuery query, long actorId, CancellationToken ct = default);

    /// <summary>
    /// AYNI hisobotning CSV ko'rinishi (Excel uchun BOM va <c>sep=</c> bilan).
    ///
    /// ★ Ma'lumot <see cref="GetSummaryAsync"/> dan olinadi — ikkinchi
    /// hisoblash yo'li YOZILMAYDI. Aks holda ekrandagi raqam bilan
    /// yuklangan fayldagi raqam bir kuni farq qilib qolardi va qaysi biri
    /// to'g'riligini hech kim bilmasdi (eski tizimning klassik xatosi).
    /// </summary>
    Task<CsvExport> ExportSummaryCsvAsync(
        PaymentSummaryQuery query, long actorId, CancellationToken ct = default);
}
