using Zinnur.Domain.Enums;

namespace Zinnur.Application.Shop;

/// <summary>Saytdagi tarif kartasi. <paramref name="Features"/> — taklif matnining qatorlari.</summary>
/// <param name="IncludesInteractiveBook">Tarifga interaktiv darslik kiradi (Mustaqil+).</param>
public sealed record ShopPlanDto(
    long Id,
    string Name,
    PlanKind Kind,
    string CourseName,
    int Months,
    decimal Price,
    IReadOnlyList<string> Features,
    bool IncludesInteractiveBook);

/// <summary>Ommaviy tariflar va to'lov yo'riqnomasi (login talab qilinmaydi).</summary>
public sealed record ShopOffersDto(
    IReadOnlyList<ShopPlanDto> Plans,
    string? PaymentInstructions,
    string? PaymeUrl,
    string? ClickUrl,
    string? RahmatUrl = null);

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
    string? DecisionNote,
    DateTimeOffset? PaidClaimedAt);

/// <summary>O'quvchining do'kon holati: qaysi tariflar unga mos va buyurtmalari.</summary>
/// <param name="InGroup">Faol guruh a'zoligi bor — mustaqil o'qish tarifi unga sotilmaydi.</param>
/// <param name="HasActiveSubscription">Hozir mustaqil o'qish obunasi faol.</param>
/// <param name="Balance">O'quvchi balansi (2026-10-29, Q-4) — "Balansdan to'lash" taklifi uchun.</param>
/// <param name="CurrentPlanId">Hozir yurib turgan davrning tarifi (Q-1) — «Joriy tarif» belgisi uchun.</param>
/// <param name="CurrentPlanEndsAt">Hozirgi davr tugash vaqti.</param>
public sealed record ShopMeDto(
    bool InGroup,
    bool HasActiveSubscription,
    IReadOnlyList<ShopOrderDto> Orders,
    decimal Balance = 0m,
    long? CurrentPlanId = null,
    DateTimeOffset? CurrentPlanEndsAt = null);

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
    string? DecisionNote,
    DateTimeOffset? PaidClaimedAt);

public sealed record CreateOrderRequest(long PlanId);

public sealed record DecideOrderRequest(string? Note);
