using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// DARS YOZUVINING KO'RINISHI (talab R5) — <c>/api/v1/recordings</c>
/// ========================================================================
///
/// Loyiha egasi: *"dars yozuvlari qismi student uchun dynamic bo'lishi
/// kerak, o'quv bo'limi va teacher tarafidan manage qilinadi, ko'rinish
/// yoki ko'rinmasligi, entire part of records"*.
///
/// Bu testlar TO'RT narsani qo'riqlaydi va ularning har biri alohida
/// sababga ega:
///
///   1) 🔴 STANDART — KO'RINADI. Bu MIGRATSIYA qarori: <c>false</c> bo'lsa
///      deploy kunida hamma o'quvchining bo'limi bo'shab qolardi.
///
///   2) 🔴 HAVOLA ENDPOINTI HAM YOPILADI, faqat ro'yxat emas. O'quvchi
///      yozuv Id'sini bilishi mumkin (kecha ochiq turgan sahifa,
///      xatcho'p, brauzer tarixi) va faqat ro'yxatni filtrlash
///      "ko'rinmasin" ni emas, "topish qiyinroq bo'lsin" ni anglatardi.
///
///   3) 🔴 YOPILGAN YOZUV VA TO'LOV QARZI — IKKI XIL NOSOZLIK. Xabarlari
///      ham har xil: aks holda yopilgan darsni ko'rmoqchi bo'lgan
///      o'quvchi buxgalteriyaga borardi.
///
///   4) 🔴 USTOZ O'QUV BO'LIMI YOPGANINI QAYTA OCHA OLMAYDI. Aks holda
///      sifat nazoratining (R29) yagona amaliy vositasi kuchsiz
///      maslahatga aylanardi.
/// </summary>
public sealed class RecordingVisibilityEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const string Base = "/api/v1/recordings";

    // ================================================================= 1) standart

    /// <summary>
    /// Yangi tayyor yozuv o'quvchiga DARHOL ko'rinadi — hech kim hech
    /// narsani "e'lon qilmasdan".
    /// </summary>
    [Fact]
    public async Task Recording_IsVisibleToStudentsByDefault()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rec-vis-std");
        var (sessionId, recordingId) = await AddCompletedRecordingAsync(world.GroupId);

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var list = await ForSessionAsync(student, sessionId);

        list.Should().ContainSingle().Which.Id.Should().Be(recordingId);
    }

    // ================================================================= 2) yozuv kaliti

    [Fact]
    public async Task HiddenRecording_DisappearsFromTheStudentListButStaysForStaff()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rec-vis-yop");
        var (sessionId, recordingId) = await AddCompletedRecordingAsync(world.GroupId);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        await HideAsync(teacher, recordingId);

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        (await ForSessionAsync(student, sessionId)).Should().BeEmpty();

        // XODIMGA QOLADI: o'quv bo'limi aynan shu yozuvni ko'rib xulosa
        // yozishi kerak (R29), ya'ni yashirish uni ARXIVDAN olib
        // tashlamaydi.
        (await ForSessionAsync(teacher, sessionId))
            .Should().ContainSingle()
            .Which.IsVisibleToStudents.Should().BeFalse();
    }

    /// <summary>
    /// 🔴 ENG MUHIM TEST: havola endpointi ham yopiladi.
    ///
    /// O'quvchi ro'yxatga qaramasdan, to'g'ridan-to'g'ri yozuv Id'si bilan
    /// keladi — aynan shunday bo'lishi mumkin (xatcho'p, tarix).
    /// </summary>
    [Fact]
    public async Task HiddenRecording_RefusesTheViewLink_EvenWithAKnownId()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rec-vis-link");
        var (_, recordingId) = await AddCompletedRecordingAsync(world.GroupId);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);
        await HideAsync(teacher, recordingId);

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await student.GetAsync(new Uri($"{Base}/{recordingId}/link", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(response));

        // 🔴 XABAR TO'LOV XABARIDAN FARQ QILISHI SHART: pleyer ikkalasini
        //    bir xil ko'rsatsa, o'quvchi yopilgan darsni "qarz" deb
        //    tushunardi.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("yopilgan");
        body.Should().NotContain("qarz");
    }

    /// <summary>Xodim uchun yashirilgan yozuv HAMON ochiladi.</summary>
    [Fact]
    public async Task HiddenRecording_IsStillOpenableByStaff()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rec-vis-xodim");
        var (_, recordingId) = await AddCompletedRecordingAsync(world.GroupId);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);
        await HideAsync(teacher, recordingId);

        var response = await teacher.GetAsync(new Uri($"{Base}/{recordingId}/link", UriKind.Relative));

        // 200 yoki 503 (ombor sozlanmagan bo'lsa) — LEKIN HECH QACHON 403.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, await WorldBuilder.Body(response));
    }

    // ================================================================= 3) guruh kaliti

    [Fact]
    public async Task GroupSwitch_HidesEveryRecordingOfThatGroup()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rec-vis-guruh");
        var (sessionId, _) = await AddCompletedRecordingAsync(world.GroupId);

        await factory.WithDbAsync(async db =>
        {
            var group = await db.Groups.FindAsync(world.GroupId);
            group!.RecordingsVisibleToStudents = false;
            await db.SaveChangesAsync();
            return 0;
        });

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        (await ForSessionAsync(student, sessionId)).Should().BeEmpty();

        // ★ Kirish kartochkasi ham yo'qoladi: o'quvchining YAGONA guruhi
        //   yopilgan, ya'ni bo'lim unga umuman bo'sh bo'lardi.
        var section = await student.GetFromJsonAsync<SectionResponse>(
            new Uri($"{Base}/section", UriKind.Relative));

        section!.Visible.Should().BeFalse();
    }

    // ================================================================= 4) ustunlik

    /// <summary>
    /// 🔴 O'QUV BO'LIMI YOPGANINI USTOZ QAYTA OCHA OLMAYDI.
    ///
    /// Bu — "eng qattig'i yutadi" qoidasining amaliy ma'nosi va R29 ning
    /// ishlash sharti.
    /// </summary>
    [Fact]
    public async Task TeacherCannotReopen_WhatTheAcademicDepartmentClosed()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rec-vis-ustun");
        var (_, recordingId) = await AddCompletedRecordingAsync(world.GroupId);

        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var academic = await WorldBuilder.CreateUserAsync(admin, UserRole.Academic, "rec-vis-oq");

        using var academicClient = await WorldBuilder.ClientAsync(factory, academic);
        await HideAsync(academicClient, recordingId);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.PatchAsJsonAsync(
            new Uri($"{Base}/{recordingId}/visibility", UriKind.Relative), new { visible = true });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(response));

        // O'quv bo'limi esa ocha oladi — eskalatsiya nuqtasi ochiq qoladi.
        var reopened = await academicClient.PatchAsJsonAsync(
            new Uri($"{Base}/{recordingId}/visibility", UriKind.Relative), new { visible = true });

        reopened.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(reopened));
    }

    /// <summary>
    /// Ustoz O'ZI yopganini qaytarib ocha oladi — xato bosish tuzatiladigan
    /// bo'lishi kerak.
    /// </summary>
    [Fact]
    public async Task TeacherCanReopen_WhatTheTeacherClosed()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rec-vis-oz");
        var (_, recordingId) = await AddCompletedRecordingAsync(world.GroupId);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        await HideAsync(teacher, recordingId);

        var response = await teacher.PatchAsJsonAsync(
            new Uri($"{Base}/{recordingId}/visibility", UriKind.Relative), new { visible = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        var dto = (await response.Content.ReadFromJsonAsync<RecordingResponse>())!;
        dto.IsVisibleToStudents.Should().BeTrue();
    }

    /// <summary>
    /// O'quvchi ko'rinishni umuman boshqara olmaydi (rol darvozasi).
    /// </summary>
    [Fact]
    public async Task Student_CannotChangeVisibility()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rec-vis-talaba");
        var (_, recordingId) = await AddCompletedRecordingAsync(world.GroupId);

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await student.PatchAsJsonAsync(
            new Uri($"{Base}/{recordingId}/visibility", UriKind.Relative), new { visible = false });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(response));
    }

    /// <summary>
    /// Tayyor bo'lmagan yozuvni ochib bo'lmaydi (domain qoidasi -> 409).
    /// </summary>
    [Fact]
    public async Task ShowingAnUnfinishedRecording_IsRejected()
    {
        var world = await WorldBuilder.CreateAsync(factory, "rec-vis-xom");

        var recordingId = await factory.WithDbAsync(async db =>
        {
            var sessionId = await AddSessionAsync(db, world.GroupId);

            var recording = new SessionRecording
            {
                SessionId = sessionId,
                ObjectKey = $"itest/{Guid.NewGuid():N}.mp4",
                Status = RecordingStatus.Requested,
                IsVisibleToStudents = false,
            };

            db.SessionRecordings.Add(recording);
            await db.SaveChangesAsync();
            return recording.Id;
        });

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.PatchAsJsonAsync(
            new Uri($"{Base}/{recordingId}/visibility", UriKind.Relative), new { visible = true });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await WorldBuilder.Body(response));
    }

    // ================================================================= yordamchilar

    private static async Task HideAsync(HttpClient client, long recordingId)
    {
        var response = await client.PatchAsJsonAsync(
            new Uri($"{Base}/{recordingId}/visibility", UriKind.Relative), new { visible = false });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));
    }

    private static async Task<IReadOnlyList<RecordingResponse>> ForSessionAsync(
        HttpClient client, long sessionId)
    {
        var response = await client.GetAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/recordings", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<List<RecordingResponse>>())!;
    }

    /// <summary>
    /// YAKUNLANGAN dars + TAYYOR yozuv. Bazaga to'g'ridan-to'g'ri yoziladi:
    /// yozuvni "haqiqiy" yo'l bilan olish LiveKit Egress'ini talab qilardi,
    /// bu testlar esa KO'RINISH qoidasini tekshiradi, yozuv oqimini emas
    /// (uni `Recordings/` bo'limidagi testlar qoplaydi).
    /// </summary>
    private Task<(long SessionId, long RecordingId)> AddCompletedRecordingAsync(long groupId) =>
        factory.WithDbAsync(async db =>
        {
            var sessionId = await AddSessionAsync(db, groupId);

            var recording = new SessionRecording
            {
                SessionId = sessionId,
                ObjectKey = $"itest/{Guid.NewGuid():N}.mp4",
                Status = RecordingStatus.Completed,
                DurationSeconds = 3600,
                SizeBytes = 1024,
                StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
                EndedAt = DateTimeOffset.UtcNow.AddHours(-1),
            };

            db.SessionRecordings.Add(recording);
            await db.SaveChangesAsync();

            return (sessionId, recording.Id);
        });

    private static async Task<long> AddSessionAsync(
        Zinnur.Infrastructure.Persistence.ApplicationDbContext db, long groupId)
    {
        var session = new LiveSession
        {
            GroupId = groupId,
            Type = SessionType.Teacher,
            Status = SessionStatus.Ended,
            ScheduledStart = DateTimeOffset.UtcNow.AddHours(-2),
            ScheduledEnd = DateTimeOffset.UtcNow.AddHours(-1),
            ActualStart = DateTimeOffset.UtcNow.AddHours(-2),
            ActualEnd = DateTimeOffset.UtcNow.AddHours(-1),
            RoomName = LiveSession.GenerateRoomName(),
        };

        db.LiveSessions.Add(session);
        await db.SaveChangesAsync();

        return session.Id;
    }

    private sealed record RecordingResponse(
        long Id, long SessionId, string Status, bool IsPlayable, bool IsVisibleToStudents,
        bool HasReview, string? ReviewStatus);

    private sealed record SectionResponse(bool Visible);
}
