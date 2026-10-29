using Zinnur.Domain.Enums;

namespace Zinnur.Application.Subscriptions;

public sealed record SubscriptionPlanDto(
    long Id,
    string Name,
    long CourseId,
    string CourseName,
    int Months,
    decimal Price,
    bool IsActive,
    PlanKind Kind,
    string? Description,
    bool IsPublic,
    bool IncludesInteractiveBook);

public sealed record SaveSubscriptionPlanRequest(
    string Name,
    long CourseId,
    int Months,
    decimal Price,
    bool IsActive = true,
    PlanKind Kind = PlanKind.SelfStudy,
    string? Description = null,
    bool IsPublic = false,
    bool IncludesInteractiveBook = false);

/// <summary>Obuna berish yoki uzaytirish (to'lov qo'lda qabul qilingan).</summary>
public sealed record GrantSubscriptionRequest(
    long StudentId,
    long PlanId,
    PaymentMethod Method,
    string? Note);

/// <summary>
/// Obunani bekor qilish (2026-10-29, Q-5: pul bilan nima qilinadi).
/// </summary>
/// <param name="RefundMode">
/// <c>None</c> — pul qaytarilmaydi; <c>ToBalance</c> — o'quvchi balansiga;
/// <c>Cash</c> — naqd qaytarildi (kassadan chiqdi). Har ikkalasi jurnalga yoziladi.
/// </param>
/// <param name="RefundAmount">Qaytariladigan summa — davr summasidan oshmaydi.</param>
public sealed record CancelSubscriptionRequest(
    string? Reason,
    SubscriptionRefundMode RefundMode = SubscriptionRefundMode.None,
    decimal? RefundAmount = null);

/// <param name="Method"><c>null</c> — o'quvchi balansidan to'langan.</param>
/// <param name="IncludesInteractiveBook">Sotilgan paytdagi tarifda darslik bormidi (muzlatilgan).</param>
/// <param name="UnusedValue">Hozirgi holatda ishlatilmagan qiymati (pro-rata) — bekor qilish formasidagi taklif.</param>
public sealed record SubscriptionPeriodDto(
    long Id,
    long StudentId,
    string? PlanName,
    long CourseId,
    string CourseName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal Amount,
    PaymentMethod? Method,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    long? PlanId,
    bool IncludesInteractiveBook,
    decimal UnusedValue = 0m);

/// <summary>"Mening obunam" — o'quvchi profili uchun.</summary>
/// <param name="HasSubscription">Obuna hech qachon bo'lganmi.</param>
/// <param name="IsActive">Hozir kirish huquqi bormi.</param>
/// <param name="DaysLeft">Faol bo'lsa — qolgan to'liq kunlar.</param>
public sealed record MySubscriptionDto(
    bool HasSubscription,
    bool IsActive,
    long? CourseId,
    string? CourseName,
    DateTimeOffset? EndsAt,
    int? DaysLeft,
    IReadOnlyList<SubscriptionPeriodDto> History,
    string? PlanName = null);

/// <summary>
/// Obunani guruhga o'tkazish (2026-10-29, Q-2): qancha kun va qancha pul
/// balansga o'tadi (yoki o'tdi).
/// </summary>
/// <param name="HasLiveSubscription">Hozirgi yoki oldindan to'langan davr bormi.</param>
/// <param name="DaysLeft">Qolgan to'liq kunlar (hamma tirik davrlar bo'yicha).</param>
/// <param name="Amount">Balansga o'tadigan summa (pro-rata, pastga yaxlitlangan).</param>
public sealed record GroupConversionDto(bool HasLiveSubscription, int DaysLeft, decimal Amount);

/// <summary>Obuna ochilgani natijasi — buyurtma havolasi va o'quvchiga xabar uchun.</summary>
/// <param name="TransactionId">Pul jurnaliga yozilgan yozuv (summa 0 bo'lsa <c>null</c>).</param>
/// <param name="Credit">Tarif almashtirilganda eski davrdan balansga o'tgan summa.</param>
public sealed record SubscriptionGrantResult(
    long PeriodId,
    long? TransactionId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal Credit);
