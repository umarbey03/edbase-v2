namespace Zinnur.Domain.Enums;

/// <summary>
/// O'QUV BO'LIMINING DARS SIFATI BO'YICHA XULOSASI (talab R29 / R30).
///
/// ── NIMA UCHUN AYNAN SHU UCHTA QIYMAT ───────────────────────────────────
///
/// Bu ro'yxat o'ylab topilmagan — u ESKI ILOVADAN tiklandi. Eski yozuvlar
/// ro'yxatida har video ustida "Ko'rilmagan / Tasdiqlandi / Muammo bor"
/// nishoni turardi (v2 ga ko'chirilmagan sabab: backendda maydon ham,
/// endpoint ham yo'q edi — <c>RecordingCard.vue</c> izohi). Xodimlar shu
/// uch holatga o'rgangan, shuning uchun to'rtinchi holat O'YLAB
/// TOPILMADI.
///
/// ★ "KO'RILMAGAN" IKKI XIL YO'L BILAN IFODALANADI VA BU ATAYLAB:
///
///   • QATOR UMUMAN YO'Q — darsga hali hech kim qaramagan. Bu ENG
///     KO'P uchraydigan holat va u uchun bazada qator saqlash ma'nosiz
///     bo'lardi (har dars uchun bittadan bo'sh qator).
///   • QATOR BOR, lekin <see cref="NotReviewed"/> — xodim tahlilni
///     YOZDI, lekin xulosani hali chiqarmadi (qoralama). Busiz yarim
///     yozilgan tahlilni saqlashning yo'li yo'q edi: xodim "Tasdiqlandi"
///     yoki "Muammo bor" dan birini TANLASHGA majbur bo'lardi va
///     ikkalasi ham yolg'on bo'lardi.
///
/// Ikkalasi ham interfeysda AYNI nishonni beradi ("Ko'rilmagan") —
/// ustoz uchun farqi yo'q, o'quv bo'limi esa farqni ro'yxatda emas,
/// o'z oynasida ko'radi.
///
/// ⚠️ RAQAMLAR BAZAGA YOZILADI (loyihaning umumiy uslubi: enum -> int).
/// Yangi qiymat FAQAT oxiriga qo'shiladi, mavjudlari hech qachon
/// o'zgartirilmaydi.
///
/// ★ <see cref="NotReviewed"/> = 0 BO'LISHI SHART: u C# ning default
/// qiymati, ya'ni xulosasiz yaratilgan tahlil o'z-o'zidan qoralama
/// bo'ladi. Agar 0 da "Tasdiqlandi" tursa, xulosani unutgan xodim
/// darsni JIMGINA tasdiqlab qo'yardi.
/// </summary>
public enum SessionReviewVerdict
{
    /// <summary>Tahlil yozildi, lekin yakuniy xulosa chiqarilmadi (qoralama).</summary>
    NotReviewed = 0,

    /// <summary>Dars sifat talablariga javob beradi.</summary>
    Approved = 1,

    /// <summary>Darsda muammo bor — ustoz bilan ishlash kerak.</summary>
    HasIssue = 2,
}
