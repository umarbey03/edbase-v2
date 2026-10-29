using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Gating.Services;
using Zinnur.Application.Interactive.Access;
using Zinnur.Application.Interactive.Content;
using Zinnur.Application.Interactive.Dtos;
using Zinnur.Application.Interactive.Grading;
using Zinnur.Application.Media;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using static Zinnur.Application.Interactive.Content.UnitJson;

namespace Zinnur.Application.Interactive.Services;

/// <summary>
/// Interaktiv darslik — o'quvchi va xodim use-case'lari (2026-10-29).
/// </summary>
public interface IInteractiveUnitService
{
    Task<LessonBookDto> GetLessonBookAsync(long lessonId, long actorId, CancellationToken ct = default);

    /// <summary>O'quvchi proyeksiyasi + progress (xodimga — oldindan ko'rish, progresssiz).</summary>
    Task<JsonObject> GetUnitAsync(long unitId, long actorId, CancellationToken ct = default);

    Task<StaffUnitDto> GetStaffUnitAsync(long unitId, long actorId, CancellationToken ct = default);

    Task<AudioTicketDto> CreateAudioTicketAsync(long unitId, long actorId, CancellationToken ct = default);

    /// <summary>Chiptadan foydalanuvchini aniqlaydi (sessiya bo'lmaganda).</summary>
    long? ResolveTicket(string? ticket, long unitId);

    Task<LessonAssetDownload> OpenAudioAsync(
        long unitId, string handle, string? rangeHeader, long actorId, CancellationToken ct = default);

    Task<CheckItemResult> CheckAsync(
        long unitId, string itemKey, CheckItemRequest request, long actorId, CancellationToken ct = default);

    Task<UnitProgressDto> MarkBlockDoneAsync(long unitId, string blockId, long actorId, CancellationToken ct = default);

    Task<RevealResult> RevealAsync(long unitId, string blockId, int n, long actorId, CancellationToken ct = default);

    Task<JsonObject> StartTestAsync(long unitId, long actorId, CancellationToken ct = default);

    Task<TestSubmitResult> SubmitTestAsync(
        long unitId, TestSubmitRequest request, long actorId, CancellationToken ct = default);

    Task<IReadOnlyList<AdminBookDto>> ListBooksAsync(long actorId, CancellationToken ct = default);

    Task<AdminUnitDto> SetUnitLessonAsync(long unitId, long? moduleLessonId, long actorId, CancellationToken ct = default);

    Task<AdminBookDto> PublishAsync(long bookId, bool published, long actorId, CancellationToken ct = default);
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// INTERAKTIV DARSLIK SERVISI (2026-10-29)
/// ════════════════════════════════════════════════════════════════════════
///
/// 🔴 HAR O'QUVCHI SO'ROVI <see cref="EnsureStudentAsync"/> dan o'tadi:
///   (1) huquq (<see cref="IInteractiveBookAccess"/>), (2) birlik darsga
///   bog'langan, kitob nashr qilingan va dars huquq berilgan kursda,
///   (3) dars gating bo'yicha ochiq. Audio ham HAR so'rovda shu tekshiruvdan
///   o'tadi (chipta faqat "kim" ekanini aytadi).
///
/// ★ Huquqsiz o'quvchiga 404 (egasi qarori 10): birlik borligi ham oshkor
///   qilinmaydi, kontentni sanab bo'lmaydi. Qulflangan darsga — 403 (dars
///   o'quvchiga baribir ko'rinadi, sababi aytiladi).
///
/// ★ Darslik GATINGGA TA'SIR QILMAYDI (egasi qarori 2): bu servis
///   `IGatingService` dan faqat O'QIYDI.
/// </summary>
public sealed class InteractiveUnitService(
    IApplicationDbContext db,
    IInteractiveBookAccess access,
    IGatingService gating,
    IMediaStorage storage,
    StudentUnitProjector projector,
    AudioHandleCodec audio,
    MatchTokenCodec match,
    UnitTicketCodec tickets,
    TimeProvider clock) : IInteractiveUnitService
{
    private const string NotFoundEntity = "InteractiveUnit";

    // ================================================================ dars kartasi

    public async Task<LessonBookDto> GetLessonBookAsync(long lessonId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);

        var unit = await db.InteractiveUnits.AsNoTracking()
            .Where(u => u.ModuleLessonId == lessonId && u.CurrentVersionId != null)
            .Select(u => new
            {
                u.Id,
                Book = u.Book!.Code,
                u.Book.IsPublished,
                u.Code,
                u.TitleUz,
                u.TitleAr,
                CourseId = u.ModuleLesson!.Module!.CourseId,
            })
            .FirstOrDefaultAsync(ct);

        if (actor.Role != UserRole.Student)
        {
            return unit is null
                ? new LessonBookDto("staff", false, null)
                : new LessonBookDto("staff", true, new LessonUnitDto(
                    unit.Id, unit.Book, unit.Code, unit.TitleUz, unit.TitleAr,
                    await SummaryAsync(unit.Id, null, ct)));
        }

        var decision = await access.GetAsync(actor.Id, ct);
        var visible = unit is not null && unit.IsPublished && unit.CourseId == decision.CourseId;

        if (!visible || decision.Status == BookAccessStatus.None)
            return new LessonBookDto("none", false, null);

        if (decision.Status == BookAccessStatus.Upsell)
            return new LessonBookDto("upsell", true, null);

        return new LessonBookDto("granted", true, new LessonUnitDto(
            unit!.Id, unit.Book, unit.Code, unit.TitleUz, unit.TitleAr,
            await SummaryAsync(unit.Id, actor.Id, ct)));
    }

    // ================================================================ birlik

    public async Task<JsonObject> GetUnitAsync(long unitId, long actorId, CancellationToken ct = default)
    {
        var ctx = await EnsureReaderAsync(unitId, actorId, ct);

        var projection = projector.Project(ctx.Content, ctx.VersionId);
        var result = new JsonObject
        {
            ["unitId"] = ctx.UnitId,
            ["version"] = ctx.Version,
            ["versionId"] = ctx.VersionId,
            ["book"] = ctx.Book,
            ["code"] = ctx.Code,
            ["lessonId"] = ctx.LessonId,
            ["projector"] = StudentUnitProjector.ProjectorVersion,
        };

        foreach (var (key, value) in projection.ToList())
        {
            projection.Remove(key);
            result[key] = value;
        }

        var progress = ctx.IsStudent ? await ProgressAsync(ctx.UnitId, ctx.ActorId, ct) : EmptyProgress();
        result["progress"] = JsonSerializer.SerializeToNode(progress, JsonOptions);

        return result;
    }

    public async Task<StaffUnitDto> GetStaffUnitAsync(long unitId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        if (actor.Role == UserRole.Student)
            throw new ForbiddenException("Bu ko'rinish faqat xodimlar uchun.");

        var row = await db.InteractiveUnits.AsNoTracking()
            .Where(u => u.Id == unitId && u.CurrentVersionId != null)
            .Select(u => new
            {
                u.Id,
                Book = u.Book!.Code,
                u.Code,
                u.ModuleLessonId,
                u.CurrentVersion!.Version,
                u.CurrentVersion.ContentRaw,
                u.CurrentVersion.TeacherGuideMd,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(NotFoundEntity, unitId);

        return new StaffUnitDto(
            row.Id, row.Version, row.Book, row.Code, row.ModuleLessonId,
            UnitJson.Parse(row.ContentRaw), row.TeacherGuideMd);
    }

    // ================================================================ audio

    public async Task<AudioTicketDto> CreateAudioTicketAsync(long unitId, long actorId, CancellationToken ct = default)
    {
        var ctx = await EnsureReaderAsync(unitId, actorId, ct, parseContent: false);
        var (token, expires) = tickets.Issue(ctx.UnitId, ctx.ActorId);
        return new AudioTicketDto(token, expires);
    }

    public long? ResolveTicket(string? ticket, long unitId) => tickets.TryResolveUserId(ticket, unitId);

    public async Task<LessonAssetDownload> OpenAudioAsync(
        long unitId, string handle, string? rangeHeader, long actorId, CancellationToken ct = default)
    {
        var ctx = await EnsureReaderAsync(unitId, actorId, ct, parseContent: false);

        var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(ctx.AudioManifest)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        var audioId = audio.Resolve(ctx.VersionId, manifest.Keys, handle)
            ?? throw new NotFoundException("InteractiveAudio", handle);

        var sha = manifest[audioId];
        var file = await db.InteractiveAudio.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Sha256 == sha, ct)
            ?? throw new NotFoundException("InteractiveAudio", handle);

        if (!storage.IsConfigured)
            throw new ServiceUnavailableException("Fayl ombori sozlanmagan — audio ochilmadi.");

        var outcome = RangeHeader.TryParse(rangeHeader, file.SizeBytes, out var range);
        if (outcome == RangeParseOutcome.Unsatisfiable)
            throw new RangeNotSatisfiableException(file.SizeBytes);

        var requested = outcome == RangeParseOutcome.Satisfiable ? range : null;

        var stored = await storage.OpenReadAsync(file.ObjectKey, requested, ct)
            ?? throw new NotFoundException("InteractiveAudio", handle);

        // ★ Fayl nomi — belgi, audio id EMAS (id javobni oshkor qilishi mumkin).
        return new LessonAssetDownload(
            stored,
            "audio/mpeg",
            handle + ".mp3",
            stored.TotalLength ?? file.SizeBytes,
            stored.IsPartial ? requested : null);
    }

    // ================================================================ tekshiruv

    public async Task<CheckItemResult> CheckAsync(
        long unitId, string itemKey, CheckItemRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ctx = await EnsureReaderAsync(unitId, actorId, ct);
        var item = AnswerChecker.Find(ctx.Content, itemKey)
            ?? throw new NotFoundException("InteractiveItem", itemKey);

        CheckVerdict verdict;
        try
        {
            verdict = AnswerChecker.Check(
                item,
                new CheckAnswer(request.Index, request.Choice, request.Value, request.Left, request.Right,
                    request.Word, request.Cells, request.Words, request.Text),
                match,
                ctx.VersionId);
        }
        catch (InvalidAnswerException ex)
        {
            throw Invalid("answer", ex.Message);
        }

        // Xodim (ustoz) oldindan ko'rishda tekshiradi, lekin natija yozilmaydi.
        if (!ctx.IsStudent) return new CheckItemResult(verdict.Correct, verdict.Reveal, false, null);

        // 🔴 Parallel ikki javobda birinchi urinish bir marta yozilsin:
        //    noyob indeks + `xmin`. Yutqazgan so'rov holatni qayta o'qib bir marta takrorlaydi.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var outcome = await RecordAsync(ctx, item, verdict, ct);
                return new CheckItemResult(verdict.Correct, verdict.Reveal, outcome is not null, outcome);
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                DetachInteractive();
            }
        }
    }

    private async Task<FirstScoreDto?> RecordAsync(
        ReaderContext ctx, GradableItem item, CheckVerdict verdict, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var row = await db.InteractiveItemResults
            .FirstOrDefaultAsync(r => r.StudentId == ctx.ActorId && r.UnitId == ctx.UnitId && r.ItemKey == item.Key, ct);

        if (row is null)
        {
            row = new InteractiveItemResult
            {
                StudentId = ctx.ActorId,
                UnitId = ctx.UnitId,
                VersionId = ctx.VersionId,
                ItemKey = item.Key,
                CreatedAt = now,
            };
            db.InteractiveItemResults.Add(row);
        }

        if (row.IsFirstDone)
        {
            // Mashq qilish: natija o'zgarmaydi.
            row.Attempts++;
            row.LastAt = now;
            if (verdict.Right is { } right) row.LastRight = right;
            row.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return new FirstScoreDto(row.FirstRight ?? 0, row.FirstTotal ?? 0);
        }

        var state = FirstAttemptState.Read(row.State);
        var score = FirstAttempt.Apply(state, item, verdict);
        row.State = state.Write();
        row.VersionId = ctx.VersionId;
        row.UpdatedAt = now;

        if (score is { } done)
        {
            row.FirstRight = done.Right;
            row.FirstTotal = done.Total;
            row.FirstAt = now;
            row.Attempts = 1;
            row.LastRight = done.Right;
            row.LastAt = now;

            var progress = await ProgressRowAsync(ctx, now, ct);
            progress.ExerciseRight += done.Right;
            progress.ExerciseTotal += done.Total;
            progress.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        return score is { } s ? new FirstScoreDto(s.Right, s.Total) : null;
    }

    public async Task<UnitProgressDto> MarkBlockDoneAsync(
        long unitId, string blockId, long actorId, CancellationToken ct = default)
    {
        var ctx = await EnsureReaderAsync(unitId, actorId, ct);

        var blockIds = Blocks(ctx.Content).Select(b => Str(b.Block, "id")).OfType<string>().ToList();
        if (!blockIds.Contains(blockId, StringComparer.Ordinal))
            throw new NotFoundException("InteractiveBlock", blockId);

        if (!ctx.IsStudent) return EmptyProgress();

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var now = clock.GetUtcNow();
                var progress = await ProgressRowAsync(ctx, now, ct);
                var done = ReadIds(progress.BlocksDone);

                if (!done.Contains(blockId, StringComparer.Ordinal))
                {
                    done.Add(blockId);
                    progress.BlocksDone = JsonSerializer.Serialize(done);
                    progress.UpdatedAt = now;
                }

                // Barcha bloklar bajarildi — birlik yakunlandi (ball shu paytdan).
                if (progress.CompletedAt is null && blockIds.All(id => done.Contains(id, StringComparer.Ordinal)))
                    progress.CompletedAt = now;

                progress.LastVersionId = ctx.VersionId;
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                DetachInteractive();
            }
        }

        return await ProgressAsync(ctx.UnitId, ctx.ActorId, ct);
    }

    public async Task<RevealResult> RevealAsync(
        long unitId, string blockId, int n, long actorId, CancellationToken ct = default)
    {
        var ctx = await EnsureReaderAsync(unitId, actorId, ct);

        var block = Blocks(ctx.Content).Select(b => b.Block)
            .FirstOrDefault(b => Str(b, "id") == blockId && Str(b, "tur") == "yozish")
            ?? throw new NotFoundException("InteractiveBlock", blockId);

        var step = Objects(block, "pogonalar").FirstOrDefault(p => Number(p, "n") == n)
            ?? throw new NotFoundException("InteractiveStep", n);

        return new RevealResult(Str(step, "namuna"));
    }

    // ================================================================ mini-test

    public async Task<JsonObject> StartTestAsync(long unitId, long actorId, CancellationToken ct = default)
    {
        var ctx = await EnsureReaderAsync(unitId, actorId, ct);
        var test = Obj(ctx.Content, "test") ?? throw new NotFoundException("InteractiveTest", unitId);

        return new JsonObject
        {
            ["nom"] = Str(test, "nom"),
            ["savollar"] = MiniTest.Questions(ctx.Content, ctx.VersionId, audio),
        };
    }

    public async Task<TestSubmitResult> SubmitTestAsync(
        long unitId, TestSubmitRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ctx = await EnsureReaderAsync(unitId, actorId, ct);
        if (Obj(ctx.Content, "test") is null) throw new NotFoundException("InteractiveTest", unitId);

        var grade = MiniTest.Grade(
            ctx.Content,
            [.. (request.Answers ?? []).Select(a => new MiniTestAnswer(a.Index, a.Choice, a.Text))]);

        var questions = grade.Questions
            .Select(q => new TestQuestionResultDto(q.Index, q.Correct, q.CorrectChoice, q.Javob, q.Oral))
            .ToList();

        if (!ctx.IsStudent) return new TestSubmitResult(grade.Score, grade.Max, grade.OralCount, false, questions);

        var now = clock.GetUtcNow();
        var hasFirst = await db.InteractiveTestAttempts.AsNoTracking()
            .AnyAsync(a => a.StudentId == ctx.ActorId && a.UnitId == ctx.UnitId && a.IsFirst, ct);

        var attempt = new InteractiveTestAttempt
        {
            StudentId = ctx.ActorId,
            UnitId = ctx.UnitId,
            VersionId = ctx.VersionId,
            StartedAt = now,
            SubmittedAt = now,
            Score = grade.Score,
            MaxScore = grade.Max,
            Answers = JsonSerializer.Serialize(new { answers = request.Answers, results = questions }, JsonOptions),
            IsFirst = !hasFirst,
            CreatedAt = now,
        };
        db.InteractiveTestAttempts.Add(attempt);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (attempt.IsFirst)
        {
            // Parallel topshiriq birinchisini allaqachon yozgan — bu urinish mashq.
            DetachInteractive();
            attempt.Id = 0;
            attempt.IsFirst = false;
            db.InteractiveTestAttempts.Add(attempt);
            await db.SaveChangesAsync(ct);
        }

        return new TestSubmitResult(grade.Score, grade.Max, grade.OralCount, attempt.IsFirst, questions);
    }

    // ================================================================ admin

    public async Task<IReadOnlyList<AdminBookDto>> ListBooksAsync(long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);

        var books = await db.InteractiveBooks.AsNoTracking().OrderBy(b => b.Position).ToListAsync(ct);
        var result = new List<AdminBookDto>(books.Count);
        foreach (var book in books) result.Add(await AdminBookAsync(book, ct));
        return result;
    }

    public async Task<AdminUnitDto> SetUnitLessonAsync(
        long unitId, long? moduleLessonId, long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);

        var unit = await db.InteractiveUnits.FirstOrDefaultAsync(u => u.Id == unitId, ct)
            ?? throw new NotFoundException(NotFoundEntity, unitId);

        if (moduleLessonId is { } lessonId)
        {
            if (!await db.ModuleLessons.AsNoTracking().AnyAsync(l => l.Id == lessonId, ct))
                throw new NotFoundException(nameof(ModuleLesson), lessonId);

            if (await db.InteractiveUnits.AsNoTracking().AnyAsync(u => u.ModuleLessonId == lessonId && u.Id != unitId, ct))
                throw new ConflictException("Bu darsga boshqa birlik allaqachon bog'langan.");
        }

        unit.ModuleLessonId = moduleLessonId;
        unit.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return (await AdminUnitsAsync([unit.Id], ct))[0];
    }

    public async Task<AdminBookDto> PublishAsync(long bookId, bool published, long actorId, CancellationToken ct = default)
    {
        await EnsureManagerAsync(actorId, ct);

        var book = await db.InteractiveBooks.FirstOrDefaultAsync(b => b.Id == bookId, ct)
            ?? throw new NotFoundException(nameof(InteractiveBook), bookId);

        book.IsPublished = published;
        book.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return await AdminBookAsync(book, ct);
    }

    private async Task<AdminBookDto> AdminBookAsync(InteractiveBook book, CancellationToken ct)
    {
        var unitIds = await db.InteractiveUnits.AsNoTracking()
            .Where(u => u.BookId == book.Id)
            .OrderBy(u => u.Position)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var runs = await db.InteractiveImportRuns.AsNoTracking()
            .Where(r => r.BookCode == book.Code)
            .OrderByDescending(r => r.StartedAt)
            .Take(20)
            .Select(r => new AdminImportRunDto(r.Id, r.StartedAt, r.FinishedAt, r.DryRun, r.Result))
            .ToListAsync(ct);

        return new AdminBookDto(
            book.Id, book.Code, book.Title, book.Subtitle, book.CourseId, book.IsPublished,
            await AdminUnitsAsync(unitIds, ct), runs);
    }

    private async Task<List<AdminUnitDto>> AdminUnitsAsync(List<long> unitIds, CancellationToken ct)
    {
        var rows = await db.InteractiveUnits.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .Select(u => new AdminUnitDto(
                u.Id,
                u.Code,
                u.Number,
                u.TitleUz,
                u.ModuleLessonId,
                u.ModuleLesson != null ? u.ModuleLesson.Name : null,
                u.CurrentVersion != null ? u.CurrentVersion.Version : null,
                u.CurrentVersion != null ? u.CurrentVersion.ContentSha256 : null))
            .ToListAsync(ct);

        return [.. rows.OrderBy(r => unitIds.IndexOf(r.Id))];
    }

    // ================================================================ kirish

    private sealed record ReaderContext(
        long ActorId,
        bool IsStudent,
        long UnitId,
        long VersionId,
        int Version,
        string Book,
        string Code,
        long? LessonId,
        string AudioManifest,
        JsonObject Content);

    /// <summary>
    /// 🔴 YAGONA DARVOZA: o'quvchi uchun huquq + bog'lanish + nashr + kurs + gating.
    /// Xodim (ustoz, kurator, o'quv bo'limi, admin) — oldindan ko'rish uchun o'tadi.
    /// </summary>
    private async Task<ReaderContext> EnsureReaderAsync(
        long unitId, long actorId, CancellationToken ct, bool parseContent = true)
    {
        var actor = await LoadActorAsync(actorId, ct);

        var row = await db.InteractiveUnits.AsNoTracking()
            .Where(u => u.Id == unitId && u.CurrentVersionId != null)
            .Select(u => new
            {
                u.Id,
                Book = u.Book!.Code,
                u.Book.IsPublished,
                u.Code,
                u.ModuleLessonId,
                CourseId = u.ModuleLesson != null ? (long?)u.ModuleLesson.Module!.CourseId : null,
                VersionId = u.CurrentVersionId!.Value,
                u.CurrentVersion!.Version,
                u.CurrentVersion.AudioManifest,
                Raw = parseContent ? u.CurrentVersion.ContentRaw : null,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(NotFoundEntity, unitId);

        var isStudent = actor.Role == UserRole.Student;

        if (isStudent)
        {
            var decision = await access.GetAsync(actor.Id, ct);

            if (!decision.Granted
                || !row.IsPublished
                || row.ModuleLessonId is null
                || row.CourseId != decision.CourseId)
            {
                throw new NotFoundException(NotFoundEntity, unitId);
            }

            await gating.EnsureLessonUnlockedAsync(actor.Id, row.ModuleLessonId.Value, ct);
        }

        return new ReaderContext(
            actor.Id, isStudent, row.Id, row.VersionId, row.Version, row.Book, row.Code, row.ModuleLessonId,
            row.AudioManifest,
            row.Raw is null ? [] : UnitJson.Parse(row.Raw));
    }

    private async Task<User> LoadActorAsync(long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive) throw new ForbiddenException("Profilingiz faol emas.");

        return actor;
    }

    private async Task EnsureManagerAsync(long actorId, CancellationToken ct)
    {
        var actor = await LoadActorAsync(actorId, ct);
        if (actor.Role is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Darslikni faqat o'quv bo'limi yoki administrator boshqaradi.");
    }

    // ================================================================ progress

    private async Task<InteractiveUnitProgress> ProgressRowAsync(ReaderContext ctx, DateTimeOffset now, CancellationToken ct)
    {
        var progress = db.InteractiveUnitProgress.Local
            .FirstOrDefault(p => p.StudentId == ctx.ActorId && p.UnitId == ctx.UnitId)
            ?? await db.InteractiveUnitProgress
                .FirstOrDefaultAsync(p => p.StudentId == ctx.ActorId && p.UnitId == ctx.UnitId, ct);

        if (progress is null)
        {
            progress = new InteractiveUnitProgress
            {
                StudentId = ctx.ActorId,
                UnitId = ctx.UnitId,
                LastVersionId = ctx.VersionId,
                CreatedAt = now,
            };
            db.InteractiveUnitProgress.Add(progress);
        }

        return progress;
    }

    private async Task<UnitProgressDto> ProgressAsync(long unitId, long studentId, CancellationToken ct)
    {
        var progress = await db.InteractiveUnitProgress.AsNoTracking()
            .FirstOrDefaultAsync(p => p.StudentId == studentId && p.UnitId == unitId, ct);

        var items = await db.InteractiveItemResults.AsNoTracking()
            .Where(r => r.StudentId == studentId && r.UnitId == unitId && r.FirstTotal != null)
            .Select(r => new { r.ItemKey, r.FirstRight, r.FirstTotal })
            .ToListAsync(ct);

        var test = await db.InteractiveTestAttempts.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.UnitId == unitId && a.IsFirst && a.SubmittedAt != null)
            .Select(a => new { a.Score, a.MaxScore, a.SubmittedAt, a.Answers })
            .FirstOrDefaultAsync(ct);

        return new UnitProgressDto(
            progress is null ? [] : ReadIds(progress.BlocksDone),
            items.ToDictionary(i => i.ItemKey, i => new FirstScoreDto(i.FirstRight ?? 0, i.FirstTotal ?? 0), StringComparer.Ordinal),
            progress?.CompletedAt,
            progress?.ExerciseRight ?? 0,
            progress?.ExerciseTotal ?? 0,
            test is null
                ? null
                : new TestResultSummaryDto(
                    (int)(test.Score ?? 0), (int)(test.MaxScore ?? 0), OralCount(test.Answers), test.SubmittedAt!.Value));
    }

    private async Task<UnitProgressSummaryDto> SummaryAsync(long unitId, long? studentId, CancellationToken ct)
    {
        var stats = await db.InteractiveUnits.AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => u.CurrentVersion!.Stats)
            .FirstOrDefaultAsync(ct);

        var blocksTotal = 0;
        if (stats is not null
            && JsonSerializer.Deserialize<Dictionary<string, int>>(stats) is { } map
            && map.TryGetValue("bloklar", out var total))
        {
            blocksTotal = total;
        }

        if (studentId is null) return new UnitProgressSummaryDto(0, blocksTotal, false, 0, 0, null, null);

        var p = await ProgressAsync(unitId, studentId.Value, ct);
        return new UnitProgressSummaryDto(
            p.BlocksDone.Count, blocksTotal, p.CompletedAt is not null, p.ExerciseRight, p.ExerciseTotal,
            p.Test?.Score, p.Test?.Max);
    }

    private static UnitProgressDto EmptyProgress() =>
        new([], new Dictionary<string, FirstScoreDto>(StringComparer.Ordinal), null, 0, 0, null);

    private static List<string> ReadIds(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int OralCount(string answers)
    {
        try
        {
            var node = JsonNode.Parse(answers);
            return Arr(node, "results").OfType<JsonObject>().Count(r => Flag(r, "oral") == true);
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    /// <summary>
    /// To'qnashuvdan keyin qayta urinish: shu so'rovda kuzatilayotgan darslik
    /// qatorlari tashlanadi va bazadan qayta o'qiladi.
    /// </summary>
    private void DetachInteractive()
    {
        foreach (var e in db.InteractiveItemResults.Local.ToList())
            db.InteractiveItemResults.Entry(e).State = EntityState.Detached;
        foreach (var e in db.InteractiveUnitProgress.Local.ToList())
            db.InteractiveUnitProgress.Entry(e).State = EntityState.Detached;
        foreach (var e in db.InteractiveTestAttempts.Local.ToList())
            db.InteractiveTestAttempts.Entry(e).State = EntityState.Detached;
    }
}
