using Zinnur.Application.Payments.Dtos;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Payments.Services;

/// <summary>
/// ========================================================================
/// QARZDORLIK DARVOZASI
/// ========================================================================
///
/// Qoida Domain'da (<c>PaymentBlockPolicy</c>), sozlama bazada/konfiguratsiyada
/// (<c>IFinanceSettingsStore</c>), bu servis esa faqat FAKTNI (qarz, istisno)
/// topadi va qoidani chaqiradi.
///
/// NIMA UCHUN ALOHIDA, KICHIK INTERFEYS: uni moliyadan TASHQARIDAGI servislar
/// chaqiradi (jonli darsga kirish, kurs kontenti). Ular butun
/// <see cref="IPaymentService"/> ga bog'lanib qolsa, moliya moduli butun
/// tizimga tarqalardi — hozir esa bog'liqlik ikkita metoddan iborat.
/// </summary>
public interface IPaymentBlockService
{
    /// <summary>
    /// Holatni HISOBLAYDI, lekin istisno KO'TARMAYDI — frontend
    /// ogohlantirish ko'rsatishi va xodim sabab ko'rishi uchun.
    /// </summary>
    Task<PaymentBlockDto> EvaluateAsync(
        long studentId, PaymentBlockScope requested, CancellationToken ct = default);

    /// <summary>
    /// Bloklangan bo'lsa <see cref="Common.Exceptions.ForbiddenException"/>
    /// ko'taradi (HTTP 403). Xabar FOYDALANUVCHIGA ko'rsatiladi: qarz summasi,
    /// chegara va nima qilish kerakligi yoziladi.
    /// </summary>
    Task EnsureAllowedAsync(
        long studentId, PaymentBlockScope requested, CancellationToken ct = default);
}
