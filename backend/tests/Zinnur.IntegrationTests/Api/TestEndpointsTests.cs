using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// Onlayn test endpointlari.
///
/// ★ Bu fayl eski tizimning TO'RT haqiqiy bugini qo'riqlaydi:
///   1) `/take` javobida to'g'ri javoblar oshkor bo'lishi;
///   2) ko'p to'g'ri javobli savol umuman ishlamasligi;
///   3) bir vaqtda ikki topshirish -> ikkita urinish yoki 500;
///   4) natijalarda ikki guruhdagi o'quvchining IKKI MARTA chiqishi.
/// </summary>
public sealed class TestEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const string StudentEmail = "student@zinnur.uz";
    private const string StudentPassword = "Demo!2345";

    // ================================================================== /take maxfiyligi

    /// <summary>
    /// ★★ ENG MUHIM TEST: yechish varaqasi to'g'ri javoblarni OSHKOR
    /// QILMASLIGI kerak.
    ///
    /// JSON XOM MATN sifatida tekshiriladi (DTO'ga o'girib emas): DTO'da
    /// maydon bo'lmasa u JIMGINA tashlanardi va test "o'tib" ketardi, holbuki
    /// javob tanasida `isCorrect` turgan bo'lardi. Matnni tekshirish esa
    /// haqiqatan simda nima ketganini ko'radi.
    /// </summary>
    [Fact]
    public async Task Take_DoesNotExposeCorrectAnswers()
    {
        var ids = await CreatePublishedTestAsync("Maxfiylik testi");

        using var student = await StudentClientAsync();
        await Start(student, ids.TestId);

        var response = await student.GetAsync(Take(ids.TestId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();

        json.Should().NotContain("isCorrect", "to'g'ri javob KLIENTGA yuborilmaydi");
        json.Should().NotContain("IsCorrect");
        json.Should().NotContain("correct");

        // Savol va variantlar esa BOR — varaqa haqiqatan yechish uchun yaroqli.
        json.Should().Contain("Bitta to'g'ri javob");
        json.Should().Contain("options");
        json.Should().Contain("multipleAnswers");
    }

    /// <summary>Tuzuvchi ko'rinishida esa to'g'ri javoblar KO'RINADI (aks holda tahrirlab bo'lmasdi).</summary>
    [Fact]
    public async Task GetForAuthoring_AsAdmin_DoesExposeCorrectAnswers()
    {
        var ids = await CreatePublishedTestAsync("Tuzuvchi ko'rinishi");

        using var admin = await AdminClientAsync();
        var json = await admin.GetStringAsync(new Uri($"/api/v1/tests/{ids.TestId}", UriKind.Relative));

        json.Should().Contain("isCorrect");
    }

    [Fact]
    public async Task Take_AsAdmin_ReturnsForbidden()
    {
        var ids = await CreatePublishedTestAsync("Xodim yechmaydi");

        using var admin = await AdminClientAsync();
        var response = await admin.GetAsync(Take(ids.TestId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================== baholash

    /// <summary>
    /// Barcha javob to'g'ri: 3 + 5 = 8 ball.
    /// Ikkinchi savolda IKKI to'g'ri variant bor — eski tizimda bu holat
    /// umuman ishlamasdi.
    /// </summary>
    [Fact]
    public async Task Submit_WithAllCorrect_ScoresFullMarks()
    {
        var ids = await CreatePublishedTestAsync("Hammasi to'g'ri");

        using var student = await StudentClientAsync();
        await Start(student, ids.TestId);

        var result = await Submit(student, ids.TestId, new
        {
            answers = new object[]
            {
                new { questionId = ids.SingleQuestionId, optionIds = new[] { ids.SingleCorrectId } },
                new { questionId = ids.MultiQuestionId, optionIds = ids.MultiCorrectIds },
            },
        });

        result.Score.Should().Be(8m);
        result.MaxScore.Should().Be(8m);
        result.Percent.Should().Be(100m);
        result.Status.Should().Be("Submitted");
    }

    /// <summary>
    /// ★ QISMAN tanlov: ikki to'g'ri variantdan faqat bittasi belgilangan ->
    /// o'sha savol uchun 0 ball ("hammasi yoki hech nima").
    /// </summary>
    [Fact]
    public async Task Submit_WithPartialMultiChoice_ScoresOnlyTheSingleQuestion()
    {
        var ids = await CreatePublishedTestAsync("Qisman tanlov");

        using var student = await StudentClientAsync();
        await Start(student, ids.TestId);

        var result = await Submit(student, ids.TestId, new
        {
            answers = new object[]
            {
                new { questionId = ids.SingleQuestionId, optionIds = new[] { ids.SingleCorrectId } },
                new { questionId = ids.MultiQuestionId, optionIds = new[] { ids.MultiCorrectIds[0] } },
            },
        });

        result.Score.Should().Be(3m, "ko'p tanlovli savolda qisman ball berilmaydi");
        result.MaxScore.Should().Be(8m);
    }

    /// <summary>★ To'g'ri variantlar + ORTIQCHA noto'g'ri variant -> 0 ball.</summary>
    [Fact]
    public async Task Submit_WithExtraWrongOption_ScoresZeroForThatQuestion()
    {
        var ids = await CreatePublishedTestAsync("Ortiqcha tanlov");

        using var student = await StudentClientAsync();
        await Start(student, ids.TestId);

        var selected = ids.MultiCorrectIds.Append(ids.MultiWrongId).ToArray();

        var result = await Submit(student, ids.TestId, new
        {
            answers = new object[]
            {
                new { questionId = ids.SingleQuestionId, optionIds = new[] { ids.SingleCorrectId } },
                new { questionId = ids.MultiQuestionId, optionIds = selected },
            },
        });

        result.Score.Should().Be(3m);
    }

    /// <summary>Javob yuborilmagan savol — 0 ball, lekin maksimal ball o'zgarmaydi.</summary>
    [Fact]
    public async Task Submit_WithNoAnswers_ScoresZero()
    {
        var ids = await CreatePublishedTestAsync("Bo'sh javob");

        using var student = await StudentClientAsync();
        await Start(student, ids.TestId);

        var result = await Submit(student, ids.TestId, new { answers = Array.Empty<object>() });

        result.Score.Should().Be(0m);
        result.MaxScore.Should().Be(8m);
    }

    /// <summary>
    /// ★ BEGONA VARIANT: klient birinchi savolga IKKINCHI savolning to'g'ri
    /// variantini yuboradi — u hisobga olinmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Submit_WithOptionFromAnotherQuestion_IgnoresIt()
    {
        var ids = await CreatePublishedTestAsync("Begona variant");

        using var student = await StudentClientAsync();
        await Start(student, ids.TestId);

        var result = await Submit(student, ids.TestId, new
        {
            answers = new object[]
            {
                new { questionId = ids.SingleQuestionId, optionIds = new[] { ids.MultiCorrectIds[0] } },
            },
        });

        result.Score.Should().Be(0m);
    }

    // ================================================================== takroriy topshirish

    /// <summary>★ Ikkinchi topshirish 409 qaytaradi — 500 EMAS.</summary>
    [Fact]
    public async Task Submit_Twice_ReturnsConflictNotServerError()
    {
        var ids = await CreatePublishedTestAsync("Ikki marta");

        using var student = await StudentClientAsync();
        await Start(student, ids.TestId);

        var payload = new
        {
            answers = new object[]
            {
                new { questionId = ids.SingleQuestionId, optionIds = new[] { ids.SingleCorrectId } },
            },
        };

        var first = await student.PostAsJsonAsync(SubmitUri(ids.TestId), payload);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await student.PostAsJsonAsync(SubmitUri(ids.TestId), payload);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ((int)second.StatusCode).Should().NotBe(500);
    }

    /// <summary>
    /// ★★ BIR VAQTDA ikki topshirish (o'quvchi tugmani ikki marta bosdi).
    ///
    /// Kutilgan natija: bittasi 200, ikkinchisi 409; bazada BITTA urinish va
    /// javob qatorlari FAQAT BIR MARTA yozilgan (ball ikki barobar bo'lmagan).
    /// Eski tizimda bu 500 yoki ikkilangan ball berardi.
    /// </summary>
    [Fact]
    public async Task Submit_Concurrently_KeepsOneAttemptAndReturnsConflictForLoser()
    {
        var ids = await CreatePublishedTestAsync("Poyga");

        using var student = await StudentClientAsync();
        var attempt = await Start(student, ids.TestId);

        var payload = new
        {
            answers = new object[]
            {
                new { questionId = ids.SingleQuestionId, optionIds = new[] { ids.SingleCorrectId } },
                new { questionId = ids.MultiQuestionId, optionIds = ids.MultiCorrectIds },
            },
        };

        // Ikki so'rov ATAYLAB parallel: `Task.WhenAll` ikkalasini ham
        // navbatga qo'yadi va ular bir vaqtda serverga tushadi.
        var responses = await Task.WhenAll(
            student.PostAsJsonAsync(SubmitUri(ids.TestId), payload),
            student.PostAsJsonAsync(SubmitUri(ids.TestId), payload));

        var codes = responses.Select(r => r.StatusCode).ToList();

        codes.Should().Contain(HttpStatusCode.OK);
        codes.Should().NotContain(HttpStatusCode.InternalServerError, "500 QABUL QILINMAYDI");

        codes.Count(c => c == HttpStatusCode.OK).Should().Be(1, "faqat bittasi o'tishi kerak");
        codes.Count(c => c == HttpStatusCode.Conflict).Should().Be(1, "yutqazgani 409 olishi kerak");

        foreach (var response in responses) response.Dispose();

        // BAZA HOLATI: bitta urinish, javob qatorlari bir marta.
        var attempts = await factory.WithDbAsync(db =>
            db.TestAttempts.CountAsync(a => a.TestId == ids.TestId));

        attempts.Should().Be(1);

        var answers = await factory.WithDbAsync(db =>
            db.TestAnswers.CountAsync(a => a.AttemptId == attempt.AttemptId));

        answers.Should().Be(3, "1 + 2 tanlov: har TANLOV uchun bitta qator");
    }

    /// <summary>
    /// Boshlashni ikki marta chaqirish IDEMPOTENT: ayni urinish qaytadi va
    /// `startedAt` O'ZGARMAYDI (aks holda sahifani yangilab taymerni cheksiz
    /// cho'zish mumkin bo'lardi).
    /// </summary>
    [Fact]
    public async Task Start_Twice_ReturnsSameAttemptAndKeepsTimer()
    {
        var ids = await CreatePublishedTestAsync("Qayta boshlash", timeLimitMinutes: 30);

        using var student = await StudentClientAsync();

        var first = await Start(student, ids.TestId);
        var second = await Start(student, ids.TestId);

        second.AttemptId.Should().Be(first.AttemptId);

        // Vaqt MIKROSEKUND aniqligida solishtiriladi: birinchi javob
        // xotiradagi qiymatni (100 ns), ikkinchisi esa BAZADAN o'qilganini
        // (`timestamptz` — mikrosekund) qaytaradi. Farq taymerga ta'sir
        // qilmaydi, lekin qat'iy tenglik testni tasodifan yiqitardi.
        second.StartedAt.Should().BeCloseTo(first.StartedAt, TimeSpan.FromMilliseconds(1));
        second.Deadline!.Value.Should()
            .BeCloseTo(first.Deadline!.Value, TimeSpan.FromMilliseconds(1));
        second.Deadline.Should().NotBeNull("vaqt chegarasi bor testda muddat hisoblanadi");
    }

    /// <summary>★ Muddati o'tgan test: `due_at` SERVERDA tekshiriladi.</summary>
    [Fact]
    public async Task Start_AfterDueDate_ReturnsConflict()
    {
        var ids = await CreatePublishedTestAsync(
            "Muddati o'tgan", dueAt: DateTimeOffset.UtcNow.AddDays(-1));

        using var student = await StudentClientAsync();

        var response = await student.PostAsync(
            new Uri($"/api/v1/tests/{ids.TestId}/start", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("muddat", "sabab foydalanuvchiga aytiladi");
    }

    [Fact]
    public async Task Take_WithoutStarting_ReturnsConflict()
    {
        var ids = await CreatePublishedTestAsync("Boshlanmagan");

        using var student = await StudentClientAsync();
        var response = await student.GetAsync(Take(ids.TestId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ================================================================== e'lon qilish

    [Fact]
    public async Task Publish_WithoutQuestions_ReturnsConflict()
    {
        using var admin = await AdminClientAsync();

        var created = await CreateDraftAsync(admin, "Bo'sh test");

        var response = await admin.PostAsync(
            new Uri($"/api/v1/tests/{created}/publish", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AddQuestion_WithSingleOption_ReturnsConflict()
    {
        using var admin = await AdminClientAsync();
        var testId = await CreateDraftAsync(admin, "Nuqsonli savol");

        var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/tests/{testId}/questions", UriKind.Relative),
            new
            {
                body = "Yagona variant",
                points = 1m,
                options = new[] { new { body = "A", isCorrect = true } },
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "kamida 2 variant kerak");
    }

    [Fact]
    public async Task AddQuestion_WithNoCorrectOption_ReturnsConflict()
    {
        using var admin = await AdminClientAsync();
        var testId = await CreateDraftAsync(admin, "To'g'ri javobsiz");

        var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/tests/{testId}/questions", UriKind.Relative),
            new
            {
                body = "To'g'ri javob belgilanmagan",
                points = 1m,
                options = new[]
                {
                    new { body = "A", isCorrect = false },
                    new { body = "B", isCorrect = false },
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Urinish boshlangach savolni o'zgartirish TAQIQ (natijalar ma'nosini yo'qotardi).</summary>
    [Fact]
    public async Task AddQuestion_AfterAttemptStarted_ReturnsConflict()
    {
        var ids = await CreatePublishedTestAsync("Urinishdan keyin");

        using var student = await StudentClientAsync();
        await Start(student, ids.TestId);

        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/tests/{ids.TestId}/questions", UriKind.Relative),
            new
            {
                body = "Kechikkan savol",
                points = 1m,
                options = new[]
                {
                    new { body = "A", isCorrect = true },
                    new { body = "B", isCorrect = false },
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_AsStudent_ReturnsForbidden()
    {
        using var student = await StudentClientAsync();

        var response = await student.PostAsJsonAsync(
            new Uri("/api/v1/tests", UriKind.Relative),
            new { title = "O'quvchi tuzgan test", kind = "Competition" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================== natijalar

    /// <summary>
    /// ★★ IKKI GURUHDAGI O'QUVCHI natijalarda BIR MARTA chiqishi kerak.
    ///
    /// Eski tizim `outerjoin(GroupMember).outerjoin(Group)` qilardi va bunday
    /// o'quvchi ikki qator bo'lib chiqardi — reyting, o'rtacha ball va CSV
    /// eksport buzilardi.
    /// </summary>
    [Fact]
    public async Task Results_ForStudentInTwoGroups_ReturnsExactlyOneRow()
    {
        var ids = await CreatePublishedTestAsync("Ikki guruh");

        var studentId = await StudentIdAsync();
        await AddToExtraGroupAsync(studentId, "Qo'shimcha guruh (test)");

        using var student = await StudentClientAsync();
        await Start(student, ids.TestId);

        await Submit(student, ids.TestId, new
        {
            answers = new object[]
            {
                new { questionId = ids.SingleQuestionId, optionIds = new[] { ids.SingleCorrectId } },
            },
        });

        using var admin = await AdminClientAsync();

        var rows = await admin.GetFromJsonAsync<List<ResultRowDto>>(
            $"/api/v1/tests/{ids.TestId}/results");

        rows.Should().NotBeNull();
        rows!.Count(r => r.StudentId == studentId).Should().Be(1, "bitta urinish = bitta qator");

        var row = rows!.Single(r => r.StudentId == studentId);
        row.GroupNames.Should().Contain("Qo'shimcha guruh (test)");
        row.GroupNames.Should().Contain(",", "ikkinchi guruh ham bir yacheykada ko'rsatiladi");
        row.Score.Should().Be(3m);
    }

    /// <summary>CSV eksportda ham takror qator bo'lmaydi va BOM mavjud (Excel uchun).</summary>
    [Fact]
    public async Task ExportResults_ReturnsCsvWithBomAndOneRowPerAttempt()
    {
        var ids = await CreatePublishedTestAsync("CSV eksport");

        var studentId = await StudentIdAsync();
        await AddToExtraGroupAsync(studentId, "CSV guruh (test)");

        using var student = await StudentClientAsync();
        await Start(student, ids.TestId);

        await Submit(student, ids.TestId, new
        {
            answers = new object[]
            {
                new { questionId = ids.SingleQuestionId, optionIds = new[] { ids.SingleCorrectId } },
            },
        });

        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync(
            new Uri($"/api/v1/tests/{ids.TestId}/results/export", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var bytes = await response.Content.ReadAsByteArrayAsync();

        bytes.Take(3).Should().Equal([(byte)0xEF, (byte)0xBB, (byte)0xBF], "Excel BOM kutadi");

        var csv = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().StartWith("F.I.Sh.");
        lines.Count(l => l.Contains("Demo O'quvchi", StringComparison.Ordinal))
            .Should().Be(1, "ikki guruhdagi o'quvchi CSV'da ham bir marta");
    }

    [Fact]
    public async Task Results_AsStudent_ReturnsForbidden()
    {
        var ids = await CreatePublishedTestAsync("Natija maxfiy");

        using var student = await StudentClientAsync();

        var response = await student.GetAsync(
            new Uri($"/api/v1/tests/{ids.TestId}/results", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================== mavjud testlar

    [Fact]
    public async Task Available_ListsPublishedCompetitionTests()
    {
        var ids = await CreatePublishedTestAsync("Mavjud musobaqa");

        using var student = await StudentClientAsync();

        var available = await student.GetFromJsonAsync<List<AvailableDto>>(
            "/api/v1/tests/available");

        available.Should().NotBeNull();
        available!.Should().Contain(t => t.Id == ids.TestId);

        var mine = available!.First(t => t.Id == ids.TestId);
        mine.CanStart.Should().BeTrue();
        mine.QuestionCount.Should().Be(2);
        mine.MaxScore.Should().Be(8m);
    }

    [Fact]
    public async Task Available_DoesNotListDraftTests()
    {
        using var admin = await AdminClientAsync();
        var draftId = await CreateDraftAsync(admin, "Qoralama");

        using var student = await StudentClientAsync();

        var available = await student.GetFromJsonAsync<List<AvailableDto>>(
            "/api/v1/tests/available");

        available!.Should().NotContain(t => t.Id == draftId);
    }

    [Fact]
    public async Task MyResult_AfterSubmit_ReturnsScore()
    {
        var ids = await CreatePublishedTestAsync("Mening natijam");

        using var student = await StudentClientAsync();
        await Start(student, ids.TestId);

        await Submit(student, ids.TestId, new
        {
            answers = new object[]
            {
                new { questionId = ids.MultiQuestionId, optionIds = ids.MultiCorrectIds },
            },
        });

        var result = await student.GetFromJsonAsync<MyResultDto>(
            $"/api/v1/tests/{ids.TestId}/my-result");

        result!.Score.Should().Be(5m);
        result.Status.Should().Be("Submitted");
    }

    // ================================================================== yordamchi

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> StudentClientAsync()
    {
        var tokens = await factory.LoginAsync(StudentEmail);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private Task<long> StudentIdAsync() => factory.WithDbAsync(db =>
        db.Users.Where(u => u.Email == StudentEmail).Select(u => u.Id).FirstAsync());

    /// <summary>Ikkinchi guruh — KURSSIZ, shuning uchun gating'ga ta'sir qilmaydi.</summary>
    private async Task AddToExtraGroupAsync(long studentId, string groupName)
    {
        await factory.WithDbAsync(async db =>
        {
            var existing = await db.Groups.FirstOrDefaultAsync(g => g.Name == groupName);

            if (existing is null)
            {
                existing = new Group
                {
                    Name = groupName,
                    StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    Weekdays = [DayOfWeek.Monday, DayOfWeek.Thursday],
                };

                db.Groups.Add(existing);
                await db.SaveChangesAsync();
            }

            if (!await db.GroupMembers.AnyAsync(
                    m => m.GroupId == existing.Id && m.StudentId == studentId))
            {
                db.GroupMembers.Add(new GroupMember
                {
                    GroupId = existing.Id,
                    StudentId = studentId,
                    Status = MemberStatus.Active,
                });
            }

            return await db.SaveChangesAsync();
        });
    }

    private static async Task<long> CreateDraftAsync(HttpClient admin, string title)
    {
        var response = await admin.PostAsJsonAsync(
            new Uri("/api/v1/tests", UriKind.Relative),
            new { title, kind = "Competition" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<TestDto>();
        return created!.Id;
    }

    /// <summary>
    /// Ikki savolli e'lon qilingan musobaqa testi:
    ///   1-savol — BITTA to'g'ri javob, 3 ball;
    ///   2-savol — IKKITA to'g'ri javob, 5 ball (asosiy tekshiruv nishoni).
    /// </summary>
    private async Task<TestIds> CreatePublishedTestAsync(
        string title, int? timeLimitMinutes = null, DateTimeOffset? dueAt = null)
    {
        using var admin = await AdminClientAsync();

        var createResponse = await admin.PostAsJsonAsync(
            new Uri("/api/v1/tests", UriKind.Relative),
            new
            {
                title,
                kind = "Competition",
                timeLimitMinutes,
                dueAt,
            });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var test = await createResponse.Content.ReadFromJsonAsync<TestDto>();

        var single = await AddQuestionAsync(admin, test!.Id, new
        {
            body = "Bitta to'g'ri javob",
            points = 3m,
            options = new[]
            {
                new { body = "To'g'ri", isCorrect = true },
                new { body = "Noto'g'ri", isCorrect = false },
            },
        });

        var multi = await AddQuestionAsync(admin, test.Id, new
        {
            body = "Ikkita to'g'ri javob",
            points = 5m,
            options = new[]
            {
                new { body = "To'g'ri 1", isCorrect = true },
                new { body = "To'g'ri 2", isCorrect = true },
                new { body = "Noto'g'ri", isCorrect = false },
            },
        });

        var publish = await admin.PostAsync(
            new Uri($"/api/v1/tests/{test.Id}/publish", UriKind.Relative), content: null);

        publish.StatusCode.Should().Be(HttpStatusCode.OK);

        return new TestIds(
            test.Id,
            single.Id,
            single.Options.First(o => o.IsCorrect).Id,
            multi.Id,
            multi.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToArray(),
            multi.Options.First(o => !o.IsCorrect).Id);
    }

    private static async Task<QuestionDto> AddQuestionAsync(
        HttpClient admin, long testId, object payload)
    {
        var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v1/tests/{testId}/questions", UriKind.Relative), payload);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<QuestionDto>())!;
    }

    private static async Task<StartDto> Start(HttpClient student, long testId)
    {
        var response = await student.PostAsync(
            new Uri($"/api/v1/tests/{testId}/start", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<StartDto>())!;
    }

    private static async Task<MyResultDto> Submit(HttpClient student, long testId, object payload)
    {
        var response = await student.PostAsJsonAsync(SubmitUri(testId), payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<MyResultDto>())!;
    }

    private static Uri SubmitUri(long testId) =>
        new(string.Create(CultureInfo.InvariantCulture, $"/api/v1/tests/{testId}/submit"),
            UriKind.Relative);

    private static Uri Take(long testId) =>
        new(string.Create(CultureInfo.InvariantCulture, $"/api/v1/tests/{testId}/take"),
            UriKind.Relative);

    // ---------------------------------------------------------------- javob shakllari

    private sealed record TestIds(
        long TestId,
        long SingleQuestionId,
        long SingleCorrectId,
        long MultiQuestionId,
        long[] MultiCorrectIds,
        long MultiWrongId);

    private sealed record TestDto(long Id, string Title, bool IsPublished, int QuestionCount);

    private sealed record QuestionDto(long Id, string Body, decimal Points, List<OptionDto> Options);

    private sealed record OptionDto(long Id, string Body, int Position, bool IsCorrect);

    private sealed record StartDto(
        long AttemptId, long TestId, DateTimeOffset StartedAt, DateTimeOffset? Deadline);

    private sealed record MyResultDto(
        long TestId, long AttemptId, string Status, decimal? Score, decimal? MaxScore, decimal? Percent);

    private sealed record ResultRowDto(
        long AttemptId, long StudentId, string StudentName, string GroupNames,
        decimal? Score, decimal? MaxScore, decimal? Percent);

    private sealed record AvailableDto(
        long Id, string Title, string Kind, int QuestionCount, decimal MaxScore, bool CanStart);
}
