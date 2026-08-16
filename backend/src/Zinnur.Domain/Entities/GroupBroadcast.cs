using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ============================================================================
/// GURUHLARGA XABAR YUBORISH — TARIX YOZUVI (2026-08-16)
/// ============================================================================
///
/// Talab: *"kerakli guruhlarni tanlab ... shablon yoki qo'lda yozgan xabarni
/// telegram bot orqali ham platformadagi guruhlarga ham yubora olishi
/// kerak"*. Bu klass — HAR YUBORISH URINISHINING yozuvi ("Xabarlar" ro'yxati,
/// "kim, qachon, nimani, qaysi guruhlarga yubordi").
///
/// ── NIMA UCHUN GURUHLAR ALOHIDA JADVALDA EMAS, SNAPSHOT MATNDA ─────────────
///
/// <see cref="TargetGroupNames"/> — "ATF-1, Kechki guruh" kabi tayyor matn,
/// guruh Id'lariga ishora qiluvchi child-jadval EMAS. Sabab:
///   1) Bu — TARIX yozuvi, qayta so'rov (masalan "shu guruhga oxirgi
///      qachon xabar ketgan") hozircha kerak emas;
///   2) Guruh keyinroq nomi o'zgartirilsa yoki arxivlansa ham, tarix
///      YUBORILGAN PAYTDAGI nomni ko'rsatishi kerak — child-jadval FK
///      orqali JOIN qilsa, nom ESKI emas, HOZIRGI bo'lib chiqardi
///      (`NotificationRequest.RecipientAddress` dagi AYNI "nega snapshot"
///      mulohazasi).
///
/// ⚠️ <see cref="Body"/> ham SNAPSHOT: shablon asosida yuborilgan bo'lsa ham,
/// shablon keyin o'zgarsa bu yozuv ESKI matnni saqlaydi — "menga bunday
/// xabar kelmagan" degan shikoyatda DALIL shu qator (`NotificationRequest.Body`
/// izohidagi 1-band bilan AYNI mulohaza).
/// </summary>
public class GroupBroadcast : BaseEntity
{
    public const int MaxBodyLength = 4000;
    public const int MaxTargetNamesLength = 1000;

    /// <summary>Xabarni yuborgan xodim (o'quv bo'limi/admin).</summary>
    public long AuthorId { get; set; }

    public User? Author { get; set; }

    /// <summary>Shu shablon asosida yuborilgan bo'lsa — uning Id'si. Qo'lda yozilgan bo'lsa <c>null</c>.</summary>
    public long? TemplateId { get; set; }

    public MessageTemplate? Template { get; set; }

    /// <summary>Yuborilgan matnning O'ZI (snapshot — sinf izohiga qarang).</summary>
    public required string Body { get; set; }

    /// <summary>Nishon guruhlar nomi, vergul bilan (snapshot).</summary>
    public required string TargetGroupNames { get; set; }

    /// <summary>Nechta guruh tanlangani.</summary>
    public int TargetGroupCount { get; set; }

    /// <summary>Telegram orqali yuborildimi (har a'zoga alohida navbat qatori — <c>INotificationOutbox</c>).</summary>
    public bool SentToTelegram { get; set; }

    /// <summary>Platformadagi guruh chatiga (har guruhga bitta xabar) yozildimi.</summary>
    public bool SentToPlatformChat { get; set; }

    /// <summary>Telegram orqali NECHTA odamga navbatga qo'yilgani (bog'lanmaganlar hisobga kirmaydi).</summary>
    public int TelegramRecipientCount { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Body))
            throw new DomainException("Xabar matni kiritilishi shart.");

        if (Body.Length > MaxBodyLength)
            throw new DomainException("Xabar matni juda uzun.");

        if (TargetGroupCount <= 0)
            throw new DomainException("Kamida bitta guruh tanlanishi shart.");

        if (!SentToTelegram && !SentToPlatformChat)
        {
            throw new DomainException(
                "Yuborish kanali tanlanmagan — Telegram yoki platforma chatidan kamida bittasi kerak.");
        }
    }
}
