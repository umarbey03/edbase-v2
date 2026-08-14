using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// DARS BAHOSINI O'ZGARTIRISH IZI: KIM, QACHON, NIMADAN-NIMAGA
/// ========================================================================
///
/// <see cref="AttendanceAudit"/> ning bir xil maqsadli qarindoshi, faqat
/// o'zgaradigan qiymat — holat emas, BALL.
///
/// NIMA UCHUN KERAK: dars bahosi reytingga kiradi va ota-onaga
/// ko'rsatiladi. "Nega mening bolamda 5 turgan edi, endi 3?" — bu savol
/// albatta chiqadi. Qatorning o'zida faqat OXIRGI qaror bor
/// (<c>GradedById</c> / <c>GradedAt</c>), ya'ni izsiz o'zgarish sukut
/// bo'yicha to'liq mumkin bo'lardi.
///
/// NIMA UCHUN ALOHIDA JADVAL: bitta baho bir necha marta tuzatilishi
/// mumkin (ustoz 3 qo'yadi, o'quvchi ishni ko'rsatadi, ustoz 5 ga
/// o'zgartiradi, o'quv bo'limi 4 ga tushiradi). Qatorda ustun bo'lsa faqat
/// oxirgisi qolardi va nizoning sababi — O'RTADAGI qadam — yo'qolardi.
///
/// ── 🔴 `AttendanceAudit` DAN YAGONA TUZILISH FARQI ─────────────────────
///
/// Bu yerda BAHO QATORIGA FK YO'Q (u yerda <c>AttendanceId</c> bor).
/// Sabab: davomat qatori HECH QACHON o'chirilmaydi — u yaratiladi va
/// tuzatiladi. Dars bahosi esa O'CHIRILADI (adashib boshqa o'quvchiga
/// qo'yilgan bahoni olib tashlash — haqiqiy stsenariy).
///
/// FK bo'lganda ikkala yo'l ham yomon edi:
///   • <c>Cascade</c> — baho o'chirilishi bilan uning BUTUN tarixi ham
///     o'chardi, ya'ni "izsiz yo'qotish" aynan audit to'sishi kerak
///     bo'lgan holat sukut bo'yicha ishlab turardi;
///   • <c>SET NULL</c> — iz qolardi, lekin darsni o'chirish MUMKIN
///     BO'LMASDI: qolgan audit qatorlari <c>SessionId</c> orqali darsni
///     ushlab turardi.
///
/// ★ YECHIM: iz DARSGA va O'QUVCHIGA bog'lanadi (ikkalasi ham quyida
/// allaqachon bor va indekslangan). Baho qatori o'chsa iz QOLADI; dars
/// o'chsa iz ham ketadi (<c>SessionId</c> — Cascade, YAGONA cascade yo'l).
///
/// ★ FAQAT QO'SHILADI VA O'QILADI: yozuv yaratilgandan keyin hech qachon
/// yangilanmaydi va o'chirilmaydi.
/// </summary>
public class LessonGradeAudit : BaseEntity
{
    /// <summary>Qaysi darsning bahosi o'zgardi.</summary>
    public long SessionId { get; set; }

    /// <summary>Kimning bahosi o'zgardi.</summary>
    public long StudentId { get; set; }

    /// <summary>Bahoni qo'ygan/o'zgartirgan/o'chirgan xodim.</summary>
    public long ActorId { get; set; }

    /// <summary>
    /// Xodim — YAGONA navigatsiya: baho oynasida "qo'ydi: Aziz Karimov"
    /// yozuvi chiqadi, ya'ni ISM har o'qishda kerak. Dars va o'quvchi esa
    /// chaqiruvchida allaqachon ma'lum — ular uchun navigatsiya faqat
    /// ortiqcha <c>JOIN</c> imkoniyati bo'lardi.
    /// </summary>
    public User? Actor { get; set; }

    /// <summary>
    /// Oldingi ball. <c>null</c> — baho SHU amalda BIRINCHI marta qo'yildi
    /// (qator yo'q edi). "Bahosi yo'q edi" bilan "bahosi 0 edi" —
    /// mutlaqo boshqa ikki holat.
    /// </summary>
    public decimal? OldScore { get; set; }

    /// <summary>
    /// Yangi ball. <c>null</c> — baho O'CHIRILDI.
    ///
    /// ★ O'CHIRISH "0 QO'YISH" EMAS: 0 — reytingga to'liq kiradigan
    /// HAQIQIY baho, o'chirilgan baho esa umuman hisobga olinmaydi.
    /// Shu farq bo'lmasa, adashib qo'yilgan bahoni tuzatishning yagona
    /// yo'li o'quvchiga 0 yozib qo'yish bo'lardi.
    /// </summary>
    public decimal? NewScore { get; set; }

    /// <summary>
    /// Oldingi maxraj. Ball bilan BIRGA saqlanadi: "3" ning ma'nosi
    /// shkalasiz o'qilmaydi (3/5 va 3/100 — boshqa-boshqa natija).
    /// </summary>
    public decimal? OldMaxScore { get; set; }

    public decimal? NewMaxScore { get; set; }

    public string? OldComment { get; set; }

    public string? NewComment { get; set; }
}
