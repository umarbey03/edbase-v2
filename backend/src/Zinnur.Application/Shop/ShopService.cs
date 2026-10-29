using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Payments.Dtos;
using Zinnur.Application.Payments.Services;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Application.Subscriptions;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Application.Shop;

public interface IShopService
{
    /// <summary>Saytdagi tariflar — login talab qilinmaydi.</summary>
    Task<ShopOffersDto> GetOffersAsync(CancellationToken ct = default);

    Task<ShopMeDto> GetMeAsync(long studentId, CancellationToken ct = default);

    Task<ShopOrderDto> CreateOrderAsync(long studentId, CreateOrderRequest request, CancellationToken ct = default);

    Task<ShopOrderDto> CancelOrderAsync(long studentId, long orderId, CancellationToken ct = default);

    Task<IReadOnlyList<StaffOrderDto>> ListOrdersAsync(
        long actorId, PurchaseOrderStatus? status, CancellationToken ct = default);

    Task<StaffOrderDto> ConfirmAsync(long orderId, DecideOrderRequest request, long actorId, CancellationToken ct = default);

    Task<StaffOrderDto> RejectAsync(long orderId, DecideOrderRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Xodimlar guruhidagi tugma (<c>so:ok:{id}</c> / <c>so:no:{id}</c>).
    /// Bizga tegishli bo'lmagan callback — <c>null</c>; aks holda toast matni.
    /// </summary>
    Task<string?> HandleCallbackAsync(long telegramUserId, string? data, CancellationToken ct = default);
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// ONLAYN SOTUV (2026-10-29)
/// ════════════════════════════════════════════════════════════════════════
///
/// O'quvchi saytda tarif tanlaydi → buyurtma kodi oladi → Payme/Click'da
/// to'laydi → xodim to'lovni ko'rib tasdiqlaydi (Telegram tugmasi yoki
/// panel). Tasdiqlanganda:
///   • mustaqil o'qish tarifi — obuna ochiladi (<see cref="ISubscriptionService"/>);
///   • guruh tarifi — pul o'quvchi balansiga yoziladi
///     (<see cref="IPaymentService.RecordPaymentAsync"/>, yagona to'lov yo'li);
///     guruhga joylagach oy ochilganda balansdan avtomatik yopiladi.
///
/// ★ IKKI MARTA TASDIQLAB BO'LMAYDI: holat avval SHARTLI yangilanadi
///   (<c>WHERE Status = Pending</c>) — ikki kassir bir vaqtda bossa ham
///   faqat bittasi "yutadi" va pul bir marta yoziladi. Keyingi qadam
///   yiqilsa holat qaytariladi.
/// ★ NARX SERVERDA: buyurtma summasi tarifdan olinadi, mijozdan emas.
/// </summary>
public sealed class ShopService(
    IApplicationDbContext db,
    ISubscriptionService subscriptions,
    ISubscriptionAccess subscriptionAccess,
    IPaymentService payments,
    ISettingsResolver settings,
    INotificationOutbox outbox,
    TimeProvider clock,
    ILogger<ShopService> logger) : IShopService
{
    /// <summary>Bir o'quvchida bir vaqtda ochiq buyurtmalar chegarasi — spamga qarshi.</summary>
    private const int MaxPendingPerStudent = 3;

    private const int MaxListRows = 300;

    private const string CallbackPrefix = "so:";

    // ================================================================ ommaviy

    public async Task<ShopOffersDto> GetOffersAsync(CancellationToken ct = default)
    {
        var plans = await db.SubscriptionPlans.AsNoTracking()
            .Where(p => p.IsActive && p.IsPublic && p.Course!.IsActive)
            .OrderBy(p => p.Kind)
            .ThenBy(p => p.Price)
            .Select(p => new { p.Id, p.Name, p.Kind, CourseName = p.Course!.Name, p.Months, p.Price, p.Description })
            .ToListAsync(ct);

        var values = await settings.ResolveManyAsync(
            [Definition(SettingsRegistry.Keys.SalesPaymentInstructions),
             Definition(SettingsRegistry.Keys.SalesPaymeUrl),
             Definition(SettingsRegistry.Keys.SalesClickUrl)],
            ct);

        return new ShopOffersDto(
            plans.ConvertAll(p => new ShopPlanDto(
                p.Id, p.Name, p.Kind, p.CourseName, p.Months, p.Price, Features(p.Description))),
            Blank(values[0].Value),
            Blank(values[1].Value),
            Blank(values[2].Value));
    }

    // ================================================================ o'quvchi

    public async Task<ShopMeDto> GetMeAsync(long studentId, CancellationToken ct = default)
    {
        var inGroup = await InGroupAsync(studentId, ct);
        var hasActive = await subscriptionAccess.HasActiveAsync(studentId, ct);

        var orders = await OrdersQuery(db.PurchaseOrders.AsNoTracking().Where(o => o.StudentId == studentId))
            .Take(50)
            .ToListAsync(ct);

        return new ShopMeDto(inGroup, hasActive, orders);
    }

    public async Task<ShopOrderDto> CreateOrderAsync(
        long studentId, CreateOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var student = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId, ct)
            ?? throw new NotFoundException(nameof(User), studentId);

        if (student.Role != UserRole.Student || !student.IsActive)
            throw new ForbiddenException("Tarifni faqat faol o'quvchi sotib oladi.");

        var plan = await db.SubscriptionPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive && p.IsPublic, ct)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), request.PlanId);

        if (plan.Kind == PlanKind.SelfStudy)
        {
            // Guruh va obuna bir-birini taqiqlaydi (sabab `ISubscriptionAccess` izohida) —
            // bu yerda AYTIB qo'yiladi, to'lovdan OLDIN, aks holda pul tushib, obuna ochilmasdi.
            if (await InGroupAsync(studentId, ct))
            {
                throw new ConflictException(
                    "Siz guruhda o'qiyapsiz — mustaqil o'qish tarifi faqat guruhsiz o'quvchilar uchun.");
            }

            var state = await subscriptionAccess.GetStateAsync(studentId, ct);
            if (state is { IsActive: true } && state.CourseId != plan.CourseId)
            {
                throw new ConflictException(
                    "Sizda boshqa kursga faol obuna bor. Bir vaqtda bitta kurs o'qiladi.");
            }
        }

        var pending = await db.PurchaseOrders.AsNoTracking()
            .Where(o => o.StudentId == studentId && o.Status == PurchaseOrderStatus.Pending)
            .Select(o => new { o.Id, o.PlanId })
            .ToListAsync(ct);

        // Ayni tarifga ochiq buyurtma bor — yangisi ochilmaydi (ikki marta bosish, sahifani yangilash).
        var same = pending.Find(o => o.PlanId == plan.Id);
        if (same is not null)
            return await OrderDtoAsync(same.Id, ct);

        if (pending.Count >= MaxPendingPerStudent)
        {
            throw new ConflictException(
                "Sizda to'lovi kutilayotgan buyurtmalar ko'p. Avval ularni to'lang yoki bekor qiling.");
        }

        var order = new PurchaseOrder
        {
            Code = await NewCodeAsync(ct),
            StudentId = studentId,
            PlanId = plan.Id,
            Kind = plan.Kind,
            CourseId = plan.CourseId,
            Amount = plan.Price,
            Months = plan.Months,
            CreatedAt = clock.GetUtcNow(),
        };

        if (order.Amount <= 0)
            throw new ConflictException("Bu tarif sotuvda emas.");

        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync(ct);

        await NotifyStaffAboutNewAsync(order, student, plan, ct);

        ShopLog.OrderCreated(logger, order.Id, studentId, plan.Id);

        return await OrderDtoAsync(order.Id, ct);
    }

    public async Task<ShopOrderDto> CancelOrderAsync(long studentId, long orderId, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders.AsTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.StudentId == studentId, ct)
            ?? throw new NotFoundException(nameof(PurchaseOrder), orderId);

        try
        {
            order.Cancel(clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            throw new ConflictException(ex.Message);
        }

        await db.SaveChangesAsync(ct);
        return await OrderDtoAsync(order.Id, ct);
    }

    // ================================================================ xodim

    public async Task<IReadOnlyList<StaffOrderDto>> ListOrdersAsync(
        long actorId, PurchaseOrderStatus? status, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);

        var query = db.PurchaseOrders.AsNoTracking();
        if (status is { } s)
            query = query.Where(o => o.Status == s);

        return await StaffQuery(query).Take(MaxListRows).ToListAsync(ct);
    }

    public async Task<StaffOrderDto> ConfirmAsync(
        long orderId, DecideOrderRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await EnsureManagerAsync(actorId, ct);

        var order = await ClaimAsync(orderId, PurchaseOrderStatus.Confirmed, actor.Id, request.Note, ct);
        var planName = await PlanNameAsync(order.PlanId, ct);

        try
        {
            var note = string.Create(CultureInfo.InvariantCulture, $"Onlayn buyurtma {order.Code}");

            if (order.Kind == PlanKind.SelfStudy)
            {
                var periodId = await subscriptions.GrantForOrderAsync(
                    order.StudentId, order.PlanId, order.Amount, note, actor.Id, ct);

                await db.PurchaseOrders.Where(o => o.Id == order.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(o => o.SubscriptionPeriodId, periodId), ct);
            }
            else
            {
                var receipt = await payments.RecordPaymentAsync(
                    new RecordPaymentRequest(order.StudentId, order.Amount, PaymentMethod.Card, Note: note),
                    actor.Id, ct);

                await db.PurchaseOrders.Where(o => o.Id == order.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(o => o.PaymentTransactionId, receipt.TransactionId), ct);
            }
        }
        catch
        {
            // Obuna yoki to'lov yozilmadi — buyurtma yana "kutilmoqda" bo'ladi,
            // aks holda u "tasdiqlangan" bo'lib qolib, o'quvchi hech narsa olmasdi.
            await ReleaseAsync(order.Id, ct);
            throw;
        }

        await NotifyDecisionAsync(order, planName, confirmed: true, actor.FullName, request.Note, ct);
        ShopLog.OrderConfirmed(logger, order.Id, actor.Id);

        return await StaffDtoAsync(order.Id, ct);
    }

    public async Task<StaffOrderDto> RejectAsync(
        long orderId, DecideOrderRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await EnsureManagerAsync(actorId, ct);

        var order = await ClaimAsync(orderId, PurchaseOrderStatus.Rejected, actor.Id, request.Note, ct);
        var planName = await PlanNameAsync(order.PlanId, ct);

        await NotifyDecisionAsync(order, planName, confirmed: false, actor.FullName, request.Note, ct);
        ShopLog.OrderRejected(logger, order.Id, actor.Id);

        return await StaffDtoAsync(order.Id, ct);
    }

    public async Task<string?> HandleCallbackAsync(long telegramUserId, string? data, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(data) || !data.StartsWith(CallbackPrefix, StringComparison.Ordinal))
            return null;

        var parts = data.Split(':');
        if (parts.Length != 3
            || parts[1] is not ("ok" or "no")
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var orderId))
        {
            return null;
        }

        // 🔴 KIM BOSDI — Telegram'ning o'zi aytadi (`from.id`). `callback_data` ochiq
        //    matn, guruhdagi har kim tugmani bosa oladi: faqat botga ulangan
        //    administrator yoki o'quv bo'limi xodimi o'tadi.
        var actor = await db.Users.AsNoTracking()
            .Where(u => u.TelegramId == telegramUserId && u.IsActive
                     && (u.Role == UserRole.Admin || u.Role == UserRole.Academic))
            .Select(u => new { u.Id })
            .FirstOrDefaultAsync(ct);

        if (actor is null)
            return "Buyurtmani faqat administrator yoki o'quv bo'limi tasdiqlaydi.";

        try
        {
            var result = parts[1] == "ok"
                ? await ConfirmAsync(orderId, new DecideOrderRequest(null), actor.Id, ct)
                : await RejectAsync(orderId, new DecideOrderRequest("To'lov topilmadi"), actor.Id, ct);

            return result.Status == PurchaseOrderStatus.Confirmed
                ? $"✅ {result.Code} tasdiqlandi"
                : $"❌ {result.Code} rad etildi";
        }
        catch (ConflictException ex)
        {
            return Clip(ex.Message, 190);
        }
        catch (NotFoundException)
        {
            return "Buyurtma topilmadi.";
        }
        catch (ValidationException ex)
        {
            return Clip(ex.Errors.Values.SelectMany(v => v).FirstOrDefault() ?? "Tasdiqlab bo'lmadi.", 190);
        }
    }

    // ================================================================ ichki

    /// <summary>
    /// Holatni SHARTLI o'zgartiradi: faqat hali kutilayotgan buyurtma. Yutqazgan
    /// chaqiruv (boshqa xodim ulgurgan) — <see cref="ConflictException"/>.
    /// </summary>
    private async Task<PurchaseOrder> ClaimAsync(
        long orderId, PurchaseOrderStatus status, long actorId, string? note, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var clipped = Clip(note, PurchaseOrder.MaxNoteLength);

        var claimed = await db.PurchaseOrders
            .Where(o => o.Id == orderId && o.Status == PurchaseOrderStatus.Pending)
            .ExecuteUpdateAsync(u => u
                .SetProperty(o => o.Status, status)
                .SetProperty(o => o.DecidedById, actorId)
                .SetProperty(o => o.DecidedAt, now)
                .SetProperty(o => o.UpdatedAt, now)
                .SetProperty(o => o.DecisionNote, clipped), ct);

        var order = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException(nameof(PurchaseOrder), orderId);

        if (claimed == 0)
        {
            throw new ConflictException(order.Status switch
            {
                PurchaseOrderStatus.Confirmed => $"{order.Code} allaqachon tasdiqlangan.",
                PurchaseOrderStatus.Rejected => $"{order.Code} allaqachon rad etilgan.",
                _ => $"{order.Code} o'quvchi tomonidan bekor qilingan.",
            });
        }

        return order;
    }

    private Task<int> ReleaseAsync(long orderId, CancellationToken ct) =>
        db.PurchaseOrders
            .Where(o => o.Id == orderId && o.Status == PurchaseOrderStatus.Confirmed
                     && o.SubscriptionPeriodId == null && o.PaymentTransactionId == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(o => o.Status, PurchaseOrderStatus.Pending)
                .SetProperty(o => o.DecidedById, (long?)null)
                .SetProperty(o => o.DecidedAt, (DateTimeOffset?)null)
                .SetProperty(o => o.DecisionNote, (string?)null), CancellationToken.None);

    private async Task NotifyStaffAboutNewAsync(
        PurchaseOrder order, User student, SubscriptionPlan plan, CancellationToken ct)
    {
        var chatId = await StaffChatIdAsync(ct);
        if (chatId is null)
            return;

        var courseName = await db.Courses.AsNoTracking()
            .Where(c => c.Id == plan.CourseId).Select(c => c.Name).FirstAsync(ct);

        await outbox.EnqueueAsync(
            new NotificationRequest
            {
                Channel = NotificationChannel.Telegram,
                RecipientAddress = chatId,
                TemplateKey = TelegramTemplates.ShopOrderStaff,
                Body = TelegramTemplates.ShopOrderStaffText(
                    order.Code, student.FullName, student.Phone, student.TelegramUsername,
                    plan.Name, courseName, order.Amount),
                CallbackData = TelegramTemplates.ShopOrderButtons(order.Id),
                IdempotencyKey = string.Create(CultureInfo.InvariantCulture, $"shop_order:{order.Id}"),
            },
            ct);

        await db.SaveChangesAsync(ct);
    }

    private async Task NotifyDecisionAsync(
        PurchaseOrder order, string planName, bool confirmed, string actorName, string? note, CancellationToken ct)
    {
        var chatId = await StaffChatIdAsync(ct);
        if (chatId is not null)
        {
            await outbox.EnqueueAsync(
                new NotificationRequest
                {
                    Channel = NotificationChannel.Telegram,
                    RecipientAddress = chatId,
                    TemplateKey = TelegramTemplates.ShopOrderDecided,
                    Body = TelegramTemplates.ShopOrderDecidedText(order.Code, confirmed, actorName),
                    IdempotencyKey = string.Create(CultureInfo.InvariantCulture, $"shop_order_decided:{order.Id}"),
                },
                ct);
        }

        var studentChat = await db.Users.AsNoTracking()
            .Where(u => u.Id == order.StudentId)
            .Select(u => u.TelegramId)
            .FirstOrDefaultAsync(ct);

        if (studentChat is { } telegramId)
        {
            await outbox.EnqueueAsync(
                new NotificationRequest
                {
                    Channel = NotificationChannel.Telegram,
                    RecipientUserId = order.StudentId,
                    RecipientAddress = telegramId.ToString(CultureInfo.InvariantCulture),
                    TemplateKey = confirmed ? TelegramTemplates.ShopOrderConfirmed : TelegramTemplates.ShopOrderRejected,
                    Body = confirmed
                        ? TelegramTemplates.ShopOrderConfirmedText(order.Code, planName, order.Kind == PlanKind.SelfStudy)
                        : TelegramTemplates.ShopOrderRejectedText(order.Code, note ?? "To'lov topilmadi"),
                    IdempotencyKey = string.Create(CultureInfo.InvariantCulture, $"shop_order_student:{order.Id}"),
                },
                ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<string?> StaffChatIdAsync(CancellationToken ct)
    {
        var raw = (await settings.ResolveAsync(Definition(SettingsRegistry.Keys.SalesStaffChatId), ct)).Value?.Trim();

        // Faqat butun son (guruh ID'si manfiy) — boshqa narsa Bot API'da 400 berardi.
        return long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id) && id != 0
            ? id.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private async Task<string> NewCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = PurchaseOrder.NewCode();
            if (!await db.PurchaseOrders.AsNoTracking().AnyAsync(o => o.Code == code, ct))
                return code;
        }

        throw new ConflictException("Buyurtma kodini yaratib bo'lmadi — qaytadan urinib ko'ring.");
    }

    private Task<bool> InGroupAsync(long studentId, CancellationToken ct) =>
        db.GroupMembers.AsNoTracking().AnyAsync(
            m => m.StudentId == studentId
                 && (m.Status == MemberStatus.Active || m.Status == MemberStatus.Paused)
                 && m.Group!.Type != GroupType.Curator,
            ct);

    private Task<string> PlanNameAsync(long planId, CancellationToken ct) =>
        db.SubscriptionPlans.AsNoTracking().Where(p => p.Id == planId).Select(p => p.Name).FirstAsync(ct);

    private IQueryable<ShopOrderDto> OrdersQuery(IQueryable<PurchaseOrder> source) =>
        source
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Select(o => new ShopOrderDto(
                o.Id,
                o.Code,
                o.PlanId,
                o.Plan!.Name,
                o.Kind,
                db.Courses.Where(c => c.Id == o.CourseId).Select(c => c.Name).First(),
                o.Amount,
                o.Months,
                o.Status,
                o.CreatedAt,
                o.DecidedAt,
                o.DecisionNote));

    private Task<ShopOrderDto> OrderDtoAsync(long orderId, CancellationToken ct) =>
        OrdersQuery(db.PurchaseOrders.AsNoTracking().Where(o => o.Id == orderId)).FirstAsync(ct);

    private IQueryable<StaffOrderDto> StaffQuery(IQueryable<PurchaseOrder> source) =>
        source
            .OrderBy(o => o.Status == PurchaseOrderStatus.Pending ? 0 : 1)
            .ThenByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Select(o => new StaffOrderDto(
                o.Id,
                o.Code,
                o.StudentId,
                o.Student!.FullName,
                o.Student.Phone,
                o.Student.TelegramUsername,
                o.Plan!.Name,
                o.Kind,
                db.Courses.Where(c => c.Id == o.CourseId).Select(c => c.Name).First(),
                o.Amount,
                o.Months,
                o.Status,
                o.CreatedAt,
                o.DecidedAt,
                db.Users.Where(u => u.Id == o.DecidedById).Select(u => u.FullName).FirstOrDefault(),
                o.DecisionNote));

    private Task<StaffOrderDto> StaffDtoAsync(long orderId, CancellationToken ct) =>
        StaffQuery(db.PurchaseOrders.AsNoTracking().Where(o => o.Id == orderId)).FirstAsync(ct);

    private async Task<User> EnsureManagerAsync(long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive || actor.Role is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Buyurtmalarni faqat o'quv bo'limi yoki administrator boshqaradi.");

        return actor;
    }

    /// <summary>Taklif matnini qatorlarga bo'ladi; bo'sh qator va "•", "-" belgilari tashlanadi.</summary>
    private static List<string> Features(string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? []
            : description
                .Split('\n')
                .Select(line => line.Trim().TrimStart('•', '-', '*', '✓', '✅').Trim())
                .Where(line => line.Length > 0)
                .ToList();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Clip(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static SettingDefinition Definition(string key) =>
        SettingsRegistry.TryGet(key, out var definition)
            ? definition
            : throw new InvalidOperationException($"Sozlama registrda yo'q: {key}");
}

internal static partial class ShopLog
{
    [LoggerMessage(EventId = 6900, Level = LogLevel.Information,
        Message = "Onlayn buyurtma yaratildi: order={OrderId} student={StudentId} plan={PlanId}")]
    internal static partial void OrderCreated(ILogger logger, long orderId, long studentId, long planId);

    [LoggerMessage(EventId = 6901, Level = LogLevel.Information,
        Message = "Onlayn buyurtma tasdiqlandi: order={OrderId} actor={ActorId}")]
    internal static partial void OrderConfirmed(ILogger logger, long orderId, long actorId);

    [LoggerMessage(EventId = 6902, Level = LogLevel.Information,
        Message = "Onlayn buyurtma rad etildi: order={OrderId} actor={ActorId}")]
    internal static partial void OrderRejected(ILogger logger, long orderId, long actorId);

    [LoggerMessage(EventId = 6903, Level = LogLevel.Information,
        Message = "Bot orqali yangi o'quvchi ro'yxatdan o'tdi: user={UserId}")]
    internal static partial void SelfSignup(ILogger logger, long userId);
}
