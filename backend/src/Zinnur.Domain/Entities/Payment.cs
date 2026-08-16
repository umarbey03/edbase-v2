using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;
using Zinnur.Domain.Finance;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Bitta o'quvchining bitta guruh bo'yicha BIR OYLIK to'lov yozuvi.
/// <c>(StudentId, GroupId, Period)</c> — UNIKAL.
///
/// ★ ENG QIMMAT ESKI XATO SHU YERDA EDI. Ilgari kelgan pul shunday
/// hisoblanardi:
/// <code>
///     months_covered = max(1, round(amount / monthly))
/// </code>
/// ya'ni HAR QANDAY summa kamida bitta to'liq oyni yopardi: 100 000 so'm
/// 540 000 lik oyni "to'langan" qilardi. Markaz jimgina pul yo'qotardi va
/// buni hech kim sezmasdi — jurnalda haqiqiy summa turardi, to'lovlar
/// jadvalida esa "paid".
///
/// Yangi qoida: pul QANCHA bo'lsa, SHUNCHA qarz yopiladi
/// (<see cref="ApplyPayment"/>), qolgani <see cref="PaymentStatus.Partial"/>
/// bo'lib qarz bo'lib turaveradi.
/// </summary>
public class Payment : BaseEntity
{
    public long StudentId { get; set; }

    public User? Student { get; set; }

    public long GroupId { get; set; }

    public Group? Group { get; set; }

    /// <summary>Hisob oyi (<c>YYYY-MM</c>) — bazada satr sifatida saqlanadi.</summary>
    public required string Period { get; set; }

    /// <summary>To'lanishi kerak bo'lgan YAKUNIY summa (chegirmadan keyin).</summary>
    public decimal Amount { get; set; }

    /// <summary>Chegirmagacha bo'lgan tarif summasi — hisobot va nizolar uchun.</summary>
    public decimal BaseAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    /// <summary>Shu oyga tushgan pul. <c>0..Amount</c> oralig'ida.</summary>
    public decimal PaidAmount { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Due;

    /// <summary>To'liq to'langan payt. Qisman to'lovda <c>null</c> BO'LIB QOLADI.</summary>
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>
    /// Oxirgi to'lov usuli. <c>null</c> — hali pul tushmagan (yoki kechirilgan).
    ///
    /// ENUM, erkin satr EMAS: eski tizimda `"naqd"`/`"cash"`/`"CASH"` aralash
    /// yozilib, kunlik kassa usul bo'yicha bo'linmay qolardi.
    /// </summary>
    public PaymentMethod? Method { get; set; }

    public string? Note { get; set; }

    /// <summary>Oxirgi marta kim o'zgartirgani (xodim).</summary>
    public long? MarkedById { get; set; }

    // ---------------------------------------------------------------- hisob

    /// <summary>
    /// Qolgan qarz. ★ Qisman to'langan oy ham QARZ — qolgan qismi bo'yicha.
    /// Eski tizimda qarz butun <c>amount</c> bo'yicha hisoblanardi, ya'ni
    /// qisman to'lov qarzni umuman kamaytirmasdi.
    /// </summary>
    public decimal Outstanding => Math.Max(0m, Amount - PaidAmount);

    /// <summary>Yopilmagan (hali pul kutayotgan) yozuvmi.</summary>
    public bool IsOpen => Status is PaymentStatus.Due or PaymentStatus.Partial;

    public BillingPeriod PeriodValue => BillingPeriod.Parse(Period);

    // ---------------------------------------------------------------- qoidalar

    /// <summary>
    /// Kelgan puldan shu oyga TUSHADIGAN qismini oladi va HAQIQATAN
    /// olingan summani qaytaradi (qolganini chaqiruvchi keyingi oyga beradi).
    ///
    /// Qaytarilgan qiymat orqali taqsimlash mantig'i "qancha qoldi" ni biladi —
    /// shu sababli hisob-kitob bitta joyda va test qilinadigan bo'ladi.
    /// </summary>
    public decimal ApplyPayment(decimal amount, DateTimeOffset now)
    {
        if (amount <= 0)
            throw new DomainException("To'lov summasi musbat bo'lishi kerak.");

        if (!IsOpen)
            throw new DomainException("Yopilgan oyga to'lov qo'shib bo'lmaydi.");

        var take = Math.Min(amount, Outstanding);
        if (take <= 0) return 0m;

        PaidAmount += take;

        if (Outstanding <= 0)
        {
            Status = PaymentStatus.Paid;
            PaidAt = now;
        }
        else
        {
            Status = PaymentStatus.Partial;
            // ★ Sana ATAYLAB qo'yilmaydi: oy hali to'liq to'lanmagan.
            // Eski tizimda bu yerda sana qo'yilib, hisobotda oy "to'langan"
            // qatoriga tushib ketardi.
            PaidAt = null;
        }

        UpdatedAt = now;
        return take;
    }

    /// <summary>
    /// Kechirim: pul olinmaydi, lekin oy qarz bo'lib qolmaydi.
    /// <see cref="PaidAt"/> QO'YILMAYDI — kassaga pul tushmagan, aks holda
    /// kunlik tushum hisoboti soxta bo'lardi.
    /// </summary>
    public void Waive(DateTimeOffset now, long? actorId = null)
    {
        if (Status == PaymentStatus.Paid)
            throw new DomainException("To'langan oyni kechirib bo'lmaydi.");

        Status = PaymentStatus.Waived;
        PaidAt = null;
        MarkedById = actorId ?? MarkedById;
        UpdatedAt = now;
    }

    /// <summary>
    /// To'lovni ORQAGA qaytaradi va haqiqatan qaytarilgan summani beradi.
    ///
    /// ★ Eski tizimda qaytarish faqat jurnalga yozuv qo'shardi — <c>payments</c>
    /// qatori hamon "to'langan" turardi, ya'ni pul qaytarilgan bo'lsa ham tizim
    /// o'quvchini qarzsiz deb bilardi.
    /// </summary>
    public decimal Reverse(decimal amount, DateTimeOffset now)
    {
        if (amount <= 0)
            throw new DomainException("Qaytariladigan summa musbat bo'lishi kerak.");

        if (Status == PaymentStatus.Waived)
            throw new DomainException("Kechirilgan oydan pul qaytarib bo'lmaydi.");

        var give = Math.Min(amount, PaidAmount);
        if (give <= 0) return 0m;

        PaidAmount -= give;
        Status = PaidAmount > 0 ? PaymentStatus.Partial : PaymentStatus.Due;
        PaidAt = null;
        UpdatedAt = now;
        return give;
    }

    /// <summary>
    /// ============================================================================
    /// BOSQICHMA-BOSQICH HISOBLASH (2026-08-16) — bir darsning ulushini
    /// oy summasiga qo'shadi.
    /// ============================================================================
    ///
    /// Talab (loyiha egasi): *"har bir dars uchun platformani o'zi dars
    /// o'tilganidan keyin auto yechib olishi kerak"*. Oy BOSHIDA to'liq
    /// tarif summasi emas — har o'tilgan (va hisoblanadigan) dars shu
    /// metod orqali o'z ulushini qo'shadi (Application qatlamidagi
    /// <c>LessonAccrualService</c> da chaqiriladi — Domain undan bexabar,
    /// faqat qoidani beradi).
    ///
    /// ★ IKKI YO'NALISHDA ISHLAYDI (2026-08-16 dan): odatda
    /// <paramref name="newBaseAmount"/> OLDINGISIDAN katta (yangi dars
    /// qo'shildi), lekin KICHIK ham bo'lishi mumkin — dars keyinchalik
    /// "bepul"/"sababli" deb qayta belgilansa, uning ulushi bekor qilinadi
    /// (`LessonAccrualService` reconciliation, izoh o'sha yerda).
    ///
    /// ★ <c>Paid → Partial/Due</c> TUSHISHI (O'SISHDA) — XATO EMAS, KUTILGAN
    /// HOLAT: balansdan avtomatik yopilgan (yoki kichik summa bilan to'liq
    /// to'langan) oy keyingi darsda YANA qisman qarz bo'lib qolishi mumkin.
    ///
    /// ★ QAYTARIB BERISH (KAMAYISHDA): agar yangi <c>Amount</c> avval
    /// to'langan summadan KICHIK chiqsa, ortiqcha summa <c>PaidAmount</c>
    /// dan olib tashlanadi (aks holda bazadagi <c>PaidAmount &lt;= Amount</c>
    /// CHECK'i buziladi) va NATIJA sifatida QAYTARILADI — chaqiruvchi buni
    /// <c>StudentAccount.Balance</c> ga qo'shishi SHART, aks holda pul
    /// hech qayerda ko'rinmay yo'qolib qolardi.
    /// </summary>
    public decimal Accrue(decimal newBaseAmount, decimal newDiscountAmount, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Waived)
            throw new DomainException("Kechirilgan oyga dars ulushini qo'shib bo'lmaydi.");

        BaseAmount = newBaseAmount;
        DiscountAmount = newDiscountAmount;
        Amount = BaseAmount - DiscountAmount;

        var excess = PaidAmount > Amount ? PaidAmount - Amount : 0m;
        if (excess > 0m) PaidAmount = Amount;

        var wasPaid = Status == PaymentStatus.Paid;

        // ★ Holat HAR SAFAR qaytadan chiqariladi (faqat "Paid'dan tushish"
        // emas) — kamayish yo'nalishida "Partial edi, endi to'liq to'langan"
        // (masalan 100/60 -> Amount 60 ga tushsa 60/60 bo'lib qoladi) kabi
        // KO'TARILISH holatlari ham bo'ladi, ularni alohida shart yozish
        // o'rniga formula BIR JOYDA to'g'ri chiqarilishi ishonchliroq.
        Status = Amount == 0m && PaidAmount == 0m
            ? PaymentStatus.Due
            : PaidAmount >= Amount
                ? PaymentStatus.Paid
                : PaidAmount > 0m
                    ? PaymentStatus.Partial
                    : PaymentStatus.Due;

        PaidAt = Status == PaymentStatus.Paid ? (wasPaid ? PaidAt : now) : null;

        UpdatedAt = now;
        return excess;
    }

    /// <summary>
    /// Invariantlar — bazadagi <c>CHECK</c> cheklovlari bilan BIR XIL.
    /// Ikki joyda tekshiriladi: baza oxirgi himoya, Domain esa xatoni
    /// tushunarli xabar bilan darrov aytadi.
    /// </summary>
    public void Validate()
    {
        _ = BillingPeriod.Parse(Period);

        if (Amount < 0 || BaseAmount < 0 || DiscountAmount < 0)
            throw new DomainException("To'lov summalari manfiy bo'lmaydi.");

        if (DiscountAmount > BaseAmount)
            throw new DomainException("Chegirma tarif summasidan oshmaydi.");

        // ★ UCH SUMMA BIR-BIRIGA MOS BO'LISHI SHART: Amount = BaseAmount − DiscountAmount.
        //
        // Busiz `BaseAmount=600 000, DiscountAmount=60 000, Amount=999 999` kabi
        // qator qolgan barcha tekshiruvlardan o'tib ketardi. Moliya hisoboti esa
        // aynan shu uch ustunga tayanadi: "tarif bo'yicha kutilgan tushum",
        // "berilgan chegirma" va "to'lanishi kerak bo'lgan summa". Ular
        // bir-biriga mos kelmasa hisobot jimgina uydirmaga aylanadi.
        //
        // Amaliy ma'nosi: oy summasini QO'LDA kamaytirish `DiscountAmount`
        // orqali ifodalanadi — "shu oyga chegirma berildi" degani, va u
        // hisobotda ko'rinadi. Aks holda pul "yo'qolgan" bo'lib chiqardi.
        if (Amount != BaseAmount - DiscountAmount)
        {
            throw new DomainException(
                "Oylik summa tarif va chegirmaga mos emas: Amount = BaseAmount − DiscountAmount bo'lishi kerak.");
        }

        if (PaidAmount < 0 || PaidAmount > Amount)
            throw new DomainException("To'langan summa 0 va oylik summa oralig'ida bo'lishi kerak.");
    }
}
