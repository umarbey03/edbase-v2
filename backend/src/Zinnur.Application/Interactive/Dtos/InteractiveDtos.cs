using System.Text.Json.Nodes;

namespace Zinnur.Application.Interactive.Dtos;

/// <summary>Birlik bo'yicha qisqa progress (dars kartasi uchun).</summary>
public sealed record UnitProgressSummaryDto(
    int BlocksDone,
    int BlocksTotal,
    bool Completed,
    int ExerciseRight,
    int ExerciseTotal,
    int? TestScore,
    int? TestMax);

/// <summary>Darsga bog'langan birlik (kartada ko'rsatiladi).</summary>
public sealed record LessonUnitDto(
    long Id,
    string Book,
    string Code,
    string TitleUz,
    string? TitleAr,
    UnitProgressSummaryDto Progress);

/// <summary>
/// `GET /interactive/lessons/{lessonId}` — dars sahifasidagi "Darslik" kartasi.
/// </summary>
/// <param name="Access">"granted" | "upsell" | "none" | "staff".</param>
/// <param name="Unit">
/// 🔴 Faqat <c>granted</c>/<c>staff</c> da. Huquqi yo'q o'quvchi kontent
/// borligini ham bilmaydi — <c>upsell</c> da ham faqat "darslik bor" degan belgi.
/// </param>
/// <param name="HasBook">Darsga birlik bog'langanmi (faqat upsell kartasi uchun).</param>
public sealed record LessonBookDto(string Access, bool HasBook, LessonUnitDto? Unit);

/// <summary>`POST …/items/{itemKey}/check` so'rovi.</summary>
public sealed record CheckItemRequest(
    int? Index,
    int? Choice,
    bool? Value,
    string? Left,
    string? Right,
    int? Word,
    IReadOnlyDictionary<string, string>? Cells,
    IReadOnlyList<string>? Words,
    string? Text);

public sealed record FirstScoreDto(int Right, int Total);

/// <param name="Correct">Shu javob to'g'rimi.</param>
/// <param name="Reveal">Shu savolning to'g'ri javobi (javobdan keyin).</param>
/// <param name="ItemDone">Birinchi urinish yakunlanganmi (shu javob bilan yoki avval).</param>
/// <param name="First">Birinchi urinish natijasi — yakunlangan bo'lsa.</param>
public sealed record CheckItemResult(bool Correct, JsonObject Reveal, bool ItemDone, FirstScoreDto? First);

public sealed record UnitProgressDto(
    IReadOnlyList<string> BlocksDone,
    IReadOnlyDictionary<string, FirstScoreDto> Items,
    DateTimeOffset? CompletedAt,
    int ExerciseRight,
    int ExerciseTotal,
    TestResultSummaryDto? Test);

public sealed record TestResultSummaryDto(int Score, int Max, int OralCount, DateTimeOffset SubmittedAt);

public sealed record RevealRequest(int N);

public sealed record RevealResult(string? Namuna);

public sealed record AudioTicketDto(string Token, DateTimeOffset ExpiresAt);

public sealed record TestSubmitAnswer(int Index, int? Choice, string? Text);

public sealed record TestSubmitRequest(IReadOnlyList<TestSubmitAnswer> Answers);

public sealed record TestQuestionResultDto(int Index, bool? Correct, int? CorrectChoice, string? Javob, bool Oral);

/// <param name="Counted">Bu urinish ball beradimi (birinchi topshiriq).</param>
public sealed record TestSubmitResult(
    int Score,
    int Max,
    int OralCount,
    bool Counted,
    IReadOnlyList<TestQuestionResultDto> Questions);

// ---------------------------------------------------------------- xodim

public sealed record StaffUnitDto(
    long UnitId,
    int Version,
    string Book,
    string Code,
    long? LessonId,
    JsonNode Content,
    string? TeacherGuideMd);

public sealed record AdminUnitDto(
    long Id,
    string Code,
    int Number,
    string TitleUz,
    long? ModuleLessonId,
    string? LessonName,
    int? Version,
    string? ContentSha256);

public sealed record AdminImportRunDto(
    long Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    bool DryRun,
    string Result);

public sealed record AdminBookDto(
    long Id,
    string Code,
    string Title,
    string? Subtitle,
    long? CourseId,
    bool IsPublished,
    IReadOnlyList<AdminUnitDto> Units,
    IReadOnlyList<AdminImportRunDto> Runs);

public sealed record SetUnitLessonRequest(long? ModuleLessonId);

public sealed record PublishBookRequest(bool Published);
