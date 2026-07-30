using Zinnur.Domain.Enums;

namespace Zinnur.Application.Payments.Dtos;

// ============================================================================
// MOLIYA DTO'LARI (FAZA 4.3)
//
// ENUM'LAR shu yerda ENUM bo'lib qoladi (satrga qo'lda o'girilmaydi) —
// `Program.cs` dagi `JsonStringEnumConverter` ularni JSON'da baribir SATR
// qilib chiqaradi ("Partial", "Cash"), lekin tur xavfsizligi yo'qolmaydi.
// Bu `Groups`/`Courses` modullaridagi bilan AYNAN bir xil naqsh.
//
// PUL — HAMMA JOYDA `decimal`. `double` ishlatilsa 540000.00000000006 kabi
// qiymatlar paydo bo'lib, qarz hech qachon aniq nolga tushmasdi.
// ============================================================================

// ---------------------------------------------------------------- oylik yozuv

/// <summary>
/// Bitta oylik to'lov yozuvi (o'quvchi × guruh × oy).
/// </summary>
/// <param name="Period">Hisob oyi, <c>YYYY-MM</c>.</param>
/// <param name="BaseAmount">Tarif summasi — chegirmagacha.</param>
/// <param name="DiscountAmount">Berilgan chegirma.</param>
/// <param name="Amount">To'lanishi kerak bo'lgan yakuniy summa.</param>
/// <param name="Outstanding">
/// Qolgan qarz. ★ Qisman to'langan oy ham QARZ — qolgan qismi bo'yicha.
/// Bazada saqlanmaydi, <c>Amount − PaidAmount</c> dan hisoblanadi.
/// </param>
public sealed record PaymentDto(
    long Id,
    long StudentId,
    string StudentName,
    long GroupId,
    string GroupName,
    string Period,
    decimal BaseAmount,
    decimal DiscountAmount,
    decimal Amount,
    decimal PaidAmount,
    decimal Outstanding,
    PaymentStatus Status,
    DateTimeOffset? PaidAt,
    PaymentMethod? Method,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

// ---------------------------------------------------------------- oy ochish

/// <summary>
/// Oylik yozuvlarni ochish so'rovi.
/// </summary>
/// <param name="Period">
/// <c>YYYY-MM</c>. <c>null</c> — markaz vaqt zonasidagi JORIY oy
/// (server UTC'da ishlaydi, shuning uchun oy chegarasida bu farq qiladi).
/// </param>
/// <param name="GroupId"><c>null</c> — barcha faol guruhlar.</param>
public sealed record OpenPeriodRequest(string? Period = null, long? GroupId = null);

/// <summary>
/// Oy ochish natijasi — IDEMPOTENT amalning hisoboti.
/// </summary>
/// <param name="Created">Yangi yaratilgan yozuvlar soni.</param>
/// <param name="AlreadyOpen">
/// Allaqachon mavjud bo'lgani uchun O'TKAZIB YUBORILGAN a'zoliklar soni.
/// Bu XATO EMAS: amalni takror chaqirish xavfsiz bo'lishi kerak.
/// </param>
/// <param name="SkippedNoTariff">
/// Tarif topilmagani uchun ochilmagan a'zoliklar. Butun amal yiqilmaydi —
/// aks holda bitta sozlanmagan guruh butun markazning oyini ochilmay
/// qoldirardi. Sabablari <paramref name="Warnings"/> da.
/// </param>
/// <param name="BalanceApplied">
/// Ochilgandan KEYIN balansdan avtomatik yopilgan summa — oldindan to'lagan
/// o'quvchi qarzdor bo'lib chiqmasligi uchun.
/// </param>
public sealed record OpenPeriodResult(
    string Period,
    int Created,
    int AlreadyOpen,
    int SkippedNoTariff,
    decimal BalanceApplied,
    int MonthsClosedFromBalance,
    IReadOnlyList<PaymentDto> Payments,
    IReadOnlyList<string> Warnings);

// ---------------------------------------------------------------- to'lov

/// <summary>
/// Pul qabul qilish — moliya modulining YAGONA kirish nuqtasi.
/// </summary>
/// <param name="GroupId">
/// <c>null</c> — pul o'quvchining BARCHA guruhlari bo'yicha eng eski
/// qarzdan boshlab taqsimlanadi. Qiymat berilsa faqat o'sha guruh oylari
/// yopiladi (ortiqchasi baribir balansga tushadi).
/// </param>
public sealed record RecordPaymentRequest(
    long StudentId,
    decimal Amount,
    PaymentMethod Method,
    long? GroupId = null,
    string? Note = null);

/// <summary>
/// Kvitansiya — to'lov natijasining to'liq tasviri.
/// </summary>
/// <param name="ReceiptNo">Kvitansiya raqami, <c>ZN-2026-07-000123</c>.</param>
/// <param name="Applied">Qarzlarga haqiqatan tushgan summa.</param>
/// <param name="ToBalance">Qarzdan ortib, balansga o'tgan summa.</param>
/// <param name="DebtAfter">To'lovdan KEYINGI umumiy qarz.</param>
public sealed record PaymentReceiptDto(
    long TransactionId,
    string ReceiptNo,
    long StudentId,
    string StudentName,
    decimal Amount,
    decimal Applied,
    decimal ToBalance,
    int MonthsClosed,
    int MonthsPartial,
    decimal Balance,
    decimal DebtAfter,
    PaymentMethod Method,
    IReadOnlyList<PaymentDto> AffectedMonths,
    DateTimeOffset CreatedAt);

/// <summary>Kechirim sababi — auditda saqlanadi.</summary>
public sealed record WaiveRequest(string? Reason = null);

/// <summary>Pulni orqaga qaytarish.</summary>
public sealed record ReversePaymentRequest(
    long StudentId,
    decimal Amount,
    string? Reason = null);

/// <summary>
/// Qaytarish natijasi.
/// </summary>
/// <param name="FromBalance">Balansdan yechilgan qism (avval shu ishlatiladi).</param>
/// <param name="FromPayments">To'langan oylardan qaytarilgan qism (eng yangisidan).</param>
/// <param name="Unreturned">
/// Qaytarib bo'lmagan qoldiq. ★ Bu XATO EMAS, balki xodimga aytiladigan
/// FAKT: so'ralgan summa tizimda umuman tushmagan bo'lishi mumkin.
/// Jimgina "qaytarildi" deb yozish hisobni buzardi.
/// </param>
public sealed record ReversalDto(
    long StudentId,
    decimal Requested,
    decimal Returned,
    decimal FromBalance,
    decimal FromPayments,
    decimal Unreturned,
    decimal Balance,
    decimal DebtAfter,
    IReadOnlyList<PaymentDto> AffectedMonths);

// ---------------------------------------------------------------- hisob

/// <summary>Moliya jurnali qatori — pul harakatining O'ZGARMAS yozuvi.</summary>
public sealed record PaymentTransactionDto(
    long Id,
    long StudentId,
    long? GroupId,
    string? GroupName,
    PaymentTransactionKind Kind,
    decimal Amount,
    string? ReceiptNo,
    PaymentMethod? Method,
    string? Note,
    long? ActorId,
    string? ActorName,
    DateTimeOffset CreatedAt);

/// <summary>
/// O'quvchining moliya hisobi: qarz, balans, oylar tarixi va oxirgi jurnal
/// yozuvlari. O'quvchining O'ZI ham shu javobni ko'radi (faqat o'ziniki).
/// </summary>
/// <param name="Debt">Ochiq oylar bo'yicha jami qarz.</param>
/// <param name="Balance">Ortiqcha to'langan va hali sarflanmagan pul.</param>
/// <param name="Exempt">Bloklashdan istisno qilinganmi.</param>
public sealed record StudentAccountDto(
    long StudentId,
    string FullName,
    decimal Debt,
    decimal Balance,
    bool Exempt,
    int OpenMonths,
    decimal Paid,
    IReadOnlyList<PaymentDto> Months,
    IReadOnlyList<PaymentTransactionDto> RecentTransactions);

/// <summary>Oylik yozuvlar ro'yxati uchun filtr (moliya paneli).</summary>
/// <param name="OnlyDebt"><c>true</c> — faqat qarzi borlar (Due/Partial).</param>
public sealed record PaymentListQuery(
    string? Period = null,
    long? GroupId = null,
    long? StudentId = null,
    PaymentStatus? Status = null,
    bool OnlyDebt = false,
    int Page = 1,
    int PageSize = 25);

// ---------------------------------------------------------------- tarif

public sealed record TariffDto(
    long Id,
    string Name,
    decimal Amount,
    int LessonsCount,
    long? CourseId,
    string? CourseName,
    long? GroupId,
    string? GroupName,
    DateOnly ActiveFrom,
    bool IsActive,
    int Specificity,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateTariffRequest(
    string Name,
    decimal Amount,
    DateOnly ActiveFrom,
    int LessonsCount = 8,
    long? CourseId = null,
    long? GroupId = null,
    bool IsActive = true);

/// <summary>
/// ★ <c>PUT</c> — TO'LIQ ALMASHTIRISH: yuborilmagan maydon standart qiymat
/// bilan YOZILADI (<c>courseId</c> yuborilmasa — <c>null</c> bo'ladi, ya'ni
/// tarif "barcha kurslar" ga aylanadi).
///
/// Shuning uchun bu turda "ixtiyoriy" maydon YO'Q: klient doim to'liq
/// holatni yuboradi. Qisman yangilash kerak bo'lsa avval <c>GET</c> qilinadi.
/// Sana va son maydonlari servisda ALOHIDA tekshiriladi — JSON'da
/// yuborilmasa ular <c>0001-01-01</c> va <c>0</c> bo'lib tushardi va
/// jimgina yaroqsiz tarif hosil bo'lardi.
/// </summary>
public sealed record UpdateTariffRequest(
    string Name,
    decimal Amount,
    DateOnly ActiveFrom,
    int LessonsCount,
    bool IsActive,
    long? CourseId = null,
    long? GroupId = null);

// ---------------------------------------------------------------- chegirma

public sealed record StudentDiscountDto(
    long Id,
    long StudentId,
    string StudentName,
    long? GroupId,
    string? GroupName,
    DiscountKind Kind,
    decimal Value,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    bool IsActive,
    string? Reason,
    int Specificity,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateDiscountRequest(
    DiscountKind Kind,
    decimal Value,
    DateOnly ValidFrom,
    DateOnly? ValidTo = null,
    long? GroupId = null,
    string? Reason = null,
    bool IsActive = true);

/// <summary>★ <c>PUT</c> — TO'LIQ ALMASHTIRISH (izoh: <see cref="UpdateTariffRequest"/>).</summary>
public sealed record UpdateDiscountRequest(
    DiscountKind Kind,
    decimal Value,
    DateOnly ValidFrom,
    bool IsActive,
    DateOnly? ValidTo = null,
    long? GroupId = null,
    string? Reason = null);

// ---------------------------------------------------------------- blok

/// <summary>
/// Bloklash darvozasining natijasi — <c>403</c> qaytarishdan OLDIN ham
/// so'rash mumkin (frontend ogohlantirish ko'rsatishi uchun).
/// </summary>
/// <param name="Enforced">
/// Global "qattiq rejim" yoqilganmi. <c>false</c> — yumshoq rejim: qarz
/// hisoblanadi va ko'rsatiladi, lekin HECH KIM bloklanmaydi (sinov muhiti).
/// </param>
/// <param name="Reason">Bloklangan bo'lsa — foydalanuvchiga ko'rsatiladigan matn.</param>
public sealed record PaymentBlockDto(
    long StudentId,
    bool Blocked,
    decimal Debt,
    decimal Threshold,
    PaymentBlockScope ConfiguredScope,
    PaymentBlockScope RequestedScope,
    bool Exempt,
    bool Enforced,
    string? Reason);

/// <summary>Bloklashdan istisno qilish (yoki bekor qilish).</summary>
public sealed record SetExemptRequest(bool Exempt, string? Reason = null);

/// <summary>Moliya sozlamalari (chegara va qamrov bazadan, qattiq rejim konfiguratsiyadan).</summary>
public sealed record FinanceSettingsDto(
    decimal BlockThreshold,
    PaymentBlockScope BlockScope,
    bool Enforce);

/// <summary>
/// Chegarani va qamrovni o'zgartirish. <c>Enforce</c> BU YERDA YO'Q —
/// u muhit xossasi va konfiguratsiyadan keladi (<c>Payments:EnforceBlock</c>).
/// </summary>
public sealed record UpdateFinanceSettingsRequest(
    decimal BlockThreshold,
    PaymentBlockScope BlockScope);
