using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Progress.Dtos;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Progress.Services;

/// <inheritdoc cref="ILessonGradeSummaryService"/>
/// <remarks>
/// ★ N+1 YO'Q — jami IKKITA so'rov: (1) o'quvchining guruhlari,
/// (2) baholar dars va baholovchi bilan BITTA <c>JOIN</c> da. Darslar
/// soni (8 oyda ~70) javobga sig'adi, shuning uchun sahifalash
/// QO'SHILMADI — u <c>from</c>/<c>to</c> oralig'i bilan ham hal bo'ladi
/// (davomat xulosasidagi AYNI qaror).
/// </remarks>
public sealed class LessonGradeSummaryService(
    IApplicationDbContext db,
    IScheduleTimeZoneProvider timeZone) : ILessonGradeSummaryService
{
    public async Task<MyLessonGradesDto> GetMyGradesAsync(
        long studentId,
        long? groupId,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken ct = default)
    {
        await EnsureActiveAsync(studentId, ct);

        if (fromDate is { } start && toDate is { } end && start > end)
            throw Invalid("fromDate", "Boshlanish sanasi tugash sanasidan keyin bo'lishi mumkin emas.");

        var groupIds = await ResolveGroupIdsAsync(studentId, groupId, ct);

        if (groupIds.Count == 0)
            return Empty(groupIds, fromDate, toDate);

        var zone = timeZone.TimeZone;

        // Mahalliy sana -> UTC oralig'i. `to` KIRADI, shuning uchun keyingi
        // kunning boshi olinadi (`AttendanceSummaryService` dagi AYNI qoida —
        // ikki ekran bir xil sanani bir xil tushunishi shart).
        var fromUtc = fromDate is { } f ? LocalWallClock.StartOfDayUtc(f, zone) : (DateTimeOffset?)null;
        var toUtc = toDate is { } t ? LocalWallClock.StartOfDayUtc(t.AddDays(1), zone) : (DateTimeOffset?)null;

        // ★ QIDIRUV `LessonGrades` DAN BOSHLANADI, `LiveSessions` DAN EMAS:
        //   baho qo'yilmagan dars bu ekranda UMUMAN qatnashmaydi (maxraj
        //   ham shundan iborat — sabab `MyLessonGradesDto.AveragePercent`
        //   izohida). Darslar tomonidan boshlansa har dars uchun `LEFT JOIN`
        //   va keyin bo'shlarini tashlash kerak bo'lardi.
        //
        // 🔴 `g.StudentId == studentId` — MAXFIYLIKNING YAGONA QATORI.
        //   `studentId` tokendan keladi (servis boshqa Id'ni qabul
        //   qilmaydi), ya'ni bu filtr tushib qolsa test darhol qizaradi.
        var records = await db.LessonGrades.AsNoTracking()
            .Where(g => g.StudentId == studentId
                     && groupIds.Contains(g.Session!.GroupId)
                     && (fromUtc == null || g.Session!.ScheduledStart >= fromUtc)
                     && (toUtc == null || g.Session!.ScheduledStart < toUtc))
            .OrderByDescending(g => g.Session!.ScheduledStart)
            .ThenByDescending(g => g.SessionId)
            .Select(g => new GradeRow(
                g.SessionId,
                g.Session!.GroupId,
                g.Session.Title,
                g.Session.Type,
                g.Session.ScheduledStart,
                g.Score,
                g.MaxScore,
                g.Comment,
                g.GradedBy!.FullName,
                g.GradedAt))
            .ToListAsync(ct);

        // ★ FOIZ VA YORLIQ XOTIRADA: `Math.Round` va `enum.ToString()` ni
        //   so'rov ichida yozish ularni SQL'ga tarjima qilishga majbur
        //   qilardi (yoki tarjima bo'lmay, butun so'rov klientga tushib
        //   ketardi). `LessonGradeService.BuildRowsAsync` ham aynan
        //   shunday: tor proyeksiya -> keyin xotirada shakl.
        var items = records.ConvertAll(r => new MyLessonGradeDto(
            r.SessionId,
            r.GroupId,
            r.Title,
            r.Type.ToString(),
            r.ScheduledStart,
            r.Score,
            r.MaxScore ?? LessonGrade.DefaultMaxScore,
            PercentOf(r.Score, r.MaxScore),
            r.Comment,
            r.GradedByName,
            r.GradedAt));

        return new MyLessonGradesDto(
            groupIds,
            fromDate,
            toDate,
            LessonGrade.DefaultMaxScore,
            items.Count,

            // O'rtacha — FOIZLARNING o'rtachasi, ballarning emas: turli
            // shkaladagi baholar (5 ballik dars va 100 ballik imtihon)
            // aks holda qo'shilib ketardi.
            items.Count == 0 ? null : Math.Round(items.Average(i => i.Percent), 1),
            items);
    }

    /// <summary>
    /// Foiz — <c>LessonGrade.Percent</c> ning proyeksiya uchun nusxasi
    /// (<c>LessonGradeService.PercentOf</c> bilan AYNI qator).
    ///
    /// Ikkala nusxa ham <c>Math.Round(..., 1)</c>: reyting, xodim varag'i
    /// va o'quvchi ekrani bitta sonni uch xil yaxlitlashi mumkin emas.
    /// </summary>
    private static decimal PercentOf(decimal score, decimal? maxScore)
    {
        var max = maxScore ?? LessonGrade.DefaultMaxScore;
        return max > 0 ? Math.Round(score / max * 100m, 1) : 0m;
    }

    // ---------------------------------------------------------------- yordamchi

    /// <summary>
    /// Qaysi guruhlar hisobga olinadi.
    ///
    /// ★ QOIDA <c>AttendanceSummaryService.ResolveGroupIdsAsync</c> NING
    /// AYNAN NUSXASI — ataylab. Ikkala ekran (davomat va baholar)
    /// o'quvchining AYNI guruhlar to'plamini ko'rsatishi shart, aks holda
    /// u bir sahifada ko'rgan darsni ikkinchisida topa olmasdi. Qoidani
    /// umumiy bazaviy sinfga chiqarish ham mumkin edi, lekin loyihada bu
    /// juftlik uchun TAKRORLASH ataylab tanlangan (sabab
    /// <c>ILessonGradeService</c> izohida: qoidalar kelajakda ajralishi
    /// mumkin).
    ///
    /// ⚠️ QOLDIQ CHEKLOV (ochiq qaror — arxivlangan guruh reytingi):
    /// filtr FAOL a'zolik va FAOL guruh bo'yicha, ya'ni bitirgan o'quvchi
    /// o'z tarixini KO'RMAYDI. Bu ATAYLAB davomat bilan bir xil qoldirildi:
    /// arxiv masalasi ikkala ekran uchun BIRGA hal qilinishi kerak.
    /// </summary>
    private async Task<IReadOnlyList<long>> ResolveGroupIdsAsync(
        long studentId, long? groupId, CancellationToken ct)
    {
        var mine = await db.GroupMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId
                     && m.Status == MemberStatus.Active
                     && m.Group!.IsActive)
            .Select(m => m.GroupId)
            .Distinct()
            .ToListAsync(ct);

        if (groupId is not { } wanted)
            return mine;

        if (!mine.Contains(wanted))
            throw new ForbiddenException("Bu guruh ma'lumotiga ruxsatingiz yo'q.");

        return [wanted];
    }

    /// <summary>
    /// Foydalanuvchi bor va FAOLMI.
    ///
    /// Rol TEKSHIRILMAYDI — bu "meniki" so'rovi: xodim chaqirsa a'zoligi
    /// yo'qligi uchun bo'sh javob oladi (davomat xulosasidagi AYNI xulq).
    /// </summary>
    private async Task EnsureActiveAsync(long studentId, CancellationToken ct)
    {
        var isActive = await db.Users.AsNoTracking()
            .Where(u => u.Id == studentId)
            .Select(u => (bool?)u.IsActive)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), studentId);

        if (!isActive)
            throw new ForbiddenException("Profilingiz faol emas.");
    }

    private static MyLessonGradesDto Empty(
        IReadOnlyList<long> groupIds, DateOnly? from, DateOnly? to) =>
        new(groupIds, from, to, LessonGrade.DefaultMaxScore, 0, null, []);

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    /// <summary>Bazadan o'qiladigan TOR proyeksiya (entity yasalmaydi).</summary>
    private sealed record GradeRow(
        long SessionId,
        long GroupId,
        string? Title,
        SessionType Type,
        DateTimeOffset ScheduledStart,
        decimal Score,
        decimal? MaxScore,
        string? Comment,
        string? GradedByName,
        DateTimeOffset GradedAt);
}
