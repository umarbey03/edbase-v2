using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ============================================================================
/// DARS ULUSHI YOZUVI (2026-08-16) — BITTA dars, BITTA o'quvchi uchun
/// hisoblangan to'lov ulushi.
/// ============================================================================
///
/// ★ IKKI VAZIFANI BAJARADI:
///   1) IDEMPOTENTLIK QULFI — <c>LessonAccrualService.AccrueForSessionAsync</c>
///      shu darsga ALLAQACHON yozuv bormi deb SHU YERDAN tekshiradi. Dars
///      yakunlanishi ikki marta ishga tushsa (masalan avto-yakunlash fon
///      vazifasi va ustozning o'zi bir vaqtda) — ikkinchi urinish HECH
///      NARSA qo'shmaydi.
///   2) HISOBOT — "bu oyga aynan qaysi darslar kirdi" degan savolga
///      <c>Payment.BaseAmount</c> ning o'zi javob bermaydi (u faqat
///      YIG'INDI). Bu jadval har qo'shilgan ulushni alohida saqlaydi.
///
/// <c>(SessionId, StudentId)</c> — UNIKAL: bitta darsning bitta o'quvchiga
/// IKKI marta qo'shilishining oxirgi (baza darajasidagi) himoyasi —
/// <c>Payments(StudentId, GroupId, Period)</c> unikal indeksi
/// <c>OpenPeriodAsync</c> ni himoya qilgani bilan AYNI naqsh.
/// </summary>
public class LessonCharge : BaseEntity
{
    public long SessionId { get; set; }

    public long StudentId { get; set; }

    public long GroupId { get; set; }

    /// <summary>Tegishli oylik to'lov yozuvi — hisobot uchun ("bu oyga qaysi darslar kirdi").</summary>
    public long PaymentId { get; set; }

    /// <summary>
    /// Navigatsiya SHART (<c>PaymentId</c> dan TASHQARI): yangi ochilgan
    /// oyning <c>Id</c> si <c>SaveChanges</c> gacha 0, ya'ni uni qo'lda
    /// yozib bo'lmaydi — EF ikkala qatorni BIR <c>SaveChanges</c> ichida
    /// to'g'ri tartibda yozadi (izoh: <c>AttendanceAudit.Attendance</c>
    /// dagi bilan AYNI sabab).
    /// </summary>
    public Payment? Payment { get; set; }

    /// <summary>
    /// ★ "NARXI" — shu darsning STIKER narxi (tarif summasi / oyiga dars
    /// soni). DOIM to'ldiriladi, hatto <see cref="SkipReason"/> bo'lsa ham
    /// ("bu darsning narxi shuncha edi, lekin bepul/sababli bo'lgani uchun
    /// yechilmadi" — shaffoflik uchun kerak).
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// ★ HAQIQATDA yechilgan summa (2026-08-16) — chegirmadan KEYIN,
    /// <see cref="SkipReason"/> bo'lsa 0. <c>Amount</c> dan FARQ QILADI:
    /// chegirmali o'quvchida stiker narx bilan haqiqiy yechilgan summa
    /// har xil — bu maydonsiz "qaysi darsdan qancha yechilgani" degan
    /// hisobot chegirmali oilalar uchun OSHIRIB ko'rsatardi.
    ///
    /// Qiymat ISHLOV BERILGAN PAYTDAGI marjinal hissa sifatida hisoblanadi
    /// (<c>payment.Amount</c> ning oldin/keyingi farqi) — agar KEYINCHALIK
    /// boshqa bir darsning holati o'zgarsa, bu qator QAYTA HISOBLANMAYDI
    /// (drift mumkin, xuddi yumaloqlash drift'i kabi qabul qilingan).
    /// </summary>
    public decimal NetAmount { get; set; }

    /// <summary>
    /// <c>null</c> — to'liq yechilgan (oddiy holat). Aks holda NEGA
    /// yechilmagani (yoki qisman yechilmagani, agar keyinchalik chegirma
    /// bilan aralashsa).
    /// </summary>
    public LessonChargeSkipReason? SkipReason { get; set; }
}
