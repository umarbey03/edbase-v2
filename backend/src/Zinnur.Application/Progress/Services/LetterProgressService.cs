using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Gating.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Progress.Services;

/// <param name="State">Mastered, Learning yoki Locked.</param>
/// <param name="LessonNumber">Harf o'rgatiladigan dars raqami (1 dan); dars belgilanmagan bo'lsa <c>null</c>.</param>
public sealed record LetterProgressDto(
    string Letter, string State, long? LessonId, int? LessonNumber, string? LessonName);

public sealed record MyLettersDto(int Mastered, int Total, IReadOnlyList<LetterProgressDto> Letters);

/// <summary>Kurs quruvchi: darsning harflari.</summary>
public sealed record LessonLettersDto(long LessonId, long ModuleId, int Number, string LessonName, string? Letters);

public sealed record SetLessonLettersRequest(string? Letters);

public interface ILetterProgressService
{
    Task<MyLettersDto> GetMineAsync(long studentId, CancellationToken ct = default);

    Task<IReadOnlyList<LessonLettersDto>> ListCourseLessonsAsync(long courseId, CancellationToken ct = default);

    Task<LessonLettersDto> SetLessonLettersAsync(long courseId, long lessonId, string? letters, CancellationToken ct = default);
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════
/// PROFIL: HARFLAR JADVALI (2026-11-21, loyiha egasi g'oyasi)
/// ════════════════════════════════════════════════════════════════════
///
/// 28 harf alifbo tartibida; har biri o'rgatiladigan darsning holatini oladi:
///   • Mastered — dars o'zlashtirilgan (Mustaqil: kurator qarori; guruh:
///     dars bajarilgan — gating'dagi "Completed");
///   • Learning — dars ochiq, lekin hali o'zlashtirilmagan;
///   • Locked   — dars hali yopiq yoki harf hech bir darsga belgilanmagan.
///
/// Holat manbai — <see cref="IGatingService.GetCourseGateAsync"/> (keshlangan,
/// o'quv yo'li ekrani bilan AYNI qoida), dars ↔ harf bog'lanishi esa
/// <see cref="ModuleLesson.Letters"/> (Kurs quruvchida tahrirlanadi).
/// </summary>
public sealed class LetterProgressService(IApplicationDbContext db, IGatingService gating) : ILetterProgressService
{
    public async Task<MyLettersDto> GetMineAsync(long studentId, CancellationToken ct = default)
    {
        var gate = await gating.GetCourseGateAsync(studentId, ct).ConfigureAwait(false);
        List<long> lessonIds = [.. gate.Lessons.Select(l => l.LessonId)];

        var lessons = lessonIds.Count == 0
            ? []
            : await db.ModuleLessons.AsNoTracking()
                .Where(l => lessonIds.Contains(l.Id) && l.Letters != null)
                .Select(l => new { l.Id, l.Name, l.Letters })
                .ToListAsync(ct)
                .ConfigureAwait(false);

        var gateById = gate.Lessons.ToDictionary(l => l.LessonId);

        // Harf -> uni o'rgatadigan BIRINCHI dars (kurs tartibida).
        var byLetter = new Dictionary<string, (long LessonId, int Number, string Name, string State)>();
        foreach (var lesson in lessons.OrderBy(l => gateById[l.Id].Index))
        {
            var g = gateById[lesson.Id];
            var mastered = g.RequiresMastery ? g.Mastery == MasteryStatus.Mastered : g.Completed;
            var state = mastered ? "Mastered" : g.Unlocked ? "Learning" : "Locked";

            foreach (var letter in ArabicAlphabet.Split(lesson.Letters))
                byLetter.TryAdd(letter, (lesson.Id, g.Index + 1, lesson.Name, state));
        }

        var rows = ArabicAlphabet.Letters
            .Select(letter => byLetter.TryGetValue(letter, out var x)
                ? new LetterProgressDto(letter, x.State, x.LessonId, x.Number, x.Name)
                : new LetterProgressDto(letter, "Locked", null, null, null))
            .ToList();

        return new MyLettersDto(rows.Count(r => r.State == "Mastered"), rows.Count, rows);
    }

    public async Task<IReadOnlyList<LessonLettersDto>> ListCourseLessonsAsync(long courseId, CancellationToken ct = default)
    {
        var rows = await db.ModuleLessons.AsNoTracking()
            .Where(l => l.Module!.CourseId == courseId)
            .OrderBy(l => l.Module!.Position).ThenBy(l => l.Position).ThenBy(l => l.Id)
            .Select(l => new { l.Id, l.ModuleId, l.Name, l.Letters })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. rows.Select((l, i) => new LessonLettersDto(l.Id, l.ModuleId, i + 1, l.Name, l.Letters))];
    }

    public async Task<LessonLettersDto> SetLessonLettersAsync(
        long courseId, long lessonId, string? letters, CancellationToken ct = default)
    {
        var lesson = await db.ModuleLessons
            .FirstOrDefaultAsync(l => l.Id == lessonId && l.Module!.CourseId == courseId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("Dars", lessonId);

        // Noto'g'ri kiritish — forma xatosi (400), holat to'qnashuvi (409) emas.
        try
        {
            lesson.Letters = ArabicAlphabet.Normalize(letters);
        }
        catch (Zinnur.Domain.Exceptions.DomainException ex)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["letters"] = [ex.Message] });
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var all = await ListCourseLessonsAsync(courseId, ct).ConfigureAwait(false);
        return all.First(l => l.LessonId == lessonId);
    }
}
