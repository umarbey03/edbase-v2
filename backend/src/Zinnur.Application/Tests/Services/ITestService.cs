using Zinnur.Application.Common.Export;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Tests.Dtos;

namespace Zinnur.Application.Tests.Services;

/// <summary>
/// Onlayn testlar: tuzish (o'quv bo'limi) va yechish (o'quvchi).
///
/// IKKI KO'RINISH ATAYLAB AJRATILGAN: tuzish metodlari
/// <see cref="TestAuthoringDto"/> (to'g'ri javoblar bilan), yechish esa
/// <see cref="TakeTestDto"/> (to'g'ri javob maydoni UMUMAN yo'q) qaytaradi.
/// </summary>
public interface ITestService
{
    // ---------------------------------------------------------------- tuzish

    Task<PagedResult<TestDto>> ListAsync(
        TestListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>Tahrirlash ko'rinishi — TO'G'RI JAVOBLAR BILAN (faqat xodim).</summary>
    Task<TestAuthoringDto> GetForAuthoringAsync(
        long id, long actorId, CancellationToken ct = default);

    Task<TestDto> CreateAsync(
        CreateTestRequest request, long actorId, CancellationToken ct = default);

    Task<TestDto> UpdateAsync(
        long id, UpdateTestRequest request, long actorId, CancellationToken ct = default);

    Task DeleteAsync(long id, long actorId, CancellationToken ct = default);

    Task<AuthoringQuestionDto> AddQuestionAsync(
        long testId, SaveQuestionRequest request, long actorId, CancellationToken ct = default);

    Task<AuthoringQuestionDto> UpdateQuestionAsync(
        long testId, long questionId, SaveQuestionRequest request, long actorId,
        CancellationToken ct = default);

    Task DeleteQuestionAsync(
        long testId, long questionId, long actorId, CancellationToken ct = default);

    /// <summary>E'lon qilish — Domain bo'sh testni va nuqsonli savolni rad etadi.</summary>
    Task<TestDto> SetPublishedAsync(
        long id, bool published, long actorId, CancellationToken ct = default);

    Task<IReadOnlyList<TestResultRowDto>> ListResultsAsync(
        long id, long actorId, CancellationToken ct = default);

    Task<CsvExport> ExportResultsCsvAsync(
        long id, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- yechish

    Task<IReadOnlyList<AvailableTestDto>> ListAvailableAsync(
        long studentId, CancellationToken ct = default);

    /// <summary>
    /// Urinishni boshlaydi. IDEMPOTENT: allaqachon boshlangan bo'lsa AYNI
    /// urinish qaytadi (taymer noldan boshlanmaydi).
    /// </summary>
    Task<StartAttemptDto> StartAsync(
        long testId, long studentId, CancellationToken ct = default);

    /// <summary>Yechish varaqasi — to'g'ri javoblarSIZ.</summary>
    Task<TakeTestDto> GetForTakingAsync(
        long testId, long studentId, CancellationToken ct = default);

    /// <summary>Javoblarni topshiradi va SERVERDA baholaydi.</summary>
    Task<MyResultDto> SubmitAsync(
        long testId, SubmitTestRequest request, long studentId, CancellationToken ct = default);

    Task<MyResultDto> GetMyResultAsync(
        long testId, long studentId, CancellationToken ct = default);
}
