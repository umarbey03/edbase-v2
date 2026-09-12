using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// «Suzuvchi» stavkaning BITTA BOSQICHI — <see cref="PayrollRule"/> ning
/// <c>TieredByAttendance</c> turida ishlatiladi (2026-09-04).
///
/// ★ NIMA UCHUN ALOHIDA JADVAL: bosqichlar soni oldindan noma'lum.
/// Ba'zi markazda uchta (0/1/2+), ba'zisida o'ntacha. Ularni <c>Rate1</c>,
/// <c>Rate2</c>... ustunlari bilan yozish yana o'sha qotib qolgan sxemaga
/// olib borardi; JSON qilib tiqish esa ularni SQL'dan tekshirib bo'lmaydigan
/// va migratsiya qilib bo'lmaydigan holga keltirardi.
///
/// ★ TANLASH QOIDASI (<c>PayrollCalculator.PickTier</c>):
/// haqiqiy o'quvchi soniga TENG yoki undan KICHIK eng katta bosqich olinadi.
/// Eng yuqori bosqichdan ko'p o'quvchi kelsa — eng yuqori summa beriladi
/// (HolliHop'dagi bilan ayni: "hatto maksimaldan ko'p bo'lsa ham ustoz eng
/// yuqori ko'rsatilgan summani oladi"). Mos bosqich topilmasa — 0.
/// </summary>
public class PayrollRuleTier : BaseEntity
{
    public long RuleId { get; set; }

    public PayrollRule? Rule { get; set; }

    /// <summary>
    /// Bosqich CHEGARASI — "shu sondagi va undan ko'proq o'quvchi uchun".
    /// 0 dan boshlanadi: hech kim kelmagan darsning ham o'z summasi bo'ladi
    /// (ustoz kelgan, kutgan — ko'p markaz buni to'laydi).
    /// </summary>
    public int StudentCount { get; set; }

    /// <summary>Shu bosqichdagi dars summasi (so'm).</summary>
    public decimal Amount { get; set; }

    public void Validate()
    {
        if (StudentCount is < 0 or > 500)
            throw new DomainException("Bosqichdagi o'quvchi soni 0..500 oralig'ida bo'lishi kerak.");

        if (Amount < 0 || Amount > PayrollRule.MaxAmount)
            throw new DomainException("Bosqich summasi 0..1 000 000 000 oralig'ida bo'lishi kerak.");
    }
}
