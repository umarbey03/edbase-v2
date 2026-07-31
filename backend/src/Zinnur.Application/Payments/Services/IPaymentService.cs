using Zinnur.Application.Common.Models;
using Zinnur.Application.Payments.Dtos;

namespace Zinnur.Application.Payments.Services;

/// <summary>
/// Moliya use-case'lari. HTTP haqida hech narsa bilmaydi.
///
/// RUXSAT: barcha o'zgartiruvchi amallarni FAQAT <c>Academic</c> va
/// <c>Admin</c> bajaradi; o'quvchi faqat O'Z hisobini o'qiydi; ustoz va
/// kurator moliyaga umuman kirmaydi. Qoida servis ICHIDA — kontrollerdagi
/// atribut faqat darvoza (servis fon vazifasidan ham chaqiriladi).
/// </summary>
public interface IPaymentService
{
    // ---------------------------------------------------------------- oy ochish

    /// <summary>
    /// Har faol a'zolikka joriy (yoki so'ralgan) oy uchun to'lov yozuvini
    /// ochadi va ochilgandan KEYIN balansdan avtomatik yopadi.
    ///
    /// IDEMPOTENT: takror chaqirilsa yangi qator YARATILMAYDI va xato ham
    /// bermaydi — mavjudlari jimgina o'tkazib yuboriladi.
    /// </summary>
    Task<OpenPeriodResult> OpenPeriodAsync(
        OpenPeriodRequest request, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- pul

    /// <summary>
    /// ★ To'lov kiritishning YAGONA yo'li: jurnal yozuvi + oylarni yopish +
    /// ortiqchani balansga + audit — BITTA tranzaksiyada.
    /// </summary>
    Task<PaymentReceiptDto> RecordPaymentAsync(
        RecordPaymentRequest request, long actorId, CancellationToken ct = default);

    /// <summary>Oyni kechiradi (pul olinmaydi). Jurnal va audit qoldiradi.</summary>
    Task<PaymentDto> WaiveAsync(
        long paymentId, WaiveRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Pulni orqaga qaytaradi: avval balansdan, so'ng eng YANGI to'langan
    /// oylardan. Jurnal va audit qoldiradi.
    /// </summary>
    Task<ReversalDto> ReverseAsync(
        ReversePaymentRequest request, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- o'qish

    /// <summary>O'quvchi hisobi: qarz, balans, oylar tarixi, jurnal.</summary>
    Task<StudentAccountDto> GetStudentAccountAsync(
        long studentId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// "Shu o'quvchining moliyasini ko'ra oladimi" — YENGIL tekshiruv
    /// (ma'lumot yuklamaydi), ruxsati yo'q bo'lsa 403.
    ///
    /// NIMA UCHUN KERAK: blok holati endpointi ham AYNI qoidaga bo'ysunadi.
    /// U yerda butun hisobni yuklab tashlash (oylar + jurnal) shunchaki
    /// ruxsatni bilish uchun ortiqcha ish bo'lardi; qoidani ikkinchi marta
    /// yozish esa ikki nusxa demakdir — ular vaqt o'tib ajralib ketardi.
    /// </summary>
    Task EnsureCanViewStudentAsync(
        long studentId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// "Moliya bo'limiga kira oladimi" (<c>Academic</c> yoki <c>Admin</c>) —
    /// YENGIL tekshiruv, ma'lumot yuklamaydi. Ruxsati yo'q bo'lsa 403.
    ///
    /// NIMA UCHUN INTERFEYSDA: yig'ma hisobot ALOHIDA servisda
    /// (<see cref="IPaymentSummaryService"/>), lekin ruxsat qoidasi
    /// MOLIYADA BITTA bo'lishi kerak. Hisobot o'z tekshiruvini yozsa, ikki
    /// nusxa paydo bo'lardi va vaqt o'tib ular ajralib ketardi — masalan
    /// kurator moliyaga qo'shilganda biri yangilanib, ikkinchisi qolardi.
    /// Bu <see cref="EnsureCanViewStudentAsync"/> bilan AYNI naqsh.
    /// </summary>
    Task EnsureCanManageFinanceAsync(long actorId, CancellationToken ct = default);

    /// <summary>To'lovlar jurnali (sahifalangan).</summary>
    Task<PagedResult<PaymentTransactionDto>> ListTransactionsAsync(
        long studentId, int page, int pageSize, long actorId, CancellationToken ct = default);

    /// <summary>Oylik yozuvlar ro'yxati — moliya paneli va qarzdorlar hisoboti.</summary>
    Task<PagedResult<PaymentDto>> ListPaymentsAsync(
        PaymentListQuery query, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- tarif

    Task<IReadOnlyList<TariffDto>> ListTariffsAsync(
        bool? isActive, long actorId, CancellationToken ct = default);

    /// <summary>Guruh uchun AYNAN qaysi tarif tushishini oldindan ko'rsatadi.</summary>
    Task<TariffDto?> ResolveTariffAsync(
        long groupId, DateOnly? onDate, long actorId, CancellationToken ct = default);

    Task<TariffDto> CreateTariffAsync(
        CreateTariffRequest request, long actorId, CancellationToken ct = default);

    /// <summary>★ TO'LIQ ALMASHTIRISH (<c>PUT</c>) — izoh DTO'da.</summary>
    Task<TariffDto> UpdateTariffAsync(
        long id, UpdateTariffRequest request, long actorId, CancellationToken ct = default);

    Task DeleteTariffAsync(long id, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- chegirma

    Task<IReadOnlyList<StudentDiscountDto>> ListDiscountsAsync(
        long studentId, long actorId, CancellationToken ct = default);

    Task<StudentDiscountDto> CreateDiscountAsync(
        long studentId, CreateDiscountRequest request, long actorId, CancellationToken ct = default);

    /// <summary>★ TO'LIQ ALMASHTIRISH (<c>PUT</c>).</summary>
    Task<StudentDiscountDto> UpdateDiscountAsync(
        long studentId, long id, UpdateDiscountRequest request, long actorId,
        CancellationToken ct = default);

    Task DeleteDiscountAsync(
        long studentId, long id, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- sozlama

    Task<FinanceSettingsDto> GetSettingsAsync(long actorId, CancellationToken ct = default);

    Task<FinanceSettingsDto> UpdateSettingsAsync(
        UpdateFinanceSettingsRequest request, long actorId, CancellationToken ct = default);

    /// <summary>Bloklashdan istisno bayrog'ini o'rnatadi (audit bilan).</summary>
    Task<PaymentBlockDto> SetExemptAsync(
        long studentId, SetExemptRequest request, long actorId, CancellationToken ct = default);
}
