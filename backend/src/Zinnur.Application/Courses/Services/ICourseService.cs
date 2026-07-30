using Zinnur.Application.Common.Models;
using Zinnur.Application.Courses.Dtos;

namespace Zinnur.Application.Courses.Services;

/// <summary>
/// Kurs kontenti use-case'lari (kurs -> modul -> dars).
///
/// Har metod <c>actorId</c> oladi: ruxsat qoidasi SERVIS ichida tekshiriladi,
/// controller atributi faqat darvoza (batafsil: <see cref="CourseService"/>).
/// </summary>
public interface ICourseService
{
    // ---------------------------------------------------------------- kurs
    Task<PagedResult<CourseDto>> ListAsync(
        CourseListQuery query, long actorId, CancellationToken ct = default);

    /// <summary>Kurs daraxti. O'quvchi uchun gating qo'llanadi.</summary>
    Task<CourseTreeDto> GetAsync(long id, long actorId, CancellationToken ct = default);

    Task<CourseDto> CreateAsync(
        CreateCourseRequest request, long actorId, CancellationToken ct = default);

    Task<CourseDto> UpdateAsync(
        long id, UpdateCourseRequest request, long actorId, CancellationToken ct = default);

    Task DeleteAsync(long id, long actorId, CancellationToken ct = default);

    Task<IReadOnlyList<PositionDto>> ReorderCoursesAsync(
        ReorderRequest request, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- modul
    Task<CourseModuleDto> CreateModuleAsync(
        long courseId, CreateModuleRequest request, long actorId, CancellationToken ct = default);

    Task<CourseModuleDto> UpdateModuleAsync(
        long courseId, long moduleId, UpdateModuleRequest request, long actorId,
        CancellationToken ct = default);

    Task DeleteModuleAsync(
        long courseId, long moduleId, long actorId, CancellationToken ct = default);

    Task<IReadOnlyList<PositionDto>> ReorderModulesAsync(
        long courseId, ReorderRequest request, long actorId, CancellationToken ct = default);

    // ---------------------------------------------------------------- dars
    Task<CourseLessonDto> CreateLessonAsync(
        long courseId, long moduleId, CreateLessonRequest request, long actorId,
        CancellationToken ct = default);

    Task<CourseLessonDto> UpdateLessonAsync(
        long courseId, long moduleId, long lessonId, UpdateLessonRequest request, long actorId,
        CancellationToken ct = default);

    Task DeleteLessonAsync(
        long courseId, long moduleId, long lessonId, long actorId, CancellationToken ct = default);

    Task<IReadOnlyList<PositionDto>> ReorderLessonsAsync(
        long courseId, long moduleId, ReorderRequest request, long actorId,
        CancellationToken ct = default);
}
