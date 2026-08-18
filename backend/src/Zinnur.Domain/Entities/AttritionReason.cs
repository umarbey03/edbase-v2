using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TO'KILISH SABABI — TANLANADIGAN RO'YXAT (2026-08-18)
/// ════════════════════════════════════════════════════════════════════════
///
/// O'quv bo'limi talabi (Dilrabo, 2026-08-18): *"To'kilish sabablarini
/// foizda qilib berishni iloji bormi?"*.
///
/// ★ NIMA UCHUN ERKIN MATN YETARLI EMAS: <c>GroupMembershipEvent.Reason</c>
/// — operator qo'lda yozadigan matn. "Moliyaviy", "pul yo'q", "to'lay
/// olmadi" — bir xil sabab, lekin uchta HAR XIL satr. Ular bo'yicha foiz
/// hisoblansa, eng katta ulush har doim "boshqa" bo'lib chiqardi va
/// hisobot hech narsa aytmasdi. Shuning uchun sabab RO'YXATDAN tanlanadi,
/// erkin matn esa QO'SHIMCHA izoh bo'lib qoladi.
///
/// ★ RO'YXAT SOZLANADI, KOD ICHIDA EMAS: markaz vaqt o'tib yangi sabab
/// qo'shadi ("ustozdan norozi", "ko'chib ketdi"). Enum bo'lsa, har safar
/// dastur qayta yozilishi kerak bo'lardi (<see cref="PenaltyCategory"/>
/// dagi AYNI mulohaza).
///
/// ★ O'CHIRILMAYDI, ARXIVLANADI: hodisa jurnali FAQAT QO'SHILADI va unga
/// havola qiladi. Qator yo'qolsa, o'tgan oyning hisoboti "nomsiz" ulushga
/// aylanardi.
/// </summary>
public class AttritionReason : BaseEntity
{
    public const int MaxLabelLength = 100;

    /// <summary>Ko'rinadigan nomi — "Moliyaviy qiyinchilik", "Vaqt mos kelmadi".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Arxivlangan sabab YANGI hodisada tanlanmaydi, lekin eski
    /// yozuvlarda va hisobotda nomi ko'rinib turaveradi.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Nom bo'sh bo'lmasin va chegaradan oshmasin.</summary>
    public void Apply(string label)
    {
        var trimmed = (label ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            throw new DomainException("Sabab nomini kiriting.");

        Label = trimmed.Length > MaxLabelLength ? trimmed[..MaxLabelLength] : trimmed;
    }
}
