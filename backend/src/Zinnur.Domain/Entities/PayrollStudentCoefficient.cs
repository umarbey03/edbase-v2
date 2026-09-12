using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Oylik o'quvchi bonusining BIR O'QUVCHIGA tegishli KOEFFITSIENTI
/// (2026-09-04, HolliHop'dagi «Коэффициент» ustuni).
///
/// ★ MUAMMO: <c>MonthlyPerActiveStudent</c> qoidasi "har faol o'quvchi uchun
/// 100 000 so'm" deydi. Lekin o'quvchi oyning 20-kunida qo'shilgan yoki
/// oy o'rtasida boshqa kuratorga o'tkazilgan bo'lsa — to'liq summa
/// adolatsiz. Koeffitsientsiz adminning yagona yo'li QO'LDA tuzatma
/// yozish bo'lardi va sabab ("qaysi o'quvchi uchun, nega yarim") hisobotda
/// ko'rinmasdi.
///
/// ★ YO'QLIGI = 100%. Jadval FAQAT ISTISNONI saqlaydi — har oy har o'quvchi
/// uchun 100 qatorini yozib qo'yish ma'lumotni shishirardi va "yozilmagan"
/// bilan "100 deb yozilgan" o'rtasida farq bo'lmasdi.
///
/// ★ DAVR BO'YICHA: koeffitsient bitta OYGA tegishli (<see cref="PeriodStart"/>)
/// — keyingi oyda o'quvchi to'liq sanaladi, admin qayta aralashmaydi.
/// </summary>
public class PayrollStudentCoefficient : BaseEntity
{
    public const int MaxNoteLength = 200;

    /// <summary>Kimning oyligiga ta'sir qiladi (ustoz/kurator).</summary>
    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Qaysi o'quvchi uchun.</summary>
    public long StudentId { get; set; }

    public User? Student { get; set; }

    /// <summary>Davr — oyning BIRINCHI kuni (<c>BillingPeriod.FirstDay()</c>).</summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>0..100. 50 = shu o'quvchi uchun yarim summa.</summary>
    public decimal Percent { get; set; } = 100m;

    /// <summary>Nega — "12-sentabrda qo'shildi" kabi. Hisobotda ko'rinadi.</summary>
    public string? Note { get; set; }

    public long CreatedById { get; set; }

    public User? CreatedBy { get; set; }

    public void Validate()
    {
        if (Percent is < 0 or > 100)
            throw new DomainException("Koeffitsient 0..100 oralig'ida bo'lishi kerak.");

        if (Note is { Length: > MaxNoteLength })
            throw new DomainException($"Izoh {MaxNoteLength} belgidan oshmasligi kerak.");
    }
}
