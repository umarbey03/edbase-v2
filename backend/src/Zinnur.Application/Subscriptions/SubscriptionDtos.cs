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

public sealed record CancelSubscriptionRequest(string? Reason);

public sealed record SubscriptionPeriodDto(
    long Id,
    long StudentId,
    string? PlanName,
    long CourseId,
    string CourseName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal Amount,
    PaymentMethod Method,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason);

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
    IReadOnlyList<SubscriptionPeriodDto> History);
