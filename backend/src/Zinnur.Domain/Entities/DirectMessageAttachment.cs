using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// SHAXSIY YOZISHMAGA (kurator ↔ o'quvchi) BIRIKTIRILGAN FAYL — 2026-08-17
/// ========================================================================
///
/// <see cref="GroupChatAttachment"/> ning AYNI naqshi, faqat
/// <see cref="DirectMessage"/> uchun: talab — "xabarlar panelida fayl/rasm
/// biriktirish ham guruh, ham kurator bilan shaxsiy yozishmada ishlasin".
/// Guruh chatida bu allaqachon bor edi (R16b); bu yerda xuddi o'sha
/// yechim TAKRORLANADI, chunki ikkalasi ham AYNI fayl-turi tekshiruvi,
/// AYNI hajm chegarasi va AYNI "xabar bilan bitta tranzaksiya" qoidasiga
/// muhtoj.
///
/// ★ NIMA UCHUN QAYTA ISHLATILMADI, YANGI JADVAL: <see cref="GroupChatAttachment.MessageId"/>
/// `GroupChatMessage` ga, bu esa `DirectMessage` ga ishora qiladi — ikkala
/// xabar turi ham ALOHIDA jadval (sabab `DirectMessage` izohida), ya'ni
/// biriktirma ham ularning HAR BIRIGA o'z tashqi kaliti bilan bog'lanishi
/// kerak. Bitta jadvalga ikkala tur uchun ikkita nullable tashqi kalit
/// qo'shish "qaysi biri to'ldirilgan?" degan doimiy CHECK talab qilardi.
///
/// ── FARQI GroupChatAttachment'dan ──────────────────────────────────────
///
///   • KANAL YO'Q — shaxsiy yozishma bitta oqim, "Ustoz/Kurator kanali"
///     tushunchasi umuman yo'q.
///   • `ChatRetentionJob` BU YERGA TEGMAYDI (`general.support_contact`
///     bilan bir joyda emas — sozlama izohi: "shaxsiy yozishmalar ...
///     tegilmaydi"). Ya'ni bu yerdagi obyektlar avtomatik SUPURILMAYDI —
///     yagona yo'qolish yo'li `SaveChanges` muvaffaqiyatsiz bo'lganda
///     use-case'ning o'zi ombordan o'chirishi (pastdagi izoh, xabar bilan
///     bitta tranzaksiya qoidasi).
/// </summary>
public class DirectMessageAttachment : BaseEntity
{
    /// <summary>
    /// Bitta xabarga ko'pi bilan shuncha fayl — <see cref="GroupChatAttachment.MaxPerMessage"/>
    /// bilan AYNI (ikkita bir xil ma'noli chegara ikki xil raqam bo'lib qolmasin).
    /// </summary>
    public const int MaxPerMessage = GroupChatAttachment.MaxPerMessage;

    /// <summary>Ko'rinadigan fayl nomi ustunining chegarasi — <see cref="GroupChatAttachment.MaxFileNameLength"/> bilan AYNI.</summary>
    public const int MaxFileNameLength = GroupChatAttachment.MaxFileNameLength;

    public long MessageId { get; set; }

    public DirectMessage? Message { get; set; }

    /// <summary>Fayl turi — MAZMUNDAN aniqlanadi, klient aytganidan emas.</summary>
    public AttachmentKind Kind { get; set; }

    /// <summary>Xabar ichidagi tartib (0 dan, ZICH) — albom shu tartibda chiziladi.</summary>
    public int Position { get; set; }

    /// <summary>
    /// 🔴 OMBOR KALITI — UI'GA CHIQMAYDI. Sabab
    /// <see cref="GroupChatAttachment.ObjectKey"/> da batafsil.
    /// </summary>
    public required string ObjectKey { get; set; }

    /// <summary>MAZMUNDAN aniqlangan MIME turi.</summary>
    public required string ContentType { get; set; }

    /// <summary>
    /// Foydalanuvchiga ko'rsatiladigan nom. Klient bergan nom
    /// <see cref="GroupChatAttachment.SanitizeFileName"/> orqali tozalanadi
    /// — ikkinchi nusxa yozilmadi, TAKRORLANMASIN.
    /// </summary>
    public string? FileName { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Ovoz davomiyligi (bo'lsa) — pleyer uzunlikni oldindan ko'rsatsin.</summary>
    public int? DurationSec { get; set; }

    // ---------------------------------------------------------------- xatti-harakat

    public void Validate()
    {
        if (MessageId <= 0)
            throw new DomainException("Biriktirma xabarga bog'langan bo'lishi kerak.");

        if (string.IsNullOrWhiteSpace(ObjectKey))
            throw new DomainException("Ombor kaliti bo'sh bo'lishi mumkin emas.");

        if (string.IsNullOrWhiteSpace(ContentType))
            throw new DomainException("Fayl turi (MIME) aniqlanmagan.");

        if (SizeBytes <= 0)
            throw new DomainException("Fayl hajmi noldan katta bo'lishi kerak.");

        if (DurationSec is { } duration && duration is < 0 or > LessonAsset.MaxDurationSec)
            throw new DomainException("Davomiylik qiymati haqiqatga to'g'ri kelmaydi.");
    }
}
