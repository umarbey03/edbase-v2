using Zinnur.Domain.Enums;

namespace Zinnur.Application.Shop;

/// <summary>Saytdagi tarif kartasi. <paramref name="Features"/> — taklif matnining qatorlari.</summary>
public sealed record ShopPlanDto(
    long Id,
    string Name,
    PlanKind Kind,
    string CourseName,
    int Months,
    decimal Price,
    IReadOnlyList<string> Features);

/// <summary>Ommaviy tariflar va to'lov yo'riqnomasi (login talab qilinmaydi).</summary>
public sealed record ShopOffersDto(
    IReadOnlyList<ShopPlanDto> Plans,
    string? PaymentInstructions,
    string? PaymeUrl,
    string? ClickUrl);

public sealed record ShopOrderDto(
    long Id,
    string Code,
    long PlanId,
    string PlanName,
    PlanKind Kind,
    string CourseName,
    decimal Amount,
    int Months,
    PurchaseOrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? DecisionNote);

/// <summary>O'quvchining do'kon holati: qaysi tariflar unga mos va buyurtmalari.</summary>
/// <param name="InGroup">Faol guruh a'zoligi bor — mustaqil o'qish tarifi unga sotilmaydi.</param>
/// <param name="HasActiveSubscription">Hozir mustaqil o'qish obunasi faol.</param>
public sealed record ShopMeDto(
    bool InGroup,
    bool HasActiveSubscription,
    IReadOnlyList<ShopOrderDto> Orders);

/// <summary>Xodim ko'radigan buyurtma qatori — o'quvchi kontakti bilan.</summary>
public sealed record StaffOrderDto(
    long Id,
    string Code,
    long StudentId,
    string StudentName,
    string? StudentPhone,
    string? StudentTelegramUsername,
    string PlanName,
    PlanKind Kind,
    string CourseName,
    decimal Amount,
    int Months,
    PurchaseOrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? DecidedByName,
    string? DecisionNote);

public sealed record CreateOrderRequest(long PlanId);

public sealed record DecideOrderRequest(string? Note);
