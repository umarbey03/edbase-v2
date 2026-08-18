using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Students.Services;

/// <inheritdoc cref="IStudiedLessonCounter"/>
public sealed class StudiedLessonCounter(IApplicationDbContext db) : IStudiedLessonCounter
{
    /// <inheritdoc />
    public Task<int> CountAsync(long studentId, CancellationToken ct = default) =>
        Studied().Where(a => a.StudentId == studentId).CountAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, int>> CountManyAsync(
        IReadOnlyCollection<long> studentIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(studentIds);

        if (studentIds.Count == 0) return new Dictionary<long, int>();

        var ids = studentIds as IList<long> ?? [.. studentIds];

        return await Studied()
            .Where(a => ids.Contains(a.StudentId))
            .GroupBy(a => a.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StudentId, x => x.Count, ct);
    }

    /// <summary>
    /// "O'tilgan dars" ta'rifi — BITTA joyda. Uch shart:
    ///   • dars YAKUNLANGAN (rejadagi yoki bekor qilingani sanalmaydi);
    ///   • USTOZ darsi (kurator mashg'uloti kurs mavzusini surmaydi);
    ///   • o'quvchi UNDA BO'LGAN (kelmagan dars sanalmaydi).
    /// Sabab va tarixi <see cref="IStudiedLessonCounter"/> izohida.
    /// </summary>
    private IQueryable<Attendance> Studied() =>
        db.Attendances
            .AsNoTracking()
            .Where(a => a.Session!.Status == SessionStatus.Ended
                && a.Session.Type == SessionType.Teacher
                && a.Status != AttendanceStatus.Absent);
}
