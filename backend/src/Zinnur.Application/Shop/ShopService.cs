using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Gating.Services;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Payments.Dtos;
using Zinnur.Application.Payments.Services;
using Zinnur.Application.Scheduling.Services;
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

    /// <summary>
    /// Mustaqil o'qish tarifini BALANSDAN to'lash (2026-10-29, Q-4) — xodim
    /// tasdig'isiz, buyurtma tarix uchun darhol «Tasdiqlangan» bo'lib yoziladi.
    /// </summary>
    Task<ShopOrderDto> PayFromBalanceAsync(long studentId, CreateOrderRequest request, CancellationToken ct = default);

    Task<ShopOrderDto> CancelOrderAsync(long studentId, long orderId, CancellationToken ct = default);

    /// <summary>O'quvchi «To'ladim» — xodimlarga tekshirish eslatmasi.</summary>
    Task<ShopOrderDto> ClaimPaidAsync(long studentId, long orderId, CancellationToken ct = default);

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
    IGatingService gating,
    IScheduleTimeZoneProvider timeZone,
    ISettingsResolver settings,
    INotificationOutbox outbox,
    TimeProvider clock,
    ILogger<ShopService> logger) : IShopService
{
    /// <summary>Bir o'quvchida bir vaqtda ochiq buyurtmalar chegarasi — spamga qarshi.</summary>
    private const int MaxPendingPerStudent = 3;

    private const int MaxListRows = 300;

    private const string CallbackPrefix = "so:";

    /// <summary>Balansdan to'langan buyurtmaning izohi — tarixda "kassir tasdiqladi" bilan adashmasin.</summary>
    private const string PaidFromBalanceNote = "Balansdan to'landi";

    // ================================================================ ommaviy

    public async Task<ShopOffersDto> GetOffersAsync(CancellationToken ct = default)
    {
        var plans = await db.SubscriptionPlans.AsNoTracking()
            .Where(p => p.IsActive && p.IsPublic && p.Course!.IsActive)
            .OrderBy(p => p.Kind)
            .ThenBy(p => p.Price)
            .Select(p => new { p.Id, p.Name, p.Kind, CourseName = p.Course!.Name, p.Months, p.Price, p.Description, p.IncludesInteractiveBook })
            .ToListAsync(ct);

        var values = await settings.ResolveManyAsync(
            [Definition(SettingsRegistry.Keys.SalesPaymentInstructions),
             Definition(SettingsRegistry.Keys.SalesPaymeUrl),
             Definition(SettingsRegistry.Keys.SalesClickUrl),
             Definition(SettingsRegistry.Keys.SalesRahmatUrl)],
            ct);

        return new ShopOffersDto(
            plans.ConvertAll(p => new ShopPlanDto(
                p.Id, p.Name, p.Kind, p.CourseName, p.Months, p.Price, Features(p.Description),
                p.IncludesInteractiveBook)),
            Blank(values[0].Value),
            Blank(values[1].Value),
            Blank(values[2].Value),
            Blank(values[3].Value));
    }

    // ================================================================ o'quvchi

    public async Task<ShopMeDto> GetMeAsync(long studentId, CancellationToken ct = default)
    {
        var inGroup = await InGroupAsync(studentId, ct);
        var hasActive = await subscriptionAccess.HasActiveAsync(studentId, ct);

        var orders = await OrdersQuery(db.PurchaseOrders.AsNoTracking().Where(o => o.StudentId == studentId))
            .Take(50)
            .ToListAsync(ct);

        // ★ BALANS (Q-4): tariflar sahifasi "Balansdan to'lash" ni taklif qiladi.
        //   Moliya hisobidagi AYNI qator — alohida hisob-kitob yo'q.
        var balance = await db.StudentAccounts.AsNoTracking()
            .Where(a => a.StudentId == studentId)
            .Select(a => (decimal?)a.Balance)
            .FirstOrDefaultAsync(ct) ?? 0m;

        // ★ JORIY TARIF (Q-1): sahifa uni «Joriy tarif» deb belgilaydi, boshqa
        //   mustaqil tariflarga esa "darhol boshlanadi, qolgani balansga" deydi.
        var now = clock.GetUtcNow();
        var current = await db.SubscriptionPeriods.AsNoTracking()
            .Where(p => p.StudentId == studentId && p.CancelledAt == null && p.StartsAt <= now && p.EndsAt > now)
            .OrderByDescending(p => p.StartsAt)
            .Select(p => new { p.PlanId, p.EndsAt })
            .FirstOrDefaultAsync(ct);

        return new ShopMeDto(inGroup, hasActive, orders, balance, current?.PlanId, current?.EndsAt);
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
            await EnsureSelfStudyAllowedAsync(studentId, plan, ct);

        var pending = await db.PurchaseOrders.AsNoTracking()
            .Where(o => o.StudentId == studentId && o.Status == PurchaseOrderStatus.Pending)
            .Select(o => new { o.Id, o.PlanId, o.Kind })
            .ToListAsync(ct);

        // Ayni tarifga ochiq buyurtma bor — yangisi ochilmaydi (ikki marta bosish, sahifani yangilash).
        var same = pending.Find(o => o.PlanId == plan.Id);
        if (same is not null)
            return await OrderDtoAsync(same.Id, ct);

        // 🔴 MUSTAQIL VA GURUH BUYURTMASI BIR VAQTDA OCHILMAYDI (audit, 2026-10-29):
        //    ikkalasi to'lansa, biri tasdiqlanganda ikkinchisi to'qnashib
        //    "osilib" qolardi. Avval birini yakunlash (yoki bekor qilish) kerak.
        if (pending.Exists(o => o.Kind != plan.Kind))
        {
            throw new ConflictException(
                "Sizda boshqa turdagi to'lanmagan buyurtma bor. Avval uni to'lang yoki bekor qiling.");
        }

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

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// BALANSDAN TO'LASH (2026-10-29, loyiha egasi qarori Q-4)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// 🔴 ILGARI (audit P1-4): guruhdan chiqqan o'quvchining balansida qoldiq
    ///   yotardi, Mustaqil uchun esa u yana Payme orqali to'lardi — ikki marta.
    ///
    /// ★ Xodim tasdig'i KERAK EMAS: pul allaqachon markazda, kassir hech narsa
    ///   qidirmaydi. Yechish, davr va buyurtma BITTA tranzaksiyada.
    /// ★ Qoidalar oddiy buyurtmadagi bilan AYNI: guruhda emas, bir vaqtda
    ///   bitta kurs, boshqa tarifga o'tish darhol (qolgani balansga — Q-1).
    /// ★ Ochiq (to'lanmagan) buyurtma bo'lsa — rad etiladi: o'quvchi uni
    ///   Payme'da ham to'lab qo'ygan bo'lishi mumkin va pul ikki marta olinardi.
    /// </summary>
    public async Task<ShopOrderDto> PayFromBalanceAsync(
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

        if (plan.Kind != PlanKind.SelfStudy)
        {
            throw new ConflictException(
                "Balansdan faqat mustaqil o'qish tarifi to'lanadi — guruh darslari balansdan o'zi yechiladi.");
        }

        if (plan.Price <= 0)
            throw new ConflictException("Bu tarif sotuvda emas.");

        await EnsureSelfStudyAllowedAsync(studentId, plan, ct);

        if (await db.PurchaseOrders.AsNoTracking()
                .AnyAsync(o => o.StudentId == studentId && o.Status == PurchaseOrderStatus.Pending, ct))
        {
            throw new ConflictException(
                "Sizda to'lovi kutilayotgan buyurtma bor. Agar uni to'lagan bo'lsangiz, tasdiqni kuting; "
                + "aks holda avval bekor qiling.");
        }

        var orderId = await db.InTransactionAsync(async token =>
        {
            var now = clock.GetUtcNow();
            var order = new PurchaseOrder
            {
                Code = await NewCodeAsync(token),
                StudentId = studentId,
                PlanId = plan.Id,
                Kind = plan.Kind,
                CourseId = plan.CourseId,
                Amount = plan.Price,
                Months = plan.Months,
                CreatedAt = now,
            };
            order.Confirm(studentId, PaidFromBalanceNote, now);

            db.PurchaseOrders.Add(order);
            await db.SaveChangesAsync(token);

            // Balans yetmasa shu yerda 409 — buyurtma ham qaytariladi (tranzaksiya).
            var grant = await subscriptions.GrantFromBalanceAsync(
                studentId, plan.Id, order.Amount,
                string.Create(CultureInfo.InvariantCulture, $"Onlayn buyurtma {order.Code} (balansdan)"),
                token);

            order.SubscriptionPeriodId = grant.PeriodId;
            order.PaymentTransactionId = grant.TransactionId;
            await db.SaveChangesAsync(token);

            return order.Id;
        }, ct);

        await gating.InvalidateAsync(studentId, ct);
        ShopLog.OrderPaidFromBalance(logger, orderId, studentId, plan.Id);

        return await OrderDtoAsync(orderId, ct);
    }

    /// <summary>
    /// Mustaqil o'qish tarifini olish mumkinmi — oddiy buyurtma ham, balansdan
    /// to'lash ham AYNI qoida bilan (bir joyda yashaydi).
    /// </summary>
    private async Task EnsureSelfStudyAllowedAsync(long studentId, SubscriptionPlan plan, CancellationToken ct)
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

    public async Task<ShopOrderDto> ClaimPaidAsync(long studentId, long orderId, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders.AsTracking()
            .Include(o => o.Student)
            .Include(o => o.Plan)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.StudentId == studentId, ct)
            ?? throw new NotFoundException(nameof(PurchaseOrder), orderId);

        bool first;
        try
        {
            first = order.ClaimPaid(clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            throw new ConflictException(ex.Message);
        }

        if (first)
        {
            await db.SaveChangesAsync(ct);
            await NotifyStaffAboutPaidClaimAsync(order, ct);
        }

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

        // 🔴 BITTA TRANZAKSIYA (audit P1-7, 2026-10-29): band qilish, obuna yoki
        //    to'lov va buyurtmadagi havola — hammasi birga yoziladi yoki birga
        //    qaytadi. Ilgari ular alohida edi: havola yozilishi yiqilsa buyurtma
        //    "kutilmoqda" ga qaytar, davr yoki to'lov esa bazada qolardi —
        //    keyingi "Tasdiqlash" pulni IKKINCHI marta yozardi. Endi xato
        //    bo'lsa baza o'zi qaytaradi (qo'lda "Release" kerak emas).
        var (order, grant) = await db.InTransactionAsync(async token =>
        {
            var claimed = await ClaimAsync(orderId, PurchaseOrderStatus.Confirmed, actor.Id, request.Note, token);
            var note = string.Create(CultureInfo.InvariantCulture, $"Onlayn buyurtma {claimed.Code}");

            // 🔴 TO'LANGAN MUSTAQIL BUYURTMA, O'QUVCHI ESA ALLAQACHON GURUHDA
            //    (audit, 2026-10-29): obuna ochib bo'lmaydi (guruh va obuna bir-
            //    birini taqiqlaydi). Ilgari tasdiq 409 bilan yiqilib, buyurtma
            //    abadiy "kutilmoqda" qolardi — pul olingan, hech narsa berilmagan.
            //    Endi pul o'quvchi BALANSIGA yoziladi (guruh to'lovlariga ketadi).
            var toBalance = claimed.Kind == PlanKind.SelfStudy && await InGroupAsync(claimed.StudentId, token);

            if (claimed.Kind == PlanKind.SelfStudy && !toBalance)
            {
                // Obuna puli jurnalga `Subscription` turida shu ichida yoziladi (Q-5).
                var granted = await subscriptions.GrantForOrderAsync(
                    claimed.StudentId, claimed.PlanId, claimed.Amount, note, actor.Id, token);

                await db.PurchaseOrders.Where(o => o.Id == claimed.Id)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(o => o.SubscriptionPeriodId, granted.PeriodId)
                        .SetProperty(o => o.PaymentTransactionId, granted.TransactionId), token);

                return (claimed, (SubscriptionGrantResult?)granted);
            }

            var receipt = await payments.RecordPaymentAsync(
                new RecordPaymentRequest(
                    claimed.StudentId, claimed.Amount, PaymentMethod.Card,
                    Note: toBalance ? note + " (o'quvchi guruhda — balansga)" : note),
                actor.Id, token);

            await db.PurchaseOrders.Where(o => o.Id == claimed.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(o => o.PaymentTransactionId, receipt.TransactionId), token);

            return (claimed, (SubscriptionGrantResult?)null);
        }, ct);

        await gating.InvalidateAsync(order.StudentId, ct);

        var planName = await PlanNameAsync(order.PlanId, ct);
        await NotifyDecisionAsync(order, planName, confirmed: true, actor.FullName, request.Note, grant, ct);
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

        await NotifyDecisionAsync(order, planName, confirmed: false, actor.FullName, request.Note, grant: null, ct);
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

    /*
      ★ «TO'LADIM» ESLATMASI ham o'sha tugmalar bilan — xodim xabarni topib,
        darhol shu yerdan tasdiqlay oladi. Idempotentlik kaliti alohida:
        birinchi xabar (yangi buyurtma) bilan to'qnashmasin.
    */
    private async Task NotifyStaffAboutPaidClaimAsync(PurchaseOrder order, CancellationToken ct)
    {
        var chatId = await StaffChatIdAsync(ct);
        if (chatId is null)
            return;

        await outbox.EnqueueAsync(
            new NotificationRequest
            {
                Channel = NotificationChannel.Telegram,
                RecipientAddress = chatId,
                TemplateKey = TelegramTemplates.ShopOrderStaff,
                Body = TelegramTemplates.ShopOrderPaidClaimText(
                    order.Code, order.Student!.FullName, order.Plan!.Name, order.Amount),
                CallbackData = TelegramTemplates.ShopOrderButtons(order.Id),
                IdempotencyKey = string.Create(CultureInfo.InvariantCulture, $"shop_order_paid:{order.Id}"),
            },
            ct);

        await db.SaveChangesAsync(ct);
    }

    /// <param name="grant">
    /// Obuna ochilgan bo'lsa — uning natijasi. <c>null</c> bo'lsa pul balansga
    /// tushgan (guruh tarifi yoki guruhdagi o'quvchining mustaqil buyurtmasi):
    /// "darslar ochildi" deyilmaydi, aks holda o'quvchi yo'q narsani qidirardi.
    /// </param>
    private async Task NotifyDecisionAsync(
        PurchaseOrder order, string planName, bool confirmed, string actorName, string? note,
        SubscriptionGrantResult? grant, CancellationToken ct)
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
                        ? TelegramTemplates.ShopOrderConfirmedText(
                            order.Code, planName, grant is not null, StartsLaterOn(grant), grant?.Credit ?? 0m)
                        : TelegramTemplates.ShopOrderRejectedText(order.Code, note ?? "To'lov topilmadi"),
                    IdempotencyKey = string.Create(CultureInfo.InvariantCulture, $"shop_order_student:{order.Id}"),
                },
                ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// ★ KIRISH QACHON BOSHLANADI (audit P1-6, 2026-10-29): ayni tarif uzaytirilsa
    ///   yangi davr joriysining OXIRIDAN boshlanadi — xabar "darslar ochildi"
    ///   desa, o'quvchi yangi imkoniyatni bugun qidirardi. Kechroq boshlansa —
    ///   mahalliy sana (Toshkent), aks holda <c>null</c>.
    /// </summary>
    private string? StartsLaterOn(SubscriptionGrantResult? grant)
    {
        if (grant is null || grant.StartsAt <= clock.GetUtcNow().AddMinutes(1))
            return null;

        return TimeZoneInfo.ConvertTime(grant.StartsAt, timeZone.TimeZone)
            .ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
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
                o.DecisionNote,
                o.PaidClaimedAt));

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
                o.DecisionNote,
                o.PaidClaimedAt));

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

    [LoggerMessage(EventId = 6904, Level = LogLevel.Information,
        Message = "Tarif balansdan to'landi: order={OrderId} student={StudentId} plan={PlanId}")]
    internal static partial void OrderPaidFromBalance(ILogger logger, long orderId, long studentId, long planId);
}
