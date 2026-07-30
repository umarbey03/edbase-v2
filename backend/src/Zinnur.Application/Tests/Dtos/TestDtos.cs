using Zinnur.Domain.Enums;

namespace Zinnur.Application.Tests.Dtos;

// ============================================================================
// XODIM (o'quv bo'limi) KO'RINISHI — to'g'ri javoblar KO'RINADI
// ============================================================================

/// <summary>Test kartochkasi (ro'yxat uchun).</summary>
/// <param name="MaxScore">Savollar balining yig'indisi (Domain hisoblaydi).</param>
/// <param name="AttemptCount">Topshirilgan urinishlar soni.</param>
public sealed record TestDto(
    long Id,
    string Title,
    string? Description,
    TestKind Kind,
    long? ModuleLessonId,
    string? ModuleLessonName,
    int? TimeLimitMinutes,
    DateTimeOffset? DueAt,
    bool IsPublished,
    long? CreatedById,
    int QuestionCount,
    decimal MaxScore,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Tahrirlash ko'rinishi: savollar TO'G'RI JAVOBLARI bilan.
/// FAQAT o'quv bo'limi/admin oladi.
/// </summary>
public sealed record TestAuthoringDto(
    TestDto Test,
    IReadOnlyList<AuthoringQuestionDto> Questions);

public sealed record AuthoringQuestionDto(
    long Id,
    string Body,
    string? ImageKey,
    int Position,
    decimal Points,
    bool IsMultipleChoice,
    IReadOnlyList<AuthoringOptionDto> Options);

public sealed record AuthoringOptionDto(
    long Id,
    string Body,
    int Position,
    bool IsCorrect);

// ============================================================================
// ★ O'QUVCHI KO'RINISHI — ALOHIDA TURLAR, TO'G'RI JAVOB MAYDONI UMUMAN YO'Q
// ============================================================================

/// <summary>
/// ★★ TEST YECHISH VARAQASI. <c>isCorrect</c> maydoni BU TURDA MAVJUD EMAS.
///
/// NIMA UCHUN ALOHIDA TUR (bitta DTO'dan maydonni "olib tashlash" EMAS):
/// bitta umumiy DTO ishlatilsa to'g'ri javobni yashirish PROGRAMMIST
/// E'TIBORIGA bog'liq bo'lardi — bir joyda unutilsa (yangi endpoint, yangi
/// filtr, xato tuzatish) javoblar jimgina oshkor bo'lardi va buni HECH KIM
/// sezmasdi (test o'z-o'zidan ishlayveradi).
///
/// Alohida tur bilan bu XATO IMKONSIZ: <see cref="TakeOptionDto"/> da
/// bunday maydon yo'q, ya'ni uni tasodifan to'ldirishning YO'LI yo'q.
/// Kompilyator qo'riqlaydi, odam emas.
/// </summary>
/// <param name="Deadline">
/// Vaqt chegarasi qachon tugaydi (server hisoblagan, tolerantlik bilan).
/// <c>null</c> — chegarasiz. Klient taymeri SHU qiymatga tayanadi, lekin
/// haqiqiy tekshiruv baribir serverda.
/// </param>
public sealed record TakeTestDto(
    long Id,
    string Title,
    string? Description,
    int? TimeLimitMinutes,
    DateTimeOffset? DueAt,
    long AttemptId,
    DateTimeOffset StartedAt,
    DateTimeOffset? Deadline,
    decimal MaxScore,
    IReadOnlyList<TakeQuestionDto> Questions);

/// <param name="MultipleAnswers">
/// Savolda bir nechta to'g'ri javob bor — interfeys checkbox ko'rsatadi
/// (radio emas). Bu QAYSI variant to'g'ri ekanini OSHKOR QILMAYDI, faqat
/// nechta belgilash mumkinligini aytadi.
/// </param>
public sealed record TakeQuestionDto(
    long Id,
    string Body,
    string? ImageKey,
    int Position,
    decimal Points,
    bool MultipleAnswers,
    IReadOnlyList<TakeOptionDto> Options);

/// <summary>Variant — o'quvchi ko'rinishi. <c>IsCorrect</c> ATAYLAB YO'Q.</summary>
public sealed record TakeOptionDto(
    long Id,
    string Body,
    int Position);

/// <summary>O'quvchi uchun mavjud test (ro'yxat).</summary>
/// <param name="MyStatus">O'zining urinishi holati; <c>null</c> — boshlamagan.</param>
/// <param name="CanStart">Hozir boshlash mumkinmi (server qarori).</param>
public sealed record AvailableTestDto(
    long Id,
    string Title,
    string? Description,
    TestKind Kind,
    long? ModuleLessonId,
    string? ModuleLessonName,
    int? TimeLimitMinutes,
    DateTimeOffset? DueAt,
    int QuestionCount,
    decimal MaxScore,
    AttemptStatus? MyStatus,
    decimal? MyScore,
    bool CanStart);

/// <summary>Urinish boshlangach qaytadigan ma'lumot.</summary>
public sealed record StartAttemptDto(
    long AttemptId,
    long TestId,
    DateTimeOffset StartedAt,
    DateTimeOffset? Deadline,
    int? TimeLimitMinutes);

/// <summary>O'quvchining o'z natijasi.</summary>
public sealed record MyResultDto(
    long TestId,
    string Title,
    long AttemptId,
    AttemptStatus Status,
    decimal? Score,
    decimal? MaxScore,
    decimal? Percent,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,
    bool ClosedByTimeout);

// ============================================================================
// NATIJALAR (xodim)
// ============================================================================

/// <summary>
/// Natijalar jadvalining bitta qatori — BITTA URINISH = BITTA QATOR.
///
/// <c>GroupNames</c> ataylab SATR (ro'yxat emas): o'quvchi bir nechta
/// guruhda bo'lsa ular vergul bilan BIR qatorda ko'rsatiladi. Eski tizim
/// bu yerda guruh jadvaliga `outerjoin` qilardi va ikki guruhdagi o'quvchi
/// natijalar jadvalida IKKI MARTA chiqardi (CSV eksportida ham) — reyting
/// va statistika buzilardi.
/// </summary>
public sealed record TestResultRowDto(
    long AttemptId,
    long StudentId,
    string StudentName,
    string GroupNames,
    decimal? Score,
    decimal? MaxScore,
    decimal? Percent,
    DateTimeOffset? SubmittedAt,
    bool ClosedByTimeout);

/// <summary>CSV eksport natijasi (controller uni fayl sifatida qaytaradi).</summary>
public sealed record CsvExport(string FileName, string ContentType, ReadOnlyMemory<byte> Content);

// ============================================================================
// SO'ROVLAR
// ============================================================================

/// <param name="Kind">
/// <c>"Lesson"</c> — kurs darsiga bog'langan (sur'at nazoratiga kiradi) yoki
/// <c>"Competition"</c> — musobaqa. JSON'da SATR ko'rinishida yuboriladi.
/// </param>
public sealed record CreateTestRequest(
    string Title,
    TestKind Kind = TestKind.Competition,
    long? ModuleLessonId = null,
    string? Description = null,
    int? TimeLimitMinutes = null,
    DateTimeOffset? DueAt = null);

public sealed record UpdateTestRequest(
    string Title,
    string? Description = null,
    int? TimeLimitMinutes = null,
    DateTimeOffset? DueAt = null);

/// <param name="Options">Kamida 2 ta variant, kamida 1 tasi to'g'ri (Domain tekshiradi).</param>
public sealed record SaveQuestionRequest(
    string Body,
    IReadOnlyList<SaveOptionRequest> Options,
    decimal Points = 1m,
    int? Position = null,
    string? ImageKey = null);

public sealed record SaveOptionRequest(
    string Body,
    bool IsCorrect = false,
    int? Position = null);

/// <param name="Answers">
/// Savol -> tanlangan variant(lar). Yuborilmagan savol javobsiz hisoblanadi
/// (0 ball). Begona variant ID'lari SERVERDA filtrlanadi.
/// </param>
public sealed record SubmitTestRequest(IReadOnlyList<QuestionAnswerRequest> Answers);

public sealed record QuestionAnswerRequest(long QuestionId, IReadOnlyList<long> OptionIds);

/// <summary>Test ro'yxati filtri (xodim).</summary>
public sealed record TestListQuery(
    TestKind? Kind = null,
    bool? IsPublished = null,
    long? ModuleLessonId = null,
    int Page = 1,
    int PageSize = 25);
