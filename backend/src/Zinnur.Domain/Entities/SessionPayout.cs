using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ============================================================================
/// USTOZ/KURATOR HAQI YOZUVI (2026-08-16) — BITTA darsning haq SNAPSHOT'i.
/// ============================================================================
///
/// ★ NIMA UCHUN KERAK: <c>PayrollService</c> ilgari HAR SO'ROVDA
/// <c>LiveSessions</c> + <c>Attendances</c> + stavkalar jadvalidan
/// QAYTA HISOBLAR edi — bu <see cref="Payment"/>/<see cref="Tariff"/> dagi
/// "narx TARIXI saqlanadi" tamoyilidan FARQ QILARDI: stavka tahrirlansa
/// yoki o'chirilsa, O'TGAN OY HISOBOTI ham jimgina o'zgarib qolardi.
/// Bu jadval <see cref="LessonCharge"/> bilan AYNI naqsh: dars YAKUNLANGANDA
/// (yoki keyinroq bepul/sababli deb qayta belgilanganda) shu paytdagi
/// stavka bilan hisoblanadi va QOTIB QOLADI — keyingi stavka o'zgarishi
/// bu yozuvga TA'SIR QILMAYDI.
///
/// <c>SessionId</c> — UNIKAL: bitta darsda BITTA host bo'ladi
/// (<see cref="LiveSession.HostId"/>), ya'ni bitta haq yozuvi yetarli.
/// </summary>
public class SessionPayout : BaseEntity
{
    public long SessionId { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }

    public UserRole Role { get; set; }

    /// <summary>
    /// Shu darsda qatnashgan (Absent bo'lmagan) o'quvchilar soni —
    /// <see cref="SessionRate"/>/<see cref="BonusAmount"/> bilan BIRGA
    /// QOTIB QOLADI (pastga qarang): davomat Ended darsda o'zgarmaydi.
    /// </summary>
    public int AttendedStudents { get; set; }

    /// <summary>
    /// ★ FAQAT BIRINCHI YARATILGANDA hisoblanadi (shu daqiqadagi stavka
    /// bilan) va KEYIN HECH QACHON qayta hisoblanmaydi — stavka keyinroq
    /// tahrirlansa/o'chirilsa ham bu qiymat O'ZGARMAYDI. Buni "bepul dars"
    /// deb keyinroq belgilash <see cref="Excluded"/> orqali ifodalanadi,
    /// bu maydonning o'ZI emas — shunda "qancha edi" va "haqiqatda
    /// to'landimi" alohida-alohida ko'rinadi.
    /// </summary>
    public decimal SessionRate { get; set; }

    /// <summary>Qatnashgan o'quvchilar bonusi jami — <see cref="SessionRate"/> bilan AYNI qulflanish qoidasi.</summary>
    public decimal BonusAmount { get; set; }

    /// <summary>
    /// ★ 2026-08-16 — shu darsda haqiqatda qo'llangan dam olish/bayram
    /// ko'paytiruvchisi (<c>PayrollRule.WeekendHolidayMultiplier</c>).
    /// Bir nechta asosiy qoida bo'lsa — ENG KATTASI.
    /// Ustama yo'q bo'lsa <c>1</c>. <see cref="SessionRate"/> ALLAQACHON
    /// shu ko'paytiruvchi bilan hisoblangan — bu maydon faqat SHAFFOFLIK
    /// uchun (detail ko'rinishida "nega bu kunning stavkasi boshqacha"
    /// savoliga javob).
    /// </summary>
    public decimal PremiumMultiplierApplied { get; set; } = 1m;

    /// <summary>
    /// Mos qoida topilmaganmi (bu dars uchun hech qanday <see cref="PayrollRule"/>
    /// sozlanmagan) — shunday bo'lsa <see cref="SessionRate"/>/<see cref="BonusAmount"/>
    /// ikkalasi ham 0, lekin sabab "qoida yo'q", "bepul dars" EMAS va
    /// "oklad ichida" ham EMAS (<see cref="IncludedInSalary"/>).
    ///
    /// ★ NOM SAQLANDI (<c>RateMissing</c>, <c>RuleMissing</c> emas): ustun
    /// allaqachon bazada va uni qayta nomlash migratsiyada ma'noli hech
    /// narsa bermay, faqat xavf qo'shardi. DTO darajasida esa u
    /// <c>RuleMissing</c> deb chiqadi — tashqi til yangi modelga mos.
    /// </summary>
    public bool RateMissing { get; set; }

    /// <summary>
    /// ★ TOGGLE'LANADIGAN YAGONA maydon: dars keyinchalik "bepul, ustoz ham
    /// haq olmasin" (<c>LiveSession.PayrollExcluded</c>) deb belgilansa —
    /// bu <c>true</c> bo'ladi va hisobot bu darsni JAMIga QO'SHMAYDI
    /// (lekin dars SONIGA hali ham kiradi — shaffoflik uchun).
    /// </summary>
    public bool Excluded { get; set; }

    /// <summary>
    /// ★ 2026-09-04 — guruh "oklad ichida" deb belgilangani uchun haq 0
    /// (<c>Group.PayrollMode</c>). <see cref="RateMissing"/> DAN FARQI:
    /// bu ONGLI qaror, u esa SOZLANMAGANLIK belgisi. Ikkovini bir bayroqqa
    /// yig'ish hisobotda "stavka yo'q" ogohlantirishini soxta chiqarardi va
    /// admin har oy bor-yo'q muammoni qidirardi.
    /// </summary>
    public bool IncludedInSalary { get; set; }

    /// <summary>
    /// ★ 2026-09-04 — haq QANDAY chiqqani: qoidama-qoida tafsilot
    /// (izoh: <see cref="SessionPayoutLine"/>). <see cref="SessionRate"/> va
    /// <see cref="BonusAmount"/> — shularning YIG'INDISI, ular bilan BIRGA
    /// muzlatiladi.
    /// </summary>
    public ICollection<SessionPayoutLine> Lines { get; set; } = new List<SessionPayoutLine>();
}
