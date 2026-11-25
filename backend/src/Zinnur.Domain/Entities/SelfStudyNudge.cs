using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// MUSTAQIL O'QUVCHIGA YUBORILGAN BITTA FAOLSIZLIK ESLATMASI (2026-11-25)
/// ════════════════════════════════════════════════════════════════════════
///
/// Mustaqil tarifda dars jadvali yo'q — o'quvchi o'zi to'xtab qolsa, buni
/// hech kim sezmaydi. Eslatmalar zanjiri (1/2/3/5/14-kun) shu jadvalga
/// yoziladi va UCH vazifani bajaradi:
///   • IDEMPOTENTLIK — bir bosqich bir faolsizlik davri uchun bir marta
///     (<c>(StudentId, Stage, AnchorAt)</c> unikal);
///   • TUGMA JAVOBI — 5-kungi savolga o'quvchi nima degani;
///   • O'LCHOV — qaysi matn varianti o'quvchini 48 soat ichida qaytardi.
///
/// ★ <see cref="AnchorAt"/> — o'quvchining OXIRGI faolligi (yoki obuna
/// boshlanishi). O'quvchi qaytib dars qilsa, anchor o'zgaradi va zanjir
/// yangidan boshlanadi — eski bosqichlar yangi davrga ta'sir qilmaydi.
/// </summary>
public class SelfStudyNudge : BaseEntity
{
    /// <summary>Eslatma kim haqida (o'quvchi).</summary>
    public long StudentId { get; set; }

    public User? Student { get; set; }

    /// <summary>
    /// Kimga yuborildi: odatda o'quvchining o'zi, <see cref="NudgeStage.CuratorAlert"/>
    /// da esa kurator. <c>null</c> — kurator biriktirilmagan, xabar hech kimga
    /// ketmadi (bosqich baribir yoziladi, aks holda zanjir shu joyda har kuni
    /// qayta urinardi).
    /// </summary>
    public long? RecipientId { get; set; }

    public NudgeStage Stage { get; set; }

    /// <summary>Faolsizlik davri boshlangan nuqta (oxirgi faollik).</summary>
    public DateTimeOffset AnchorAt { get; set; }

    /// <summary>Matn varianti (0, 1, ...) — qaysi matn yaxshiroq qaytarishini o'lchash uchun.</summary>
    public int Variant { get; set; }

    public DateTimeOffset SentAt { get; set; }

    /// <summary>Telegram orqali ketdimi (aks holda — faqat ilova ichidagi qo'ng'iroqcha).</summary>
    public bool ToTelegram { get; set; }

    public NudgeResponse? Response { get; private set; }

    public DateTimeOffset? RespondedAt { get; private set; }

    public static SelfStudyNudge Create(
        long studentId,
        long? recipientId,
        NudgeStage stage,
        DateTimeOffset anchorAt,
        int variant,
        bool toTelegram,
        DateTimeOffset now) =>
        new()
        {
            StudentId = studentId,
            RecipientId = recipientId,
            Stage = stage,
            AnchorAt = anchorAt,
            Variant = variant,
            ToTelegram = toTelegram,
            SentAt = now,
            CreatedAt = now,
        };

    /// <summary>
    /// Tugma javobini yozadi. Birinchi javob saqlanadi; takroriy bosish
    /// (Telegram'da tugma xabarda qoladi) <c>false</c> qaytaradi.
    /// </summary>
    public bool Respond(NudgeResponse response, DateTimeOffset now)
    {
        if (Response is not null) return false;

        Response = response;
        RespondedAt = now;
        UpdatedAt = now;
        return true;
    }
}
