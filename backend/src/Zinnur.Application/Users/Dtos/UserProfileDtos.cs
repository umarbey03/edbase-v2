using Zinnur.Application.Payments.Dtos;
using Zinnur.Application.StudentNotes.Dtos;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Users.Dtos;

// ============================================================================
// O'QUVCHI PROFILI — YAGONA AGREGAT JAVOB
//
// NIMA UCHUN BITTA ENDPOINT: drawer ochilganda 7 ta parallel so'rov
// yuborilsa telefon internetida 2-3 sekund BO'SH panel ko'rinadi. Bitta
// so'rov — bitta loader.
//
// 🔴 RUXSAT SERVERDA KESILADI, frontendda yashirish YETARLI EMAS:
//   Academic/Admin  -> hammasi;
//   Teacher/Assistant -> faqat O'Z guruhidagi o'quvchi va `Finance` = null;
//   Student -> faqat o'zi, `Notes` = null va `Finance.Transactions` = null.
// Qoida `UserProfileService` da BITTA joyda.
// ============================================================================

/// <summary>
/// Profil drawer'ining butun mazmuni.
/// </summary>
/// <param name="User">
/// ATAYLAB mavjud <see cref="UserDetailsDto"/> qayta ishlatiladi (yangi
/// "profil foydalanuvchisi" turi yaratilmadi): frontendda tur allaqachon bor,
/// va ikki shakl bo'lsa ular vaqt o'tib bir-biridan ajralib ketardi.
/// </param>
/// <param name="Finance">
/// 🔴 <c>null</c> — so'rovchiga moliya KO'RSATILMAYDI (ustoz/kurator).
/// Maydon javobda UMUMAN bo'lmaydi, ya'ni uni frontendda yashirish emas,
/// serverda kesish printsipi bajariladi.
/// </param>
/// <param name="Notes">
/// 🔴 <c>null</c> — so'rovchi o'quvchining O'ZI. Izohlar ICHKI eslatma.
/// </param>
public sealed record UserProfileDto(
    UserDetailsDto User,
    ProfileTelegramDto Telegram,
    IReadOnlyList<ProfileGroupDto> Groups,
    ProfileFinanceDto? Finance,
    ProfileStudyDto Study,
    IReadOnlyList<StudentNoteDto>? Notes);

// ---------------------------------------------------------------- telegram

/// <summary>
/// Telegram ulanish holati va oxirgi uzishning izi.
/// </summary>
/// <param name="Linked">Hosila: <c>TelegramId != null</c>.</param>
/// <param name="Username"><c>@</c> BELGISIZ (frontend o'zi qo'shadi).</param>
/// <param name="UnlinkedAt">
/// OXIRGI uzish vaqti (<c>TelegramUnlinkAudit</c> dan). Bog'lanish hozir
/// mavjud bo'lsa ham to'lishi mumkin — "uzilgan, keyin qaytadan bog'langan"
/// tarixi xodim uchun aynan shu holatda qiziq.
/// Uch uzish maydoni o'quvchiga KO'RSATILMAYDI (<c>null</c>).
/// </param>
public sealed record ProfileTelegramDto(
    bool Linked,
    long? TelegramId,
    string? Username,
    DateTimeOffset? LinkedAt,
    DateTimeOffset? UnlinkedAt,
    string? UnlinkedByName,
    string? UnlinkReason);

// ---------------------------------------------------------------- guruhlar

/// <summary>
/// O'quvchining bitta guruhdagi a'zoligi.
/// </summary>
/// <param name="Status">
/// <c>Active</c> — faol o'qiyapti · <c>Paused</c> — pauzada ·
/// <c>Stopped</c> — chiqarilgan · <c>Moved</c> — boshqa guruhga ko'chirilgan.
/// </param>
/// <param name="LeftAt">
/// ⚠️ TAXMINIY: a'zolik qatorining <c>UpdatedAt</c> qiymati va faqat
/// <c>Stopped</c>/<c>Moved</c> holatida beriladi. Modelda "qachon chiqdi"
/// degan alohida ustun YO'Q, <c>UpdatedAt</c> esa a'zolikning HAR
/// o'zgarishida yangilanadi (pauza, tiklash). Ya'ni bu qiymat "holat oxirgi
/// marta qachon o'zgargan" degani — chiqarilgan a'zolik uchun amalda
/// chiqish vaqti, lekin kafolat EMAS. Aniq sana kerak bo'lsa
/// <c>GroupMember</c> ga <c>LeftAt</c> ustuni qo'shilishi kerak (Domain
/// o'zgarishi, bu ish doirasidan tashqarida).
/// </param>
/// <param name="MovedToGroupId">
/// ⚠️ HOZIR DOIM <c>null</c>. "Qaysi guruhga ko'chirildi" ma'lumoti modelda
/// SAQLANMAYDI: <c>GroupService.MoveMemberAsync</c> manba a'zolikni
/// <c>Moved</c> qilib, nishon guruhda yangi a'zolik yaratadi, lekin ikkisi
/// orasida havola qoldirmaydi. Vaqt bo'yicha taxmin qilish (bir sekund
/// ichida yaratilgan a'zolikni nishon deb hisoblash) ATAYLAB
/// BAJARILMAGAN — paketli qo'shishda u BOSHQA guruhni ko'rsatib, xodimni
/// chalg'itardi. Maydon shakl barqarorligi uchun qoldirilgan: ustun
/// qo'shilgach javob o'zgarmaydi, faqat qiymat to'ladi.
/// </param>
/// <param name="PausedUntil">
/// Pauza muddati — <c>GroupMember</c> ning SOYA ustunidan
/// (<c>GroupMemberFields.PausedUntil</c>).
/// </param>
public sealed record ProfileGroupDto(
    long GroupId,
    string GroupName,
    string? TeacherName,
    MemberStatus Status,
    DateTimeOffset JoinedAt,
    DateTimeOffset? LeftAt,
    long? MovedToGroupId,
    string? MovedToGroupName,
    DateOnly? PausedUntil);

// ---------------------------------------------------------------- moliya

/// <summary>
/// O'quvchining moliya kesimi.
/// </summary>
/// <param name="TotalDue">
/// Ochiq oylarning QOLGAN qismi yig'indisi (<c>Amount − PaidAmount</c>) —
/// moliya modulidagi qarz formulasi bilan AYNI. Qisman to'langan oy to'liq
/// qarz deb sanalmaydi.
/// </param>
/// <param name="BlockScope">
/// AMALDAGI bloklash qamrovi: <c>None</c> — bloklanmagan. Qiymat qarz,
/// chegara, istisno va "qattiq rejim" kalitidan hisoblanadi
/// (<c>PaymentBlockPolicy</c>) — ya'ni bu sozlamadagi qamrov emas, balki
/// SHU o'quvchiga hozir tushayotgan blok.
/// </param>
/// <param name="Transactions">
/// Oxirgi 50 ta jurnal yozuvi, yangisidan eskisiga.
/// 🔴 <c>null</c> — so'rovchi o'quvchining O'ZI.
/// Shakl <c>/payments/students/{id}/transactions</c> bilan AYNI
/// (<see cref="PaymentTransactionDto"/> qayta ishlatiladi) — "Hammasini
/// ko'rish" o'sha endpointga o'tadi va frontend bitta turdan foydalanadi.
/// </param>
/// <param name="HasMoreTransactions">
/// 50 tadan ko'p yozuv bormi. To'liq ro'yxat —
/// <c>/payments/students/{id}/transactions</c>.
/// </param>
public sealed record ProfileFinanceDto(
    decimal Balance,
    decimal TotalPaid,
    decimal TotalDue,
    PaymentBlockScope BlockScope,
    IReadOnlyList<ProfilePeriodDto> Periods,
    IReadOnlyList<PaymentTransactionDto>? Transactions,
    bool HasMoreTransactions);

/// <summary>
/// Bitta hisob oyi (o'quvchi × guruh × oy).
/// </summary>
/// <param name="Month">Hisob oyi, <c>YYYY-MM</c>.</param>
/// <param name="SessionCount">
/// SHU oyda SHU guruhda O'TKAZILGAN (yakunlangan) darslar soni.
///
/// ★ NIMA UCHUN SHU MAYDON BOR: talab "xarajatlari ... qaysi dars uchun"
/// deydi, to'lov modeli esa OYLIK davr asosida ishlaydi — dars kesimi
/// modelda umuman yo'q. Shu sababli javob "oy + guruh + o'sha oydagi
/// darslar soni" bilan qoplanadi: xodim "540 000 so'm / 8 dars" deb
/// tushuntira oladi. Haqiqiy dars-bahosi (per-lesson billing) — moliya
/// modelini o'zgartirish, alohida ish.
/// </param>
public sealed record ProfilePeriodDto(
    string Month,
    long GroupId,
    string GroupName,
    decimal Amount,
    decimal PaidAmount,
    decimal Outstanding,
    PaymentStatus Status,
    int SessionCount);

// ---------------------------------------------------------------- o'quv natijalari

/// <summary>O'quv natijalari: uy vazifalari, testlar, davomat.</summary>
/// <param name="HasMoreAssignments">50 tadan ko'p javob bormi.</param>
/// <param name="HasMoreTests">50 tadan ko'p urinish bormi.</param>
public sealed record ProfileStudyDto(
    IReadOnlyList<ProfileAssignmentDto> Assignments,
    bool HasMoreAssignments,
    IReadOnlyList<ProfileTestDto> Tests,
    bool HasMoreTests,
    ProfileAttendanceDto Attendance);

/// <summary>
/// Uy vazifasiga topshirilgan javob va uning bahosi.
/// </summary>
/// <param name="GroupName">
/// Guruh vazifasi bo'lsa guruh nomi, KURS vazifasi bo'lsa <c>null</c>
/// (vazifa YOKI guruhga, YOKI kurs darsiga biriktiriladi —
/// <c>Assignment.Validate</c>).
/// </param>
/// <param name="LessonName">Kurs vazifasi bo'lsa dars nomi, aks holda <c>null</c>.</param>
/// <param name="FileCount">
/// 🔴 Biriktirilgan fayllar SONI — havola ham, <c>objectKey</c> ham ATAYLAB
/// YO'Q (6-bo'lim, 16-tuzoq: <c>objectKey</c> ichki ombor kaliti va UI'ga
/// chiqmaydi). Faylning o'zi mavjud himoyalangan endpoint orqali ochiladi:
/// <c>GET /submissions/files/{id}</c>.
/// </param>
public sealed record ProfileAssignmentDto(
    long SubmissionId,
    long AssignmentId,
    string Title,
    string? GroupName,
    string? LessonName,
    decimal? Score,
    decimal MaxScore,
    SubmissionStatus Status,
    DateTimeOffset SubmittedAt,
    bool IsLate,
    int FileCount);

/// <summary>
/// Test urinishi natijasi.
/// </summary>
/// <param name="Score">
/// Olingan BALL (savol soni EMAS). Har savolning o'z <c>Points</c> qiymati
/// bor, shuning uchun "nechta to'g'ri" degan son modelda umumiy holatda
/// mavjud emas; barcha savollar 1 ballik bo'lganda (standart holat)
/// <paramref name="Score"/> aynan to'g'ri javoblar soniga teng.
/// </param>
/// <param name="MaxScore">Testning to'liq bali.</param>
/// <param name="ScorePercent">Foiz (0..100), bir xona aniqlikda.</param>
/// <param name="FinishedAt">Topshirilgan vaqt; tugatilmagan urinishda <c>null</c>.</param>
public sealed record ProfileTestDto(
    long AttemptId,
    long TestId,
    string Title,
    TestKind Kind,
    decimal? Score,
    decimal? MaxScore,
    decimal? ScorePercent,
    bool ClosedByTimeout,
    DateTimeOffset? FinishedAt);

/// <summary>
/// Davomat doirasi uchun uch son.
///
/// Hisob butun platformadagi bilan AYNI formula
/// (<c>Zinnur.Domain.Progress.AttendanceTally</c>): maxraj — o'quvchining
/// FAOL guruhlarida YAKUNLANGAN darslar soni, "kelgan" esa <c>Absent</c> dan
/// boshqa har qanday holat (kechikkan va yarim qatnashgan ham kelgan
/// hisoblanadi). Ikkinchi formula yozilsa profil va o'quvchi ilovasi turli
/// foiz ko'rsatardi.
/// </summary>
public sealed record ProfileAttendanceDto(int Total, int Present, int Missed, decimal Percent);
