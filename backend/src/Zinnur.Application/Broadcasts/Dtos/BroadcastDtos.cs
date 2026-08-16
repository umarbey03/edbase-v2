namespace Zinnur.Application.Broadcasts.Dtos;

/* ============================================================================
   XABAR SHABLONLARI (Sozlamalar panelidan boshqariladigan lug'at)
   ============================================================================ */

public sealed record MessageTemplateDto(
    long Id,
    string Name,
    string Body,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateMessageTemplateRequest(string Name, string Body, bool IsActive = true);

public sealed record UpdateMessageTemplateRequest(string Name, string Body, bool IsActive);

/// <summary>Ro'yxat filtri. <c>null</c> — filtrlanmaydi (Sozlamalar panelida arxivlangani ham ko'rinadi).</summary>
public sealed record MessageTemplateListQuery(bool? IsActive = null);

/* ============================================================================
   GURUHLARGA XABAR YUBORISH
   ============================================================================ */

/// <summary>
/// Yuborish so'rovi.
/// </summary>
/// <param name="GroupIds">Nishon guruhlar — kamida bittasi.</param>
/// <param name="TemplateId">
/// Shablondan foydalanilsa uning Id'si. <c>Body</c> BARIBIR MAJBURIY
/// (chaqiruvchi shablon matnini oldindan o'qib, kerak bo'lsa tahrirlab
/// yuboradi) — server shablonni QAYTA o'qib matnni "tiklamaydi", aks holda
/// xodim shablonni o'zgartirib yuborsa, u yozganidan BOSHQA matn ketardi.
/// </param>
/// <param name="Body">Yuboriladigan matnning O'ZI.</param>
/// <param name="SendToTelegram">Har guruh a'zosiga Telegram DM (navbat orqali).</param>
/// <param name="SendToPlatformChat">Guruhning platformadagi chatiga (Ustoz oqimi) yozish.</param>
public sealed record SendGroupBroadcastRequest(
    IReadOnlyList<long> GroupIds,
    string Body,
    long? TemplateId,
    bool SendToTelegram,
    bool SendToPlatformChat);

public sealed record GroupBroadcastDto(
    long Id,
    long AuthorId,
    string AuthorName,
    long? TemplateId,
    string? TemplateName,
    string Body,
    string TargetGroupNames,
    int TargetGroupCount,
    bool SentToTelegram,
    bool SentToPlatformChat,
    int TelegramRecipientCount,
    DateTimeOffset CreatedAt);

/// <summary>Sahifalangan tarix ro'yxati filtri.</summary>
public sealed record GroupBroadcastListQuery(int Page = 1, int PageSize = 20);
