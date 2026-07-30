using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Gating.Services;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.Tests.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Tests.Services;

/// <summary>
/// Testlar use-case'i. Biznes qoidalari Domain'da va TAKROLANMAYDI:
///   • <c>Test.Validate()/Publish()</c>            — tuzilma tekshiruvi;
///   • <c>Test.EnsureOpenForSubmission()</c>       — MUDDAT (serverda!);
///   • <c>TestAttempt.Deadline(test)</c>           — vaqt chegarasi VA muddat
///                                                   (ikkisidan ERTAROG'I);
///   • <c>TestQuestion.Score()</c>                 — baholash ("hammasi yoki hech nima");
///   • <c>TestAttempt.SubmitAnswers()/CloseByTimeout()</c> — urinishni yopish.
///
/// Eski tizimning shu moduldagi TO'RT bugi shu yerda tuzatilgan:
///  1) `due_at` HECH QAYERDA tekshirilmasdi -> endi `EnsureOpenForSubmission`
///     ham `start`, ham `take`, ham `submit` da chaqiriladi;
///  2) vaqt chegarasi faqat klientda edi -> endi `TestAttempt.Deadline`
///     serverda tekshiriladi va o'tgan urinish 0 ball bilan yopiladi;
///  3) ko'p to'g'ri javob buzuq edi -> `TestAnswer` da (attempt, savol, variant)
///     unikal, ball esa Domain'da to'plam sifatida solishtiriladi;
///  4) natijalarda guruhga `outerjoin` qilinardi va ikki guruhdagi o'quvchi
///     IKKI QATOR bo'lib chiqardi -> endi guruh nomlari ICHKI so'rov bilan
///     yig'iladi, ya'ni bitta urinish = bitta qator.
/// </summary>
public sealed class TestService(
    IApplicationDbContext db,
    IGatingService gating,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : ITestService
{
    // ================================================================= tuzish: o'qish

    public async Task<PagedResult<TestDto>> ListAsync(
        TestListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await LoadAuthorAsync(actorId, ct);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = db.Tests.AsNoTracking();

        if (query.Kind is { } kind) rows = rows.Where(t => t.Kind == kind);
        if (query.IsPublished is { } published) rows = rows.Where(t => t.IsPublished == published);
        if (query.ModuleLessonId is { } lessonId) rows = rows.Where(t => t.ModuleLessonId == lessonId);

        var total = await rows.CountAsync(ct);

        var items = await Project(rows
                .OrderByDescending(t => t.CreatedAt)
                .ThenBy(t => t.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(ct);

        return new PagedResult<TestDto>(items, page, pageSize, total);
    }

    public async Task<TestAuthoringDto> GetForAuthoringAsync(
        long id, long actorId, CancellationToken ct = default)
    {
        await LoadAuthorAsync(actorId, ct);

        var test = await Project(db.Tests.AsNoTracking().Where(t => t.Id == id)).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Test), id);

        var questions = await OrderedQuestions(id)
            .Select(q => new AuthoringQuestionDto(
                q.Id,
                q.Body,
                q.ImageKey,
                q.Position,
                q.Points,
                q.Options.Count(o => o.IsCorrect) > 1,
                q.Options
                    .OrderBy(o => o.Position)
                    .ThenBy(o => o.Id)
                    .Select(o => new AuthoringOptionDto(o.Id, o.Body, o.Position, o.IsCorrect))
                    .ToList()))
            .ToListAsync(ct);

        return new TestAuthoringDto(test, questions);
    }

    // ================================================================= tuzish: yozish

    public async Task<TestDto> CreateAsync(
        CreateTestRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var author = await LoadAuthorAsync(actorId, ct);

        if (request.ModuleLessonId is { } lessonId
            && !await db.ModuleLessons.AsNoTracking().AnyAsync(l => l.Id == lessonId, ct))
        {
            throw new NotFoundException(nameof(ModuleLesson), lessonId);
        }

        var test = new Test
        {
            Title = request.Title?.Trim() ?? string.Empty,
            Description = Normalize(request.Description),
            Kind = request.Kind,
            ModuleLessonId = request.ModuleLessonId,
            TimeLimitMinutes = request.TimeLimitMinutes,
            DueAt = request.DueAt,
            CreatedById = author.Id,
        };

        // Domain: sarlavha, vaqt chegarasi va "dars testi <-> dars" muvofiqligi.
        test.Validate();

        db.Tests.Add(test);
        await db.SaveChangesAsync(ct);

        return await Project(db.Tests.AsNoTracking().Where(t => t.Id == test.Id)).FirstAsync(ct);
    }

    public async Task<TestDto> UpdateAsync(
        long id, UpdateTestRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await LoadAuthorAsync(actorId, ct);

        var test = await db.Tests.AsTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(Test), id);

        // TUR va DARS o'zgartirilmaydi: musobaqa testini dars testiga aylantirish
        // gating'ga ta'sir qiladi va allaqachon topshirgan o'quvchilarning
        // natijasini boshqa ma'noga o'tkazardi.
        test.Title = request.Title?.Trim() ?? string.Empty;
        test.Description = Normalize(request.Description);
        test.TimeLimitMinutes = request.TimeLimitMinutes;
        test.DueAt = request.DueAt;

        test.Validate();

        await db.SaveChangesAsync(ct);

        return await Project(db.Tests.AsNoTracking().Where(t => t.Id == id)).FirstAsync(ct);
    }

    public async Task DeleteAsync(long id, long actorId, CancellationToken ct = default)
    {
        await LoadAuthorAsync(actorId, ct);

        var test = await db.Tests.AsTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(Test), id);

        await EnsureNoAttemptsAsync(id, "o'chirib", ct);

        db.Tests.Remove(test);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AuthoringQuestionDto> AddQuestionAsync(
        long testId, SaveQuestionRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await LoadAuthorAsync(actorId, ct);
        await EnsureTestExistsAsync(testId, ct);
        await EnsureNoAttemptsAsync(testId, "o'zgartirib", ct);

        var position = request.Position
            ?? (await db.TestQuestions.AsNoTracking()
                    .Where(q => q.TestId == testId)
                    .Select(q => (int?)q.Position)
                    .MaxAsync(ct) ?? -1) + 1;

        var question = new TestQuestion
        {
            TestId = testId,
            Body = request.Body?.Trim() ?? string.Empty,
            ImageKey = Normalize(request.ImageKey),
            Points = request.Points,
            Position = position,
        };

        FillOptions(question, request.Options);

        // Domain: kamida 2 variant, kamida 1 to'g'ri, ball > 0, matn bo'sh emas.
        question.Validate();

        db.TestQuestions.Add(question);
        await db.SaveChangesAsync(ct);

        return await LoadQuestionDtoAsync(question.Id, ct);
    }

    public async Task<AuthoringQuestionDto> UpdateQuestionAsync(
        long testId, long questionId, SaveQuestionRequest request, long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await LoadAuthorAsync(actorId, ct);
        await EnsureNoAttemptsAsync(testId, "o'zgartirib", ct);

        var question = await db.TestQuestions
            .AsTracking()
            .Include(q => q.Options)
            .FirstOrDefaultAsync(q => q.Id == questionId && q.TestId == testId, ct)
            ?? throw new NotFoundException(nameof(TestQuestion), questionId);

        question.Body = request.Body?.Trim() ?? string.Empty;
        question.ImageKey = Normalize(request.ImageKey);
        question.Points = request.Points;

        if (request.Position is { } position)
            question.Position = position;

        // VARIANTLAR BUTUNLAY ALMASHTIRILADI.
        //
        // NIMA UCHUN "o'rnida tahrirlash" emas: variantni ID bo'yicha
        // moslashtirish klient yuborgan ID'larga ishonishni talab qiladi va
        // begona savolning variantini "ko'chirib" olish yo'lini ochadi.
        // To'liq almashtirish esa bir ma'noli. Urinishlar bo'lsa bu metod
        // umuman ishlamaydi (yuqoridagi tekshiruv), ya'ni javob qatorlari
        // yetim qolmaydi.
        foreach (var option in question.Options.ToList())
            db.TestOptions.Remove(option);

        question.Options.Clear();
        FillOptions(question, request.Options);

        question.Validate();

        await db.SaveChangesAsync(ct);

        return await LoadQuestionDtoAsync(questionId, ct);
    }

    public async Task DeleteQuestionAsync(
        long testId, long questionId, long actorId, CancellationToken ct = default)
    {
        await LoadAuthorAsync(actorId, ct);
        await EnsureNoAttemptsAsync(testId, "o'zgartirib", ct);

        var question = await db.TestQuestions
            .AsTracking()
            .FirstOrDefaultAsync(q => q.Id == questionId && q.TestId == testId, ct)
            ?? throw new NotFoundException(nameof(TestQuestion), questionId);

        db.TestQuestions.Remove(question);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TestDto> SetPublishedAsync(
        long id, bool published, long actorId, CancellationToken ct = default)
    {
        await LoadAuthorAsync(actorId, ct);

        // Savollar VARIANTLARI bilan yuklanadi: `Publish()` har bir savolni
        // tekshiradi (kamida 2 variant, kamida 1 to'g'ri).
        var test = await db.Tests
            .AsTracking()
            .Include(t => t.Questions)
            .ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(Test), id);

        if (published)
            test.Publish();
        else
            test.Unpublish();

        await db.SaveChangesAsync(ct);

        return await Project(db.Tests.AsNoTracking().Where(t => t.Id == id)).FirstAsync(ct);
    }

    // ================================================================= natijalar

    public async Task<IReadOnlyList<TestResultRowDto>> ListResultsAsync(
        long id, long actorId, CancellationToken ct = default)
    {
        await LoadAuthorAsync(actorId, ct);
        await EnsureTestExistsAsync(id, ct);

        return await LoadResultsAsync(id, ct);
    }

    public async Task<CsvExport> ExportResultsCsvAsync(
        long id, long actorId, CancellationToken ct = default)
    {
        await LoadAuthorAsync(actorId, ct);

        var test = await db.Tests.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new { t.Id, t.Title })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Test), id);

        var rows = await LoadResultsAsync(id, ct);
        var zone = timeZone.TimeZone;

        var csv = new StringBuilder(rows.Count * 64 + 128);

        // BOM: Excel BOM'siz UTF-8 ni ANSI deb o'qiydi va o'zbek harflari
        // (ʻ, ʼ) buziladi. Kodda KO'RINMAS belgi qoldirmaslik uchun oshkor
        // `\uFEFF` yoziladi.
        csv.Append('\uFEFF');
        csv.AppendLine("F.I.Sh.,Guruh,Ball,Maksimal ball,Foiz,Topshirilgan,Vaqti tugagan");

        foreach (var row in rows)
        {
            // Mahalliy vaqt (Asia/Tashkent): hisobotni o'qiydigan odam
            // devor-soatiga qaraydi, UTC'ga emas.
            var submitted = row.SubmittedAt is { } at
                ? TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-dd HH:mm", Invariant)
                : string.Empty;

            csv.Append(Csv(row.StudentName)).Append(',')
               .Append(Csv(row.GroupNames)).Append(',')
               .Append(Number(row.Score)).Append(',')
               .Append(Number(row.MaxScore)).Append(',')
               .Append(Number(row.Percent)).Append(',')
               .Append(Csv(submitted)).Append(',')
               .Append(row.ClosedByTimeout ? "ha" : "yo'q")
               .Append('\n');
        }

        var fileName = string.Create(Invariant, $"test-{test.Id}-natijalar.csv");

        return new CsvExport(fileName, "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(csv.ToString()));
    }

    /// <summary>
    /// ★ NATIJALAR SO'ROVI — BITTA URINISH = BITTA QATOR.
    ///
    /// Guruh nomlari ICHKI (correlated) so'rov bilan olinadi. Eski tizim
    /// `outerjoin(GroupMember).outerjoin(Group)` qilardi va ikki guruhdagi
    /// o'quvchi natijalar jadvalida IKKI MARTA ko'rinardi: reyting, o'rtacha
    /// ball va CSV eksport — hammasi buzilardi.
    /// </summary>
    private async Task<List<TestResultRowDto>> LoadResultsAsync(long testId, CancellationToken ct)
    {
        var rows = await db.TestAttempts
            .AsNoTracking()
            .Where(a => a.TestId == testId && a.Status == AttemptStatus.Submitted)
            .OrderByDescending(a => a.Score)
            .ThenBy(a => a.SubmittedAt)
            .ThenBy(a => a.Id)
            .Select(a => new ResultRow(
                a.Id,
                a.StudentId,
                a.Student!.FullName,
                db.GroupMembers
                    .Where(m => m.StudentId == a.StudentId && m.Status == MemberStatus.Active)
                    .OrderBy(m => m.Group!.Name)
                    .Select(m => m.Group!.Name)
                    .ToList(),
                a.Score,
                a.MaxScore,
                a.SubmittedAt,
                a.ClosedByTimeout))
            .ToListAsync(ct);

        return rows.ConvertAll(row => new TestResultRowDto(
            row.AttemptId,
            row.StudentId,
            row.StudentName,
            row.GroupNames.Count == 0
                ? NoGroupLabel
                : string.Join(", ", row.GroupNames.Distinct(StringComparer.Ordinal)),
            row.Score,
            row.MaxScore,

            // Foizni DOMAIN hisoblaydi — formula bu yerda takrorlanmaydi.
            new TestAttempt { Score = row.Score, MaxScore = row.MaxScore }.Percent,
            row.SubmittedAt,
            row.ClosedByTimeout));
    }

    // ================================================================= yechish

    public async Task<IReadOnlyList<AvailableTestDto>> ListAvailableAsync(
        long studentId, CancellationToken ct = default)
    {
        await LoadStudentAsync(studentId, ct);

        // Gating BIR MARTA (keshdan) — har test uchun alohida emas.
        var gate = await gating.GetCourseGateAsync(studentId, ct);

        var unlockedLessons = gate.Lessons
            .Where(l => l.Unlocked)
            .Select(l => l.LessonId)
            .ToList();

        var rows = await db.Tests
            .AsNoTracking()
            .Where(t => t.IsPublished
                     && (t.Kind == TestKind.Competition
                      || (t.ModuleLessonId != null && unlockedLessons.Contains(t.ModuleLessonId.Value))))
            .OrderBy(t => t.DueAt == null)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.Id)
            .Select(t => new AvailableRow(
                t.Id,
                t.Title,
                t.Description,
                t.Kind,
                t.ModuleLessonId,
                t.ModuleLesson == null ? null : t.ModuleLesson.Name,
                t.TimeLimitMinutes,
                t.DueAt,
                t.Questions.Count,
                t.Questions.Sum(q => (decimal?)q.Points) ?? 0m,
                db.TestAttempts
                    .Where(a => a.TestId == t.Id && a.StudentId == studentId)
                    .Select(a => new AttemptBrief(a.Status, a.Score))
                    .FirstOrDefault()))
            .ToListAsync(ct);

        var now = clock.GetUtcNow();

        return rows.ConvertAll(row => new AvailableTestDto(
            row.Id, row.Title, row.Description, row.Kind, row.ModuleLessonId, row.ModuleLessonName,
            row.TimeLimitMinutes, row.DueAt, row.QuestionCount, row.MaxScore,
            row.MyAttempt?.Status,
            row.MyAttempt?.Score,

            // Boshlash mumkinmi: muddat o'tmagan, savol bor va hali
            // topshirmagan. Bu MASLAHAT — haqiqiy tekshiruv `StartAsync` da.
            CanStart: row.QuestionCount > 0
                   && row.MyAttempt?.Status != AttemptStatus.Submitted
                   && (row.DueAt is not { } due || now <= due + Test.SubmitGracePeriod)));
    }

    public async Task<StartAttemptDto> StartAsync(
        long testId, long studentId, CancellationToken ct = default)
    {
        await LoadStudentAsync(studentId, ct);

        var test = await LoadTestForTakingAsync(testId, studentId, ct);

        var existing = await db.TestAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TestId == testId && a.StudentId == studentId, ct);

        if (existing is not null)
        {
            if (existing.IsSubmitted)
                throw new ConflictException("Bu testni allaqachon topshirgansiz.");

            // IDEMPOTENT: sahifa yangilangan yoki ikkinchi tab ochilgan.
            // AYNI urinish qaytadi — `StartedAt` O'ZGARMAYDI, aks holda
            // o'quvchi sahifani yangilab taymerni cheksiz uzaytira olardi.
            return Describe(existing, test);
        }

        var attempt = new TestAttempt
        {
            TestId = testId,
            StudentId = studentId,
            Status = AttemptStatus.InProgress,
            StartedAt = clock.GetUtcNow(),
        };

        db.TestAttempts.Add(attempt);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // POYGA: ikki so'rov bir vaqtda boshladi.
            // `UX_TestAttempts_TestId_StudentId` ikkinchisini rad etdi —
            // bu XATO EMAS: g'olibning urinishini qaytaramiz.
            // (Muhimi: ikkita urinish YARATILMAYDI.)
            db.TestAttempts.Entry(attempt).State = EntityState.Detached;

            var winner = await db.TestAttempts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.TestId == testId && a.StudentId == studentId, ct)
                ?? throw new ConflictException("Testni boshlash bajarilmadi. Qaytadan urinib ko'ring.");

            return Describe(winner, test);
        }

        return Describe(attempt, test);
    }

    public async Task<TakeTestDto> GetForTakingAsync(
        long testId, long studentId, CancellationToken ct = default)
    {
        await LoadStudentAsync(studentId, ct);

        var test = await LoadTestForTakingAsync(testId, studentId, ct);
        var attempt = await LoadOpenAttemptAsync(test, studentId, ct);

        // ★ TO'G'RI JAVOBLAR SO'ROVGA UMUMAN QO'SHILMAYDI.
        //
        // `IsCorrect` bu yerda SELECT ro'yxatida yo'q — ya'ni u bazadan ham
        // o'qilmaydi. Shu tufayli uni "yashirishni unutish" imkonsiz:
        // `TakeOptionDto` turida bunday maydon yo'q va SQL ham uni tortmaydi.
        //
        // `MultipleAnswers` — faqat SANOQ (nechta to'g'ri variant bor),
        // QAYSI variant to'g'ri ekanini oshkor qilmaydi.
        var questions = await OrderedQuestions(testId)
            .Select(q => new TakeQuestionDto(
                q.Id,
                q.Body,
                q.ImageKey,
                q.Position,
                q.Points,
                q.Options.Count(o => o.IsCorrect) > 1,
                q.Options
                    .OrderBy(o => o.Position)
                    .ThenBy(o => o.Id)
                    .Select(o => new TakeOptionDto(o.Id, o.Body, o.Position))
                    .ToList()))
            .ToListAsync(ct);

        return new TakeTestDto(
            test.Id,
            test.Title,
            test.Description,
            test.TimeLimitMinutes,
            test.DueAt,
            attempt.Id,
            attempt.StartedAt,
            attempt.Deadline(test),
            questions.Sum(q => q.Points),
            questions);
    }

    public async Task<MyResultDto> SubmitAsync(
        long testId, SubmitTestRequest request, long studentId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await LoadStudentAsync(studentId, ct);

        var test = await LoadTestForTakingAsync(testId, studentId, ct);

        // KUZATILADIGAN urinish: `SubmitAnswers` javob qatorlarini
        // `attempt.Answers` ga qo'shadi va EF ularni o'zi yozadi.
        var attempt = await db.TestAttempts
            .AsTracking()
            .FirstOrDefaultAsync(a => a.TestId == testId && a.StudentId == studentId, ct)
            ?? throw new ConflictException("Avval testni boshlang.");

        var now = clock.GetUtcNow();

        if (attempt.IsSubmitted)
            throw new ConflictException("Bu testni allaqachon topshirgansiz.");

        await EnsureWithinTimeLimitAsync(attempt, test, now, ct);

        // Savollar VARIANTLARI bilan: baholash SERVERDA, klient yuborgan
        // ballga hech qachon ishonilmaydi.
        var questions = await db.TestQuestions
            .AsNoTracking()
            .Include(q => q.Options)
            .Where(q => q.TestId == testId)
            .OrderBy(q => q.Position)
            .ThenBy(q => q.Id)
            .ToListAsync(ct);

        if (questions.Count == 0)
            throw new ConflictException("Testda savol yo'q.");

        // Klient bir savolni ikki marta yuborsa — oxirgisi emas, BIRLASHMASI
        // olinadi. Aks holda "oxirgisi yutadi" qoidasi eski tizimning
        // ko'p-to'g'ri-javob bugini takrorlardi.
        var selections = new Dictionary<long, IReadOnlyCollection<long>>();

        foreach (var answer in request.Answers ?? [])
        {
            var optionIds = answer.OptionIds ?? [];

            selections[answer.QuestionId] = selections.TryGetValue(answer.QuestionId, out var already)
                ? [.. already.Concat(optionIds).Distinct()]
                : [.. optionIds.Distinct()];
        }

        // Domain: begona variantni filtrlaydi, ballni hisoblaydi, urinishni
        // yopadi va har TANLOV uchun alohida javob qatori yozadi.
        attempt.SubmitAnswers(questions, selections, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // ★ `xmin` qulfi: boshqa so'rov shu urinishni allaqachon
            //   topshirgan. 409 — 500 EMAS.
            throw DoubleSubmit();
        }
        catch (DbUpdateException)
        {
            // ★ `UX_TestAnswers_AttemptId_QuestionId_OptionId`: ikkinchi
            //   so'rov aynan shu tanlovlarni yozishga urindi.
            throw DoubleSubmit();
        }

        // Test topshirildi -> gating keshi YAROQSIZ (keyingi dars ochilishi mumkin).
        await gating.InvalidateAsync(studentId, ct);

        return Map(test, attempt);
    }

    public async Task<MyResultDto> GetMyResultAsync(
        long testId, long studentId, CancellationToken ct = default)
    {
        await LoadStudentAsync(studentId, ct);

        var test = await db.Tests.AsNoTracking().FirstOrDefaultAsync(t => t.Id == testId, ct)
            ?? throw new NotFoundException(nameof(Test), testId);

        var attempt = await db.TestAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TestId == testId && a.StudentId == studentId, ct)
            ?? throw new NotFoundException(nameof(TestAttempt), testId);

        return Map(test, attempt);
    }

    // ================================================================= ichki: yechish qoidalari

    /// <summary>
    /// Yechish uchun testni yuklaydi va TO'RT shartni tekshiradi:
    /// mavjudmi, e'lon qilinganmi, MUDDAT o'tmaganmi, darsi OCHIQMI.
    ///
    /// `start`, `take` va `submit` — UCHALASI ham shu metoddan o'tadi.
    /// Eski tizimda muddat tekshiruvi HECH QAYERDA yo'q edi; bitta joyda
    /// bo'lgani uchun endi uni unutish mumkin emas.
    /// </summary>
    private async Task<Test> LoadTestForTakingAsync(long testId, long studentId, CancellationToken ct)
    {
        var test = await db.Tests.AsNoTracking().FirstOrDefaultAsync(t => t.Id == testId, ct)
            ?? throw new NotFoundException(nameof(Test), testId);

        // Domain: e'lon qilinmagan yoki muddati o'tgan test -> DomainException -> 409.
        test.EnsureOpenForSubmission(clock.GetUtcNow());

        // DARS TESTI sur'at nazoratiga kiradi (musobaqa testi — kirmaydi).
        if (test.Kind == TestKind.Lesson && test.ModuleLessonId is { } lessonId)
            await gating.EnsureLessonUnlockedAsync(studentId, lessonId, ct);

        return test;
    }

    private async Task<TestAttempt> LoadOpenAttemptAsync(
        Test test, long studentId, CancellationToken ct)
    {
        var attempt = await db.TestAttempts
            .AsTracking()
            .FirstOrDefaultAsync(a => a.TestId == test.Id && a.StudentId == studentId, ct)
            ?? throw new ConflictException("Avval testni boshlang.");

        if (attempt.IsSubmitted)
            throw new ConflictException("Bu testni allaqachon topshirgansiz.");

        await EnsureWithinTimeLimitAsync(attempt, test, clock.GetUtcNow(), ct);

        return attempt;
    }

    /// <summary>
    /// ★ VAQT CHEGARASI SERVERDA.
    ///
    /// Eski tizimda taymer FAQAT brauzerda edi: sahifani yangilash, tabni
    /// yopib qayta ochish yoki DevTools bilan taymerni to'xtatish testni
    /// cheksiz cho'zardi.
    ///
    /// Muddat o'tgan bo'lsa urinish 0 ball bilan YOPILADI (Domain
    /// `CloseByTimeout`) — javobsiz "muzlab qolgan" urinish qolmasin, aks
    /// holda o'quvchi keyin qaytib kelib topshira olardi.
    ///
    /// ★ QOIDA BU YERDA TAKRORLANMAYDI. "Qachon kech bo'ldi" degan savolga
    /// YAGONA javob — <c>TestAttempt.Deadline(test)</c>. U vaqt chegarasi
    /// bilan `DueAt` dan ERTAROG'INI oladi, ya'ni klientga ko'rsatilgan
    /// taymer bilan bu tekshiruv AYNAN bir xil ondan foydalanadi. Shart shu
    /// yerda qo'lda yozilsa (masalan faqat `TimeLimitMinutes` bo'yicha)
    /// ikkalasi ajralib ketardi va o'quvchi taymerda vaqt bor deb turib
    /// 409 olardi.
    /// </summary>
    private async Task EnsureWithinTimeLimitAsync(
        TestAttempt attempt, Test test, DateTimeOffset now, CancellationToken ct)
    {
        if (attempt.Deadline(test) is not { } deadline || now <= deadline)
            return;

        var maxScore = await db.TestQuestions
            .AsNoTracking()
            .Where(q => q.TestId == test.Id)
            .Select(q => (decimal?)q.Points)
            .SumAsync(ct) ?? 0m;

        // Kuzatilmayotgan (AsNoTracking) urinish bo'lsa ham yopish kerak —
        // shuning uchun kuzatiladigan nusxasini olamiz.
        var tracked = await db.TestAttempts
            .AsTracking()
            .FirstAsync(a => a.Id == attempt.Id, ct);

        tracked.CloseByTimeout(maxScore, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Boshqa so'rov (yoki fon vazifasi) allaqachon yopdi — bu holat
            // idempotent, xato emas.
        }

        await gating.InvalidateAsync(attempt.StudentId, ct);

        throw new ConflictException(
            "Test uchun ajratilgan vaqt tugagan — urinish yopildi.");
    }

    // ================================================================= ichki: ruxsat

    /// <summary>
    /// Test TUZISH ruxsati.
    ///
    /// Faqat o'quv bo'limi va admin: test butun platformaga (yoki kurs
    /// darsiga) taalluqli, shuning uchun ustoz o'z guruhiga test tuza
    /// olmaydi — bu ATAYLAB (ROADMAP 3.4 doirasi).
    /// </summary>
    private async Task<User> LoadAuthorAsync(long actorId, CancellationToken ct)
    {
        var actor = await LoadUserAsync(actorId, ct);

        if (actor.Role is not (UserRole.Admin or UserRole.Academic))
        {
            throw new ForbiddenException(
                "Testlarni faqat o'quv bo'limi xodimi yoki administrator boshqaradi.");
        }

        return actor;
    }

    private async Task<User> LoadStudentAsync(long studentId, CancellationToken ct)
    {
        var actor = await LoadUserAsync(studentId, ct);

        return actor.Role == UserRole.Student
            ? actor
            : throw new ForbiddenException("Testni faqat o'quvchi yechadi.");
    }

    private async Task<User> LoadUserAsync(long userId, CancellationToken ct)
    {
        // Rol TOKEN'dan emas, BAZADAN — kirish tokeni 15 daqiqa yashaydi.
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        if (!user.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return user;
    }

    // ================================================================= ichki yordamchi

    private async Task EnsureTestExistsAsync(long testId, CancellationToken ct)
    {
        if (!await db.Tests.AsNoTracking().AnyAsync(t => t.Id == testId, ct))
            throw new NotFoundException(nameof(Test), testId);
    }

    /// <summary>
    /// Urinish bo'lsa TUZILMANI o'zgartirish TAQIQLANADI.
    ///
    /// Savol yoki variantni o'zgartirish allaqachon qo'yilgan ballarni
    /// ma'nosiz qilardi: o'quvchi 5 ball olgan savol o'chirilsa uning
    /// natijasi "5/3" ko'rinishida chiqardi va buni orqaga qaytarish
    /// imkonsiz edi.
    /// </summary>
    private async Task EnsureNoAttemptsAsync(long testId, string action, CancellationToken ct)
    {
        if (await db.TestAttempts.AsNoTracking().AnyAsync(a => a.TestId == testId, ct))
        {
            throw new ConflictException(
                $"Bu testni o'quvchilar yechishni boshlagan — {action} bo'lmaydi. "
                + "Natijalar ma'nosini yo'qotardi. Yangi test yarating.");
        }
    }

    private static void FillOptions(TestQuestion question, IReadOnlyList<SaveOptionRequest>? options)
    {
        var index = 0;

        foreach (var option in options ?? [])
        {
            question.Options.Add(new TestOption
            {
                Body = option.Body?.Trim() ?? string.Empty,
                IsCorrect = option.IsCorrect,
                Position = option.Position ?? index,
            });

            index++;
        }
    }

    private IQueryable<TestQuestion> OrderedQuestions(long testId) =>
        db.TestQuestions
            .AsNoTracking()
            .Where(q => q.TestId == testId)
            // Tartib BARQAROR: `Position` teng bo'lsa `Id` ajratadi. Aks holda
            // o'quvchi sahifani yangilaganda savollar joyini almashtirardi.
            .OrderBy(q => q.Position)
            .ThenBy(q => q.Id);

    private async Task<AuthoringQuestionDto> LoadQuestionDtoAsync(long questionId, CancellationToken ct) =>
        await db.TestQuestions
            .AsNoTracking()
            .Where(q => q.Id == questionId)
            .Select(q => new AuthoringQuestionDto(
                q.Id, q.Body, q.ImageKey, q.Position, q.Points,
                q.Options.Count(o => o.IsCorrect) > 1,
                q.Options
                    .OrderBy(o => o.Position)
                    .ThenBy(o => o.Id)
                    .Select(o => new AuthoringOptionDto(o.Id, o.Body, o.Position, o.IsCorrect))
                    .ToList()))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(TestQuestion), questionId);

    /// <summary>
    /// Urinish -> "boshlandi" javobi.
    ///
    /// Test BUTUNLIGICHA uzatiladi (faqat `TimeLimitMinutes` emas): muddat
    /// hisobiga `DueAt` ham kiradi va uni yo'qotib qo'yish mumkin bo'lmasin.
    /// </summary>
    private static StartAttemptDto Describe(TestAttempt attempt, Test test) => new(
        attempt.Id,
        attempt.TestId,
        attempt.StartedAt,

        // Muddat SERVER hisobi bo'yicha (tolerantlik ichida) — klient
        // taymeri shunga sozlanadi.
        attempt.Deadline(test),
        test.TimeLimitMinutes);

    private static MyResultDto Map(Test test, TestAttempt attempt) => new(
        test.Id,
        test.Title,
        attempt.Id,
        attempt.Status,
        attempt.Score,
        attempt.MaxScore,
        attempt.Percent,
        attempt.StartedAt,
        attempt.SubmittedAt,
        attempt.ClosedByTimeout);

    /// <summary>
    /// Test -> DTO. `MaxScore` BAZADA yig'iladi.
    ///
    /// `(decimal?)` ga o'girish MAJBURIY: savolsiz testda SQL `SUM()` NULL
    /// qaytaradi va uni `decimal` ga o'girishda so'rov yiqilardi.
    /// </summary>
    private IQueryable<TestDto> Project(IQueryable<Test> rows) =>
        rows.Select(t => new TestDto(
            t.Id,
            t.Title,
            t.Description,
            t.Kind,
            t.ModuleLessonId,
            t.ModuleLesson == null ? null : t.ModuleLesson.Name,
            t.TimeLimitMinutes,
            t.DueAt,
            t.IsPublished,
            t.CreatedById,
            t.Questions.Count,
            t.Questions.Sum(q => (decimal?)q.Points) ?? 0m,
            db.TestAttempts.Count(a => a.TestId == t.Id && a.Status == AttemptStatus.Submitted),
            t.CreatedAt,
            t.UpdatedAt));

    private static ConflictException DoubleSubmit() =>
        new("Bu test ayni damda topshirildi (ikki marta yuborilgan). "
            + "Natijangizni \"Mening natijam\" bo'limida ko'ring.");

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>CSV yacheykasi: vergul, qo'shtirnoq va qator ko'chirishni zararsizlantiradi.</summary>
    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;

        return text.AsSpan().IndexOfAny(',', '"', '\n') >= 0
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }

    private static string Number(decimal? value) =>
        value?.ToString("0.##", Invariant) ?? string.Empty;

    // ---------------------------------------------------------------- doimiylar va ichki turlar

    private const int MaxPageSize = 100;

    private const string NoGroupLabel = "Guruhsiz";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private sealed record ResultRow(
        long AttemptId,
        long StudentId,
        string StudentName,
        List<string> GroupNames,
        decimal? Score,
        decimal? MaxScore,
        DateTimeOffset? SubmittedAt,
        bool ClosedByTimeout);

    private sealed record AttemptBrief(AttemptStatus Status, decimal? Score);

    private sealed record AvailableRow(
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
        AttemptBrief? MyAttempt);
}
