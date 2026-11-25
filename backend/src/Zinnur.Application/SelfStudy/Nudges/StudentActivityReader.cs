using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;

namespace Zinnur.Application.SelfStudy.Nudges;

/// <summary>
/// O'quvchining O'QUV faolligi — «oxirgi marta qachon nimadir qildi».
///
/// ★ NIMA FAOLLIK HISOBLANADI: video ko'rish, vazifa topshirish, test
/// boshlash, interaktiv darslik va kuratorga yozish. Ilovani shunchaki
/// ochish HISOBLANMAYDI — ataylab: eslatmaning maqsadi kirish emas, o'qish.
/// Bunday signal bazada alohida saqlanmaydi ham, ya'ni yangi jadval yoki
/// har so'rovda yozuv kerak bo'lmaydi.
/// </summary>
public interface IStudentActivityReader
{
    /// <summary>Har o'quvchining oxirgi faolligi (hech narsa qilmagan bo'lsa — lug'atda yo'q).</summary>
    Task<IReadOnlyDictionary<long, DateTimeOffset>> LastActivityAsync(
        IReadOnlyCollection<long> studentIds, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="since"/> dan keyingi BARCHA faollik paytlari (o'lchov uchun:
    /// «eslatmadan keyin 48 soat ichida qaytdimi»).
    /// </summary>
    Task<IReadOnlyDictionary<long, IReadOnlyList<DateTimeOffset>>> ActivitySinceAsync(
        IReadOnlyCollection<long> studentIds, DateTimeOffset since, CancellationToken ct = default);
}

public sealed class StudentActivityReader(IApplicationDbContext db) : IStudentActivityReader
{
    public async Task<IReadOnlyDictionary<long, DateTimeOffset>> LastActivityAsync(
        IReadOnlyCollection<long> studentIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(studentIds);

        var result = new Dictionary<long, DateTimeOffset>();
        if (studentIds.Count == 0) return result;

        var ids = studentIds.Distinct().ToList();

        void Merge(IEnumerable<(long StudentId, DateTimeOffset? At)> rows)
        {
            foreach (var (studentId, at) in rows)
            {
                if (at is not { } value) continue;
                if (!result.TryGetValue(studentId, out var current) || value > current)
                    result[studentId] = value;
            }
        }

        // ★ HAR JADVAL ALOHIDA, GURUHLANGAN SO'ROV: oltita jadvalni bitta
        //   UNION ga yig'ish EF'da tarjima qilinmaydi, alohida so'rovlarning
        //   har biri esa (StudentId) indeksiga tushadi.

        // Video: ko'rilgan payt. `UpdatedAt` OLINMAYDI — uni kurator/admin
        // istisnosi (`SetOverride`) ham o'zgartiradi, bu o'quvchi faolligi emas.
        Merge((await db.LessonProgress.AsNoTracking()
                .Where(p => ids.Contains(p.StudentId) && p.VideoWatchedAt != null)
                .GroupBy(p => p.StudentId)
                .Select(g => new { g.Key, At = g.Max(p => p.VideoWatchedAt) })
                .ToListAsync(ct))
            .Select(r => (r.Key, r.At)));

        Merge((await db.Submissions.AsNoTracking()
                .Where(s => ids.Contains(s.StudentId))
                .GroupBy(s => s.StudentId)
                .Select(g => new { g.Key, At = g.Max(s => (DateTimeOffset?)s.SubmittedAt) })
                .ToListAsync(ct))
            .Select(r => (r.Key, r.At)));

        Merge((await db.TestAttempts.AsNoTracking()
                .Where(a => ids.Contains(a.StudentId))
                .GroupBy(a => a.StudentId)
                .Select(g => new { g.Key, At = g.Max(a => (DateTimeOffset?)a.StartedAt) })
                .ToListAsync(ct))
            .Select(r => (r.Key, r.At)));

        Merge((await db.InteractiveUnitProgress.AsNoTracking()
                .Where(p => ids.Contains(p.StudentId))
                .GroupBy(p => p.StudentId)
                .Select(g => new { g.Key, At = g.Max(p => (DateTimeOffset?)(p.UpdatedAt ?? p.CreatedAt)) })
                .ToListAsync(ct))
            .Select(r => (r.Key, r.At)));

        Merge((await db.InteractiveTestAttempts.AsNoTracking()
                .Where(a => ids.Contains(a.StudentId))
                .GroupBy(a => a.StudentId)
                .Select(g => new { g.Key, At = g.Max(a => (DateTimeOffset?)a.StartedAt) })
                .ToListAsync(ct))
            .Select(r => (r.Key, r.At)));

        // Kuratorga O'ZI yozgan xabar — savol berish ham o'qish.
        Merge((await db.DirectMessages.AsNoTracking()
                .Where(m => ids.Contains(m.StudentId) && m.SenderId == m.StudentId)
                .GroupBy(m => m.StudentId)
                .Select(g => new { g.Key, At = g.Max(m => (DateTimeOffset?)m.SentAt) })
                .ToListAsync(ct))
            .Select(r => (r.Key, r.At)));

        return result;
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<DateTimeOffset>>> ActivitySinceAsync(
        IReadOnlyCollection<long> studentIds, DateTimeOffset since, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(studentIds);

        if (studentIds.Count == 0) return new Dictionary<long, IReadOnlyList<DateTimeOffset>>();

        var ids = studentIds.Distinct().ToList();
        var all = new List<(long StudentId, DateTimeOffset At)>();

        all.AddRange((await db.LessonProgress.AsNoTracking()
                .Where(p => ids.Contains(p.StudentId) && p.VideoWatchedAt != null && p.VideoWatchedAt >= since)
                .Select(p => new { p.StudentId, At = p.VideoWatchedAt!.Value })
                .ToListAsync(ct))
            .Select(r => (r.StudentId, r.At)));

        all.AddRange((await db.Submissions.AsNoTracking()
                .Where(s => ids.Contains(s.StudentId) && s.SubmittedAt >= since)
                .Select(s => new { s.StudentId, At = s.SubmittedAt })
                .ToListAsync(ct))
            .Select(r => (r.StudentId, r.At)));

        all.AddRange((await db.TestAttempts.AsNoTracking()
                .Where(a => ids.Contains(a.StudentId) && a.StartedAt >= since)
                .Select(a => new { a.StudentId, At = a.StartedAt })
                .ToListAsync(ct))
            .Select(r => (r.StudentId, r.At)));

        all.AddRange((await db.InteractiveUnitProgress.AsNoTracking()
                .Where(p => ids.Contains(p.StudentId) && (p.UpdatedAt ?? p.CreatedAt) >= since)
                .Select(p => new { p.StudentId, At = p.UpdatedAt ?? p.CreatedAt })
                .ToListAsync(ct))
            .Select(r => (r.StudentId, r.At)));

        all.AddRange((await db.InteractiveTestAttempts.AsNoTracking()
                .Where(a => ids.Contains(a.StudentId) && a.StartedAt >= since)
                .Select(a => new { a.StudentId, At = a.StartedAt })
                .ToListAsync(ct))
            .Select(r => (r.StudentId, r.At)));

        all.AddRange((await db.DirectMessages.AsNoTracking()
                .Where(m => ids.Contains(m.StudentId) && m.SenderId == m.StudentId && m.SentAt >= since)
                .Select(m => new { m.StudentId, At = m.SentAt })
                .ToListAsync(ct))
            .Select(r => (r.StudentId, r.At)));

        return all
            .GroupBy(x => x.StudentId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DateTimeOffset>)g.Select(x => x.At).OrderBy(x => x).ToList());
    }
}
