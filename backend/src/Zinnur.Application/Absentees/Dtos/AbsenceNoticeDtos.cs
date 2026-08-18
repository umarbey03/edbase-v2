namespace Zinnur.Application.Absentees.Dtos;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// KELMAGANLARGA XABAR — YUBORISH VA TARIX (2026-08-18)
/// ════════════════════════════════════════════════════════════════════════
///
/// Loyiha egasi: *"xabarlar qismida darsga kirmagan o'quvchilar uchun
/// yuborilgan xabarlar turishi kerak va u alohida tab bo'lishi kerak"*.
///
/// ★ GURUH EMAS, O'QUVCHI: mavjud `GroupBroadcast` butun guruhga
/// yuboradi va bitta qator butun guruhni ifodalaydi. Kelmaganlar bilan
/// ishlashda esa kurator BITTA o'quvchi bo'yicha ish yuritadi
/// (`AbsenceNotice` izohida batafsil).
/// </summary>
/// <param name="StudentId">Kimga.</param>
/// <param name="SessionId">Qaysi qoldirilgan dars uchun.</param>
public sealed record AbsenceNoticeTarget(long StudentId, long SessionId);

/// <param name="Body">
/// Xabar matni. O'RIN EGALLOVCHILAR qo'llab-quvvatlanadi — ro'yxati
/// <c>AbsenceNoticePlaceholders</c> da.
/// </param>
/// <param name="TemplateId">Qaysi shablondan olingani — faqat tarix uchun.</param>
public sealed record SendAbsenceNoticeRequest(
    IReadOnlyList<AbsenceNoticeTarget> Targets,
    string Body,
    long? TemplateId = null);

/// <param name="Sent">Nechta xabar yozildi.</param>
/// <param name="Queued">Shundan nechtasi Telegram navbatiga tushdi.</param>
/// <param name="WithoutTelegram">
/// Telegrami ulanmagan o'quvchilar soni — ularga qo'ng'iroq qilish kerak.
/// </param>
/// <param name="Skipped">
/// Yuborilmaganlar: dars yoki o'quvchi topilmadi, yoki o'quvchi o'sha
/// darsda ASLIDA qatnashgan (ro'yxat eskirgan bo'lsa).
/// </param>
public sealed record SendAbsenceNoticeResultDto(
    int Sent,
    int Queued,
    int WithoutTelegram,
    int Skipped);

/// <param name="From">Davr boshi (mahalliy, KIRADI). Bo'sh — cheklovsiz.</param>
/// <param name="To">Davr oxiri (mahalliy, KIRADI). Bo'sh — cheklovsiz.</param>
/// <param name="Delivery">
/// Yetkazilish holati bo'yicha: <c>Pending</c>, <c>Sent</c>, <c>Failed</c>
/// yoki <c>NoTelegram</c> (umuman navbatga qo'yilmagan).
/// </param>
public sealed record AbsenceNoticeListQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    long? GroupId = null,
    long? StudentId = null,
    string? Delivery = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

/// <param name="DeliveryStatus">
/// <c>Sent</c> / <c>Pending</c> / <c>Failed</c> / <c>NoTelegram</c>.
///
/// ⚠️ <c>Sent</c> — TELEGRAM QABUL QILDI degani, "o'quvchi o'qidi" EMAS:
/// o'qilganlik belgisi Telegram Bot API'da mavjud emas.
/// </param>
/// <param name="DeliveryError">Xato bo'lsa sababi (bot bloklangan va h.k.).</param>
public sealed record AbsenceNoticeRowDto(
    long Id,
    long StudentId,
    string StudentName,
    string? StudentPhone,
    long GroupId,
    string GroupName,
    long SessionId,
    DateTimeOffset SessionStart,
    string Body,
    string SentByName,
    DateTimeOffset SentAt,
    bool ToTelegram,
    string DeliveryStatus,
    DateTimeOffset? DeliveredAt,
    string? DeliveryError);

/// <summary>Filtrga mos BUTUN to'plam bo'yicha yig'ma (sahifalashdan mustaqil).</summary>
public sealed record AbsenceNoticeSummaryDto(
    int Total,
    int Delivered,
    int Pending,
    int Failed,
    int WithoutTelegram);
