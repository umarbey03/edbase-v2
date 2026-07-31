using Zinnur.Domain.Enums;

namespace Zinnur.Application.Payments.Dtos;

// ============================================================================
// MOLIYA YIG'MA HISOBOTI — `GET /api/v1/payments/summary`
//
// ★ NIMA UCHUN SERVER TOMONDA: bu raqamlarni mijoz o'zi hisoblay olmaydi.
// Buning uchun u minglab to'lov qatorini yuklab olishi kerak bo'lardi —
// bir yillik ish uchun bu o'nlab megabayt trafik, sekundlab kutish va
// brauzerda muzlash. Bundan tashqari har ekran o'z formulasini yozib,
// "qarz" so'zi ikki sahifada ikki xil ma'no anglatib qolardi.
//
// ★ PUL — HAMMA JOYDA `decimal`. `float`/`double` HECH QACHON: ikkilik kasr
// 0.1 ni aniq saqlay olmaydi va qarz hech qachon aniq nolga tushmasdi.
//
// ── IKKI O'LCHOV BOR, ULAR ARALASHTIRILMAYDI ────────────────────────────
//
// Hisobotdagi har raqam quyidagi UCH turdan biriga tegishli va DTO
// izohida aynan qaysi turi ekani yozilgan:
//
//   1) NAQD OQIM (`from`..`to` SANA oralig'i) — moliya jurnalidan
//      (`PaymentTransactions.CreatedAt`). "Shu haftada kassaga qancha
//      tushdi" degan savolga javob.
//
//   2) HISOB (accrual) — `Payments` jadvalidan, HISOB OYI (`Period`)
//      bo'yicha. "Iyul oyiga qancha yozildi va qanchasi yopildi".
//
//   3) HOLAT (snapshot) — hozirgi paytdagi qoldiq: qarz, balans,
//      qarzdorlar soni. Davr filtriga BOG'LIQ EMAS, chunki qarz — bu
//      oraliqda sodir bo'ladigan hodisa emas, balki JORIY qoldiq.
//
// Bu ajratish ataylab: eski tizimda "yig'ilgan" so'zi bir joyda kassaga
// tushgan pulni, boshqa joyda oyga yozilgan `paid_amount` ni anglatardi va
// ikki hisobot hech qachon bir-biriga to'g'ri kelmasdi.
// ============================================================================

/// <summary>
/// Yig'ma hisobot so'rovi.
/// </summary>
/// <param name="From">
/// Davr boshi — MAHALLIY sana (Asia/Tashkent), KIRADI. <c>null</c> bo'lsa
/// joriy oyning birinchi kuni.
/// </param>
/// <param name="To">
/// Davr oxiri — MAHALLIY sana, KIRADI (o'sha kunning 23:59:59 gacha).
/// <c>null</c> bo'lsa bugun.
/// </param>
public sealed record PaymentSummaryQuery(DateOnly? From = null, DateOnly? To = null);

/// <summary>
/// Moliya boshqaruv paneli uchun YAGONA javob.
///
/// Barcha ro'yxatlar HECH QACHON <c>null</c> emas — ma'lumot bo'lmasa
/// bo'sh massiv yoki nol qiymatli element keladi. Sabab: UI'da
/// <c>null</c> arifmetikaga tushib "NaN" va "undefined" ko'rsatardi.
/// </summary>
/// <param name="From">Amalda qo'llangan davr boshi (mahalliy sana).</param>
/// <param name="To">Amalda qo'llangan davr oxiri (mahalliy sana, KIRADI).</param>
/// <param name="FromPeriod">Davrga tegishli birinchi hisob oyi, <c>YYYY-MM</c>.</param>
/// <param name="ToPeriod">Davrga tegishli oxirgi hisob oyi, <c>YYYY-MM</c>.</param>
/// <param name="AsOf">
/// Qarz va balans QAYSI kunga hisoblangani (markaz vaqti bo'yicha bugun).
/// Qarz yoshi shu kundan orqaga sanaladi.
/// </param>
/// <param name="Aging">DOIM 4 ta element (bo'sh bazada ham) — 0-30/31-60/61-90/90+.</param>
/// <param name="Months">
/// DOIM 12 ta element, ESKIDAN YANGIGA. Ma'lumoti yo'q oy ham nol
/// qiymatlar bilan keladi — grafikda oy tushib qolmasin.
/// </param>
public sealed record PaymentSummaryDto(
    DateOnly From,
    DateOnly To,
    string FromPeriod,
    string ToPeriod,
    DateOnly AsOf,
    PaymentSummaryKpiDto Kpi,
    IReadOnlyList<DebtAgingBucketDto> Aging,
    IReadOnlyList<PaymentMonthPointDto> Months,
    IReadOnlyList<PaymentGroupSliceDto> Groups,
    IReadOnlyList<PaymentMethodSliceDto> Methods);

/// <summary>
/// KPI kartochkalar. Har maydon tepasida uning O'LCHOVI ko'rsatilgan
/// (naqd oqim / hisob / holat) — fayl boshidagi izohga qarang.
/// </summary>
/// <param name="Collected">
/// NAQD OQIM. Davrda kassaga HAQIQATAN tushgan pul (jurnalda
/// <c>Kind = Payment</c>). Balansdan yopilgan qarz bu yerga KIRMAYDI —
/// u yangi pul emas, ilgari tushgan pulning ishlatilishi.
/// </param>
/// <param name="Refunded">NAQD OQIM. Davrda orqaga qaytarilgan pul (<c>Kind = Refund</c>).</param>
/// <param name="NetCollected">NAQD OQIM. <c>Collected − Refunded</c> — kassaning sof o'sishi.</param>
/// <param name="BalanceUsed">
/// NAQD OQIM. Davrda o'quvchilar BALANSIDAN yopilgan qarz
/// (<c>Kind = BalanceUse</c>). Alohida ko'rsatiladi, chunki qarz kamaydi,
/// lekin kassaga yangi pul tushmadi.
/// </param>
/// <param name="Waived">
/// NAQD OQIM. Davrda KECHIRILGAN summa (<c>Kind = Waiver</c>) — markaz
/// ongli ravishda voz kechgan pul. ★ Bu summa <see cref="Outstanding"/> ga
/// KIRMAYDI: kechirilgan oy endi qarz emas.
/// </param>
/// <param name="Billed">
/// HISOB. Davrga tegishli oylarga yozilgan yakuniy summa (chegirmadan
/// keyin) — "rejadagi tushum".
/// </param>
/// <param name="Discounts">HISOB. Shu oylarga berilgan chegirma summasi.</param>
/// <param name="PeriodCollected">HISOB. Shu oylarga tushgan pul (<c>Payments.PaidAmount</c>).</param>
/// <param name="CollectionRate">
/// HISOB. Yig'ilish foizi: <c>PeriodCollected / Billed × 100</c>, 0..100.
/// ★ <c>Billed = 0</c> bo'lsa <c>0</c> qaytadi, <c>null</c> EMAS — UI'da
/// bo'linish natijasi "NaN" bo'lib chiqmasin.
/// </param>
/// <param name="Outstanding">
/// ★★ HOLAT. JORIY UMUMIY QARZ — hisobotning eng muhim raqami.
///
/// FAQAT <c>Due</c> va <c>Partial</c> holatidagi oylar qo'shiladi.
/// <c>Waived</c> (kechirilgan) va <c>Paid</c> (to'langan) KIRMAYDI.
///
/// NIMA UCHUN BU AYNAN YOZILGAN: eski hisobotda kechirilgan oy jadvalda
/// "qarz 540 000" bo'lib turardi, o'quvchining hisobida esa "qarz 0" —
/// kassir kechirilgan oy uchun o'quvchidan YANA pul so'ragan. Qarz
/// ta'rifi endi butun hisobotda BITTA joyda (`PaymentSummaryService.Unpaid`)
/// va u o'quvchi kartochkasidagi ta'rif bilan AYNAN bir xil.
/// </param>
/// <param name="StudentBalance">
/// HOLAT. O'quvchilar balansidagi ishlatilmagan pul (oldindan to'lovlar).
/// Bu markazning MAJBURIYATI, daromadi emas.
/// </param>
/// <param name="PayingStudents">NAQD OQIM. Davrda kamida bir marta pul to'lagan noyob o'quvchilar.</param>
/// <param name="DebtorStudents">HOLAT. Hozir qarzi bor noyob o'quvchilar soni.</param>
/// <param name="PaymentCount">NAQD OQIM. Davrdagi to'lov yozuvlari (kvitansiyalar) soni.</param>
public sealed record PaymentSummaryKpiDto(
    decimal Collected,
    decimal Refunded,
    decimal NetCollected,
    decimal BalanceUsed,
    decimal Waived,
    decimal Billed,
    decimal Discounts,
    decimal PeriodCollected,
    decimal CollectionRate,
    decimal Outstanding,
    decimal StudentBalance,
    int PayingStudents,
    int DebtorStudents,
    int PaymentCount);

/// <summary>
/// QARZ YOSHI — kassir uchun eng muhim jadval: qaysi qarz eskirib ketyapti.
///
/// Yosh HISOB OYINING BIRINCHI KUNIDAN sanaladi
/// (<c>kun = AsOf − oyning 1-kuni</c>) — bu eski tizimdagi qoida bilan
/// bir xil va tushuntirishga oson: "iyul oyi qarzi 1-iyuldan beri turibdi".
///
/// Guruhlar KESISHMAYDI va butun qarzni QOPLAYDI, ya'ni to'rt guruh
/// summasi <see cref="PaymentSummaryKpiDto.Outstanding"/> ga AYNAN teng.
/// Kelajak oyi ochilgan bo'lsa (yoshi manfiy) u eng yangi guruhga —
/// <c>0-30</c> ga tushadi, shunda hech bir qator hisobdan tushib qolmaydi.
/// </summary>
/// <param name="Bucket">Kalit: <c>0-30</c>, <c>31-60</c>, <c>61-90</c>, <c>90+</c>.</param>
/// <param name="MinDays">Guruhning quyi chegarasi (kun): 0 / 31 / 61 / 91.</param>
/// <param name="MaxDays">Yuqori chegara; <c>90+</c> uchun <c>null</c> (cheksiz).</param>
/// <param name="Amount">Guruhdagi jami qarz.</param>
/// <param name="Students">Guruhda qarzi bor NOYOB o'quvchilar soni.</param>
/// <param name="Months">Guruhdagi yopilmagan oy yozuvlari soni.</param>
public sealed record DebtAgingBucketDto(
    string Bucket,
    int MinDays,
    int? MaxDays,
    decimal Amount,
    int Students,
    int Months);

/// <summary>
/// Oxirgi 12 oy dinamikasi — grafik uchun bitta nuqta.
///
/// ★ O'LCHOV: HISOB (accrual). <paramref name="Collected"/> — SHU OYGA
/// tegishli to'langan summa; pul jismonan boshqa oyda kelgan bo'lishi
/// mumkin (kechikkan to'lov). Kunlik kassa raqami uchun KPI dagi
/// <c>Collected</c> ishlatiladi.
/// </summary>
/// <param name="Period">Hisob oyi, <c>YYYY-MM</c>.</param>
/// <param name="Billed">Oyga yozilgan yakuniy summa (reja).</param>
/// <param name="Collected">Shu oyga tushgan pul.</param>
/// <param name="Outstanding">★ Shu oyning QARZI — kechirilgan oy KIRMAYDI.</param>
/// <param name="Waived">Shu oyda kechirilgan summa.</param>
/// <param name="Discounts">Shu oyga berilgan chegirma.</param>
/// <param name="CollectionRate">Yig'ilish foizi 0..100; reja 0 bo'lsa 0 (null emas).</param>
/// <param name="Records">Oydagi to'lov yozuvlari soni.</param>
public sealed record PaymentMonthPointDto(
    string Period,
    decimal Billed,
    decimal Collected,
    decimal Outstanding,
    decimal Waived,
    decimal Discounts,
    decimal CollectionRate,
    int Records);

/// <summary>
/// GURUH KESIMI — davrga tegishli hisob oylari bo'yicha.
/// Qarzi eng kattasidan boshlab tartiblangan: ish shu tartibda olib boriladi.
/// </summary>
/// <param name="Outstanding">★ Kechirilgan oylar KIRMAYDI.</param>
/// <param name="Students">Guruhda shu oylarda hisobi bo'lgan noyob o'quvchilar.</param>
public sealed record PaymentGroupSliceDto(
    long GroupId,
    string GroupName,
    decimal Billed,
    decimal Collected,
    decimal Outstanding,
    decimal Waived,
    decimal CollectionRate,
    int Students);

/// <summary>
/// TO'LOV USULI KESIMI — davrda kassaga tushgan pul (naqd oqim).
/// </summary>
/// <param name="Method">
/// <c>Cash</c> / <c>Card</c>. JSON'da SATR. <c>null</c> — usuli
/// ko'rsatilmagan eski yozuv (yangi to'lovda usul MAJBURIY).
/// </param>
/// <param name="MethodName">Tayyor o'zbekcha nom — UI har joyda o'z lug'atini yozmasin.</param>
/// <param name="Share">Umumiy tushumdagi ulushi, 0..100. Tushum 0 bo'lsa 0.</param>
public sealed record PaymentMethodSliceDto(
    PaymentMethod? Method,
    string MethodName,
    decimal Amount,
    int Count,
    decimal Share);
