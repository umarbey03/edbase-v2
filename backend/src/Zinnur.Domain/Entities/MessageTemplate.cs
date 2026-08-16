using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ============================================================================
/// XABAR SHABLONI (2026-08-16) — "Xabarlar" panelidan guruhlarga yuboriladigan
/// tayyor matn
/// ============================================================================
///
/// Talab (loyiha egasi): *"o'quv bo'limi kerakli guruhlarni tanlab u guruhlar
/// uchun shablon qilib yaratib qo'yilgan xabarni ... yubora olishi kerak.
/// xabar shablonlarini sozlamalar qismidan dinamik qilish kerak"*.
///
/// ── NIMA UCHUN JADVAL, KOD IChIDAGI RO'YXAT EMAS ─────────────────────────
///
/// "Dinamik" so'zi ANIQ shuni bildiradi: o'quv bo'limi shablonni o'zi
/// qo'shadi/o'zgartiradi, dasturchi emas. Naqsh `GroupCategory`/
/// `AnalysisCriterion` bilan AYNI — o'quv bo'limi Sozlamalar panelidan
/// boshqaradigan OCHIQ lug'at.
///
/// ⚠️ SHABLON O'ZGARSA, ALLAQACHON YUBORILGAN XABARLARGA TA'SIR QILMAYDI:
/// <c>GroupBroadcast.Body</c> yuborish PAYTIDAGI matnni SNAPSHOT sifatida
/// saqlaydi (`NotificationRequest.Body` bilan AYNI falsafa — sabab o'sha
/// yerdagi izohda). Ya'ni bu klass faqat "keyingi safar nima taklif
/// qilinsin" degan savolga javob beradi.
/// </summary>
public class MessageTemplate : BaseEntity
{
    public const int MaxNameLength = 150;
    public const int MaxBodyLength = 4000;

    /// <summary>Ko'rsatish uchun qisqa nom: "Dars bekor qilindi", "Bayram tabrigi".</summary>
    public required string Name { get; set; }

    /// <summary>Yuboriladigan matnning O'ZI.</summary>
    public required string Body { get; set; }

    /// <summary>
    /// Faolmi. Nofaol shablon "Xabarlar" tanlagichida ko'rinmaydi, lekin
    /// unga asoslangan ESKI <see cref="GroupBroadcast"/> yozuvlari
    /// o'zgarishsiz qoladi (yuqoridagi snapshot izohi).
    /// </summary>
    public bool IsActive { get; set; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new DomainException("Shablon nomi kiritilishi shart.");

        if (Name.Length > MaxNameLength)
            throw new DomainException("Shablon nomi juda uzun.");

        if (string.IsNullOrWhiteSpace(Body))
            throw new DomainException("Shablon matni kiritilishi shart.");

        if (Body.Length > MaxBodyLength)
            throw new DomainException("Shablon matni juda uzun.");
    }
}
