using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// Guruh va jadval endpointlari — HAQIQIY baza bilan.
///
/// NIMA UCHUN AYNAN BU TESTLAR: FAZA 2.2/2.3 ning eng qimmat qoidasi
/// "guruh tahrirlanganda jadvalga NIMA BO'LADI" degan savol. Eski tizimda
/// jadval SHARTSIZ qayta tuzilardi va har tahrirda:
///   • kelajakdagi barcha dars Id'lari o'zgarardi,
///   • LiveKit xona nomlari almashib, tarqatilgan havolalar buzilardi,
///   • o'tgan darslarning davomati va chati kaskad bilan yo'q bo'lardi.
///
/// Unit testlar generator MATEMATIKASINI qo'riqlaydi; bu yerdagi testlar esa
/// BAZA bilan chegarani qo'riqlaydi: `integer[]` massiv xaritalanishi, soya
/// ustun, tranzaksiya va dars Id'larining SAQLANISHI.
/// </summary>
public sealed class GroupEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const int DurationMinutes = 80;
    private const int CourseMonths = 8;
    private const string StartTime = "19:00:00";

    /// <summary>Toshkent — DST yo'q, doimiy UTC+5. 19:00 mahalliy == 14:00Z.</summary>
    private static readonly TimeSpan ExpectedUtcTimeOfDay = new(14, 0, 0);

    private static readonly string[] MondayWednesday = ["Monday", "Wednesday"];
    private static readonly string[] TuesdayThursday = ["Tuesday", "Thursday"];

    // ================================================================= 1) yaratish

    /// <summary>
    /// Guruh yaratilishi bilan BUTUN kurs jadvali paydo bo'lishi kerak —
    /// "jadvali yo'q guruh" holati bo'lmasin (uni keyin qo'lda tuzatish
    /// kerak bo'lardi).
    /// </summary>
    [Fact]
    public async Task Create_GeneratesTheWholeScheduleImmediately()
    {
        using var client = await AdminClientAsync();
        var teacherId = await CreateStaffAsync(client, UserRole.Teacher);

        var startDate = FutureStart(DayOfWeek.Monday);
        var created = await CreateGroupAsync(client, Payload("IT-yaratish", startDate, teacherId));

        created.SessionsCreated.Should().BeGreaterThan(60,
            "8 oy × haftada 2 kun ≈ 70 dars");

        var schedule = await ScheduleAsync(client, created.Group.Id);

        schedule.Should().HaveCount(created.SessionsCreated,
            "javobdagi sanoq bazadagi jadval bilan mos bo'lishi kerak");

        created.Group.SessionCount.Should().Be(created.SessionsCreated);
        created.Group.Weekdays.Should().Equal(MondayWednesday,
            "`integer[]` ustuni enum nomlariga qaytib o'girilishi kerak");
        created.Group.EndDate.Should().Be(startDate.AddMonths(CourseMonths));
    }

    /// <summary>
    /// Har dars TO'G'RI kunda, TO'G'RI soatda (14:00Z = 19:00 Toshkent) va
    /// TO'G'RI davomiylikda bo'lishi kerak.
    ///
    /// Soat alohida tekshiriladi: konteyner UTC'da ishlaydi va
    /// `TimeZoneInfo.Local` ishlatilsa jadval BESH SOATGA siljib ketardi —
    /// buni faqat birinchi dars o'tib ketganda sezish mumkin bo'lardi.
    /// </summary>
    [Fact]
    public async Task Create_PlacesEverySessionOnTheRightWeekdayAtTashkentSeven()
    {
        using var client = await AdminClientAsync();
        var teacherId = await CreateStaffAsync(client, UserRole.Teacher);

        var created = await CreateGroupAsync(
            client, Payload("IT-vaqt", FutureStart(DayOfWeek.Monday), teacherId));

        var schedule = await ScheduleAsync(client, created.Group.Id);

        schedule.Should().AllSatisfy(session =>
        {
            session.ScheduledStart.UtcDateTime.TimeOfDay.Should().Be(ExpectedUtcTimeOfDay);
            (session.ScheduledEnd - session.ScheduledStart)
                .Should().Be(TimeSpan.FromMinutes(DurationMinutes));
            session.Status.Should().Be(nameof(SessionStatus.Scheduled));
            session.HostId.Should().Be(teacherId);
        });

        WeekdaysOf(schedule).Should().Equal([DayOfWeek.Monday, DayOfWeek.Wednesday]);

        // Eski tizimning B-4 bugi: xona nomi takrorlanib, LiveKit webhook'i
        // yiqilardi va o'sha kungi butun davomat yozilmay qolardi.
        schedule.Select(s => s.RoomName).Should().OnlyHaveUniqueItems();

        schedule[0].Title.Should().Be("IT-vaqt — 1-dars");
        schedule[1].Title.Should().Be("IT-vaqt — 2-dars");
    }

    // ================================================================= 2) faqat ustoz

    /// <summary>
    /// ★ FAQAT USTOZ o'zgardi -> jadval QAYTA TUZILMAYDI.
    ///
    /// Dars Id'lari va LiveKit xona nomlari O'ZGARMASLIGI shart: ular tashqi
    /// havolalarda, yozuvlarda va davomat yozuvlarida ishlatiladi. Eski tizim
    /// bu yerda butun kelajak jadvalni o'chirib qayta yaratardi.
    /// </summary>
    [Fact]
    public async Task Update_WithOnlyTheTeacherChanged_KeepsSessionIdsAndRoomNames()
    {
        using var client = await AdminClientAsync();

        var firstTeacher = await CreateStaffAsync(client, UserRole.Teacher);
        var secondTeacher = await CreateStaffAsync(client, UserRole.Teacher);

        var startDate = FutureStart(DayOfWeek.Monday);
        var created = await CreateGroupAsync(client, Payload("IT-ustoz", startDate, firstTeacher));

        var before = await ScheduleAsync(client, created.Group.Id);

        var updated = await UpdateGroupAsync(
            client, created.Group.Id, Payload("IT-ustoz", startDate, secondTeacher));

        // ---- hisobot ----
        updated.Schedule.Regenerated.Should().BeFalse("jadval qoidasi o'zgarmadi");
        updated.Schedule.ScheduleTouched.Should().BeTrue("host o'rnida yangilandi");
        updated.Schedule.HostsUpdated.Should().Be(before.Count);
        updated.Schedule.TitlesUpdated.Should().Be(0, "nom o'zgarmadi");
        updated.Schedule.Created.Should().Be(0);
        updated.Schedule.Deleted.Should().Be(0);

        // ---- jadvalning o'zi ----
        var after = await ScheduleAsync(client, created.Group.Id);

        after.Select(s => s.Id).Should().Equal(before.Select(s => s.Id),
            "dars Id'lari SAQLANISHI shart — ular tashqi havolalarda ishlatiladi");

        after.Select(s => s.RoomName).Should().Equal(before.Select(s => s.RoomName),
            "LiveKit xona nomlari SAQLANISHI shart");

        after.Select(s => s.ScheduledStart).Should().Equal(before.Select(s => s.ScheduledStart));

        after.Should().AllSatisfy(session => session.HostId.Should().Be(secondTeacher),
            "yangi ustoz kelajakdagi darslarning hosti bo'lishi kerak");
    }

    /// <summary>
    /// Faqat NOM o'zgardi -> sarlavhalar o'rnida yangilanadi, Id'lar qoladi.
    /// </summary>
    [Fact]
    public async Task Update_WithOnlyTheNameChanged_RenamesSessionsInPlace()
    {
        using var client = await AdminClientAsync();
        var teacherId = await CreateStaffAsync(client, UserRole.Teacher);

        var startDate = FutureStart(DayOfWeek.Monday);
        var created = await CreateGroupAsync(client, Payload("IT-nom-eski", startDate, teacherId));
        var before = await ScheduleAsync(client, created.Group.Id);

        var updated = await UpdateGroupAsync(
            client, created.Group.Id, Payload("IT-nom-yangi", startDate, teacherId));

        updated.Schedule.Regenerated.Should().BeFalse();
        updated.Schedule.TitlesUpdated.Should().Be(before.Count);
        updated.Schedule.HostsUpdated.Should().Be(0);

        var after = await ScheduleAsync(client, created.Group.Id);

        after.Select(s => s.Id).Should().Equal(before.Select(s => s.Id));
        after[0].Title.Should().Be("IT-nom-yangi — 1-dars");
    }

    /// <summary>
    /// ★ ESKI TIZIMNING ASOSIY BUGI: faqat KURS almashtirilganda ham butun
    /// kelajak jadval o'chib qayta yaratilardi. Endi jadvalga UMUMAN
    /// tegilmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Update_WithOnlyTheCourseChanged_DoesNotTouchTheScheduleAtAll()
    {
        using var client = await AdminClientAsync();
        var teacherId = await CreateStaffAsync(client, UserRole.Teacher);

        var startDate = FutureStart(DayOfWeek.Monday);
        var created = await CreateGroupAsync(client, Payload("IT-kurs", startDate, teacherId));
        var before = await ScheduleAsync(client, created.Group.Id);

        var courseId = await FirstCourseIdAsync();

        var updated = await UpdateGroupAsync(client, created.Group.Id, new
        {
            name = "IT-kurs",
            startDate = startDate.ToString("O", CultureInfo.InvariantCulture),
            weekdays = MondayWednesday,
            startTime = StartTime,
            type = nameof(GroupType.Group),
            durationMinutes = DurationMinutes,
            courseMonths = CourseMonths,
            courseId,
            teacherId,
            recordEnabled = true,
            isActive = true,
        });

        updated.Schedule.ScheduleTouched.Should().BeFalse(
            "kurs va yozuv bayrog'i jadval qoidasi EMAS");
        updated.Schedule.Regenerated.Should().BeFalse();
        updated.Group.CourseId.Should().Be(courseId);

        var after = await ScheduleAsync(client, created.Group.Id);
        after.Select(s => s.Id).Should().Equal(before.Select(s => s.Id));
        after.Select(s => s.RoomName).Should().Equal(before.Select(s => s.RoomName));
    }

    // ================================================================= 3) kun o'zgarishi

    /// <summary>
    /// ★ JADVAL QOIDASI o'zgardi -> qayta tuziladi, LEKIN:
    ///   • o'tgan darslar SAQLANADI (davomat va chat tarixini olib yuradi),
    ///   • YAKUNLANGAN dars SAQLANADI,
    ///   • faqat kelajakdagi `Scheduled` darslar almashtiriladi.
    ///
    /// Eski tizim bu yerda hammasini o'chirardi va o'tgan darslarning davomati
    /// kaskad bilan yo'q bo'lardi.
    /// </summary>
    [Fact]
    public async Task Update_WithChangedWeekdays_PreservesPastAndEndedSessions()
    {
        using var client = await AdminClientAsync();
        var teacherId = await CreateStaffAsync(client, UserRole.Teacher);

        // Kurs IKKI OY OLDIN boshlangan -> jadvalda o'tgan darslar bor.
        var startDate = PastStart(DayOfWeek.Monday);
        var created = await CreateGroupAsync(client, Payload("IT-kunlar", startDate, teacherId));

        var before = await ScheduleAsync(client, created.Group.Id);
        var now = DateTimeOffset.UtcNow;

        var pastIds = before.Where(s => s.ScheduledStart <= now).Select(s => s.Id).ToList();
        var futureIds = before.Where(s => s.ScheduledStart > now).Select(s => s.Id).ToList();

        pastIds.Should().NotBeEmpty("test ma'lumoti o'tgan darslarni talab qiladi");
        futureIds.Should().NotBeEmpty("test ma'lumoti kelajak darslarni talab qiladi");

        // O'tgan darslardan bittasini YAKUNLANGAN qilamiz — u davomat va chat
        // tarixiga ega dars vazifasini bajaradi.
        var endedId = pastIds[^1];
        await MarkEndedAsync(endedId);

        // ---- kunlarni almashtiramiz: dushanba/chorshanba -> seshanba/payshanba
        var updated = await UpdateGroupAsync(client, created.Group.Id, new
        {
            name = "IT-kunlar",
            startDate = startDate.ToString("O", CultureInfo.InvariantCulture),
            weekdays = TuesdayThursday,
            startTime = StartTime,
            type = nameof(GroupType.Group),
            durationMinutes = DurationMinutes,
            courseMonths = CourseMonths,
            teacherId,
            isActive = true,
        });

        updated.Schedule.Regenerated.Should().BeTrue();
        updated.Schedule.Deleted.Should().Be(futureIds.Count);
        updated.Schedule.Preserved.Should().Be(pastIds.Count);
        updated.Schedule.Created.Should().BeGreaterThan(0);

        var after = await ScheduleAsync(client, created.Group.Id);
        var afterIds = after.Select(s => s.Id).ToHashSet();

        // ---- 1) o'tgan darslar joyida
        afterIds.Should().Contain(pastIds, "o'tgan darslar SAQLANISHI shart");

        var ended = after.Single(s => s.Id == endedId);
        ended.Status.Should().Be(nameof(SessionStatus.Ended),
            "yakunlangan dars holati o'zgarmasligi kerak");

        // ---- 2) eski kelajak darslar o'chirilgan
        afterIds.Should().NotIntersectWith(futureIds,
            "kelajakdagi rejalashtirilgan darslar QAYTA yaratiladi");

        // ---- 3) yangi kelajak darslar YANGI kunlarda
        var newFuture = after.Where(s => s.ScheduledStart > now).ToList();
        newFuture.Should().NotBeEmpty();
        WeekdaysOf(newFuture).Should().Equal([DayOfWeek.Tuesday, DayOfWeek.Thursday]);

        // ---- 4) raqamlash saqlangan darslardan KEYIN davom etadi
        //         (1 dan boshlanmaydi — aks holda sarlavhalar takrorlanardi)
        newFuture[0].Title.Should().Be(
            "IT-kunlar — "
            + (pastIds.Count + 1).ToString(CultureInfo.InvariantCulture)
            + "-dars");

        // ---- 5) yangi darslar saqlangan dars vaqtiga to'qnashmasin
        after.Select(s => s.ScheduledStart).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Aynan shu qoida ATAYLAB qayta tuzishda ham amal qiladi
    /// (<c>POST /schedule/regenerate</c>).
    /// </summary>
    [Fact]
    public async Task RegenerateSchedule_ReplacesOnlyFutureScheduledSessions()
    {
        using var client = await AdminClientAsync();
        var teacherId = await CreateStaffAsync(client, UserRole.Teacher);

        var created = await CreateGroupAsync(
            client, Payload("IT-qayta", PastStart(DayOfWeek.Monday), teacherId));

        var before = await ScheduleAsync(client, created.Group.Id);
        var now = DateTimeOffset.UtcNow;
        var pastIds = before.Where(s => s.ScheduledStart <= now).Select(s => s.Id).ToList();

        var response = await client.PostAsync(
            new Uri($"/api/v1/groups/{created.Group.Id}/schedule/regenerate", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await response.Content.ReadFromJsonAsync<ScheduleSummaryResponse>();
        summary!.Regenerated.Should().BeTrue();
        summary.Preserved.Should().Be(pastIds.Count);

        var after = await ScheduleAsync(client, created.Group.Id);
        after.Select(s => s.Id).Should().Contain(pastIds);
    }

    // ================================================================= a'zolik

    /// <summary>
    /// Pauza muddati (<c>pausedUntil</c>) BAZAGA yozilib, qaytib o'qilishi
    /// kerak — u soya (shadow) ustunda saqlanadi, shuning uchun bu yo'l
    /// alohida qo'riqlanadi (aks holda klient yuborgan sana jimgina yo'qolardi).
    /// </summary>
    [Fact]
    public async Task PauseMember_PersistsPausedUntilAndResumeClearsIt()
    {
        using var client = await AdminClientAsync();
        var teacherId = await CreateStaffAsync(client, UserRole.Teacher);
        var studentId = await CreateStaffAsync(client, UserRole.Student);

        var created = await CreateGroupAsync(
            client, Payload("IT-azolik", FutureStart(DayOfWeek.Monday), teacherId));

        var add = await client.PostAsJsonAsync(
            $"/api/v1/groups/{created.Group.Id}/members", new { studentId });
        add.StatusCode.Should().Be(HttpStatusCode.Created);

        var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

        var pause = await client.PostAsJsonAsync(
            $"/api/v1/groups/{created.Group.Id}/members/{studentId}/pause",
            new { pausedUntil = until.ToString("O", CultureInfo.InvariantCulture) });

        pause.StatusCode.Should().Be(HttpStatusCode.OK);

        var paused = await pause.Content.ReadFromJsonAsync<MemberResponse>();
        paused!.Status.Should().Be(nameof(MemberStatus.Paused));
        paused.PausedUntil.Should().Be(until);

        // Ro'yxatdan qayta o'qishda ham saqlanib turishi kerak (bazadan).
        var members = await client.GetFromJsonAsync<List<MemberResponse>>(
            $"/api/v1/groups/{created.Group.Id}/members");

        members!.Single(m => m.StudentId == studentId).PausedUntil.Should().Be(until);

        var resume = await client.PostAsync(
            new Uri($"/api/v1/groups/{created.Group.Id}/members/{studentId}/resume",
                UriKind.Relative),
            content: null);

        var resumed = await resume.Content.ReadFromJsonAsync<MemberResponse>();
        resumed!.Status.Should().Be(nameof(MemberStatus.Active));
        resumed.PausedUntil.Should().BeNull("tiklanganda muddat tozalanadi");
    }

    // =============================================== a'zolar ro'yxati va KONTAKT

    /// <summary>
    /// ========================================================================
    /// 🔴 R27: <c>GET /groups/{id}/members</c> — USTOZGA KONTAKT BERMAYDI
    /// ========================================================================
    ///
    /// Bu endpoint — ustoz yeta oladigan IKKI proyeksiyadan biri (ikkinchisi
    /// <c>GET /users/{id}/profile</c>, u <c>UserProfileEndpointsTests</c> da
    /// qo'riqlanadi). Sinf darajasidagi
    /// <c>[Authorize(Roles="Teacher,Assistant,Academic,Admin")]</c> faqat
    /// DARVOZA: u "kira oladimi" degan savolga javob beradi, "nima ko'radi"
    /// degan savolga emas.
    ///
    /// Uchala rol BITTA testda tekshiriladi (ustoz / kurator / o'quv bo'limi):
    /// qoida — bu ULARNING FARQI, va farqni bitta joyda ko'rish uni keyinroq
    /// buzib qo'yishni qiyinlashtiradi.
    /// </summary>
    [Fact]
    public async Task Members_HideContactFromTeacherOnly()
    {
        var world = await WorldBuilder.CreateAsync(factory, "azo-kontakt");

        var (email, phone) = await ProfileWorldBuilder.ContactOfAsync(factory, world.Student.Id);
        phone.Should().NotBeNullOrEmpty("dunyo quruvchi o'quvchiga ham raqam beradi");

        var uri = $"/api/v1/groups/{world.GroupId}/members";

        // ---- USTOZ: kontakt YO'Q
        using (var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher))
        {
            using var raw = await teacher.GetAsync(new Uri(uri, UriKind.Relative));
            var json = await raw.Content.ReadAsStringAsync();

            raw.StatusCode.Should().Be(HttpStatusCode.OK, json);
            json.Should().NotContain(email, "email ustoz javobiga tushmasligi kerak");
            json.Should().NotContain(phone!, "telefon ustoz javobiga tushmasligi kerak");

            var members = await teacher.GetFromJsonAsync<List<MemberResponse>>(uri);
            var row = members!.Single(m => m.StudentId == world.Student.Id);

            row.Email.Should().BeNull();
            row.Phone.Should().BeNull();
            row.FullName.Should().NotBeNullOrEmpty("ism qoladi — jurnal ishlashi kerak");
        }

        // ---- KURATOR: kontakt BOR (qo'ng'iroq — uning asosiy amali)
        using (var curator = await WorldBuilder.ClientAsync(factory, world.Curator))
        {
            var members = await curator.GetFromJsonAsync<List<MemberResponse>>(uri);
            var row = members!.Single(m => m.StudentId == world.Student.Id);

            row.Email.Should().Be(email);
            row.Phone.Should().Be(phone);
        }

        // ---- O'QUV BO'LIMI: hammasi ochiq
        using (var admin = await AdminClientAsync())
        {
            var members = await admin.GetFromJsonAsync<List<MemberResponse>>(uri);
            var row = members!.Single(m => m.StudentId == world.Student.Id);

            row.Email.Should().Be(email);
            row.Phone.Should().Be(phone);
        }
    }

    /// <summary>
    /// ★ KURATOR guruhida o'quvchilar BEVOSITA a'zo bo'lmaydi — ular
    /// <c>curatorGroupId</c> havolasi orqali bog'langan ustoz guruhlaridan
    /// keladi. Eski tizimda bu havola hisobga olinmagani uchun kurator
    /// darsida ro'yxat BO'SH chiqardi (B-8a).
    /// </summary>
    [Fact]
    public async Task CuratorGroup_ResolvesMembersThroughLinkedTeacherGroups()
    {
        using var client = await AdminClientAsync();

        var assistantId = await CreateStaffAsync(client, UserRole.Assistant);
        var teacherId = await CreateStaffAsync(client, UserRole.Teacher);
        var studentId = await CreateStaffAsync(client, UserRole.Student);

        var startDate = FutureStart(DayOfWeek.Monday);

        // Kurator guruhi: haftada 3 kun (oddiy guruh uchun bu taqiqlangan,
        // kurator uchun esa normal — eski tizimda bu farq yo'q edi).
        var curator = await CreateGroupAsync(client, new
        {
            name = "IT-kurator",
            startDate = startDate.ToString("O", CultureInfo.InvariantCulture),
            weekdays = new[] { "Monday", "Wednesday", "Friday" },
            startTime = StartTime,
            type = nameof(GroupType.Curator),
            durationMinutes = DurationMinutes,
            courseMonths = CourseMonths,
            assistantId,
            isActive = true,
        });

        // Kurator guruhining darslarini KURATOR o'tadi (ustoz emas).
        var curatorSchedule = await ScheduleAsync(client, curator.Group.Id);
        curatorSchedule.Should().AllSatisfy(s => s.HostId.Should().Be(assistantId));
        curatorSchedule[0].Title.Should().Be("IT-kurator — 1-yordamchi dars");

        // Kurator guruhiga o'quvchi TO'G'RIDAN-TO'G'RI qo'shilmaydi.
        var direct = await client.PostAsJsonAsync(
            $"/api/v1/groups/{curator.Group.Id}/members", new { studentId });
        direct.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Ustoz guruhi shu kuratorga bog'lanadi.
        var candidatesUri = $"/api/v1/groups/{curator.Group.Id}/curator-candidates";
        var forCurator = await client.GetFromJsonAsync<List<CandidateResponse>>(candidatesUri);
        forCurator.Should().BeEmpty("kurator guruhi boshqa kuratorga bog'lanmaydi");

        var teacherGroup = await CreateGroupAsync(client, new
        {
            name = "IT-bogliq",
            startDate = startDate.ToString("O", CultureInfo.InvariantCulture),
            weekdays = MondayWednesday,
            startTime = StartTime,
            type = nameof(GroupType.Group),
            durationMinutes = DurationMinutes,
            courseMonths = CourseMonths,
            teacherId,
            curatorGroupId = curator.Group.Id,
            isActive = true,
        });

        teacherGroup.Group.CuratorGroupId.Should().Be(curator.Group.Id);
        teacherGroup.Group.CuratorGroupName.Should().Be("IT-kurator");

        var candidates = await client.GetFromJsonAsync<List<CandidateResponse>>(
            $"/api/v1/groups/{teacherGroup.Group.Id}/curator-candidates");

        candidates.Should().Contain(c => c.Id == curator.Group.Id);

        var add = await client.PostAsJsonAsync(
            $"/api/v1/groups/{teacherGroup.Group.Id}/members", new { studentId });
        add.StatusCode.Should().Be(HttpStatusCode.Created);

        // ---- ★ O'quvchi KURATOR guruhi ro'yxatida ham ko'rinishi kerak
        var curatorMembers = await client.GetFromJsonAsync<List<MemberResponse>>(
            $"/api/v1/groups/{curator.Group.Id}/members");

        var resolved = curatorMembers!.Single(m => m.StudentId == studentId);
        resolved.SourceGroupId.Should().Be(teacherGroup.Group.Id,
            "kurator o'quvchi QAYSI ustoz guruhidan kelganini ko'rishi kerak");
        resolved.SourceGroupName.Should().Be("IT-bogliq");

        var curatorCard = await client.GetFromJsonAsync<GroupResponse>(
            $"/api/v1/groups/{curator.Group.Id}");

        curatorCard!.MemberCount.Should().Be(1,
            "kurator guruhining a'zolar soni havola orqali hisoblanadi");
    }

    // ================================================================= ruxsat

    [Fact]
    public async Task Create_AsTeacher_ReturnsForbidden()
    {
        using var admin = await AdminClientAsync();
        var (email, password) = await CreateStaffWithLoginAsync(admin, UserRole.Teacher);

        var tokens = await factory.LoginAsync(email);
        using var teacherClient = factory.CreateAuthorizedClient(tokens.AccessToken);

        var response = await teacherClient.PostAsJsonAsync("/api/v1/groups",
            Payload("IT-ruxsat", FutureStart(DayOfWeek.Monday), teacherId: null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "ustoz guruh yarata olmaydi");
    }

    /// <summary>
    /// Ustoz uchun ro'yxat AVTOMATIK o'z guruhlariga cheklanadi — alohida
    /// `/groups/mine` endpointi shu sabab kerak emas.
    /// </summary>
    [Fact]
    public async Task List_AsTeacher_ReturnsOnlyOwnGroups()
    {
        using var admin = await AdminClientAsync();

        var (email, password) = await CreateStaffWithLoginAsync(admin, UserRole.Teacher);
        var ownerId = await factory.WithDbAsync(db =>
            db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync());

        var strangerId = await CreateStaffAsync(admin, UserRole.Teacher);

        var startDate = FutureStart(DayOfWeek.Monday);
        var mine = await CreateGroupAsync(admin, Payload("IT-mening", startDate, ownerId));
        var theirs = await CreateGroupAsync(admin, Payload("IT-begona", startDate, strangerId));

        var tokens = await factory.LoginAsync(email);
        using var teacherClient = factory.CreateAuthorizedClient(tokens.AccessToken);

        var page = await teacherClient.GetFromJsonAsync<PagedGroups>("/api/v1/groups?pageSize=100");

        page!.Items.Select(g => g.Id).Should().Contain(mine.Group.Id);
        page.Items.Select(g => g.Id).Should().NotContain(theirs.Group.Id);

        var forbidden = await teacherClient.GetAsync(
            new Uri($"/api/v1/groups/{theirs.Group.Id}", UriKind.Relative));

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Oddiy guruh haftada ANIQ 2 kun — Domain qoidasi buzilsa 409 qaytadi
    /// (Domain istisnosi HTTP holatiga to'g'ri xaritalanganini ham tekshiradi).
    /// </summary>
    [Fact]
    public async Task Create_ForPlainGroupWithThreeWeekdays_ReturnsConflict()
    {
        using var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/groups", new
        {
            name = "IT-xato",
            startDate = FutureStart(DayOfWeek.Monday).ToString("O", CultureInfo.InvariantCulture),
            weekdays = new[] { "Monday", "Wednesday", "Friday" },
            startTime = StartTime,
            type = nameof(GroupType.Group),
            durationMinutes = DurationMinutes,
            courseMonths = CourseMonths,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ================================================================= R22: qidiruv

    /// <summary>
    /// ★★ R22 — QIDIRUV "BARCHA PARAMETRLAR" BO'YICHA.
    ///
    /// Talab: *"guruhlar bo'limida qidiruv barcha parametrlar bo'yicha
    /// ishlasin"*. Ilgari server FAQAT <c>Groups.Name</c> ni qarardi, ya'ni
    /// "ustozim kim edi?" degan savolga jadval javob bera olmasdi.
    ///
    /// ★ HAR BELGI BOSHQA-BOSHQA: guruh nomi, ustoz ismi va kurator ismi
    /// uchun UCHTA MUSTAQIL tasodifiy satr ishlatiladi. Bitta umumiy belgi
    /// bo'lsa test "ustoz bo'yicha topildi" deb yozib, aslida nom bo'yicha
    /// topilganini yashirardi — ya'ni yashil bo'lib turgan holda hech
    /// narsani isbotlamasdi.
    ///
    /// ★ KURS NOMI shu yerda alohida tekshirilmaydi: u kod shaklida kurator
    /// guruhi nomi bilan AYNAN bir xil tarmoq (nullable navigatsiya +
    /// <c>LIKE</c>), kurs yaratish esa testga aloqasiz uchta so'rov
    /// qo'shardi.
    /// </summary>
    [Fact]
    public async Task List_Search_MatchesTeacherAndCuratorNamesNotOnlyGroupName()
    {
        using var client = await AdminClientAsync();

        var groupMark = SearchMarker();
        var teacherMark = SearchMarker();
        var assistantMark = SearchMarker();
        var curatorGroupMark = SearchMarker();

        var teacherId = await CreateNamedStaffAsync(client, UserRole.Teacher, "Ustoz " + teacherMark);
        var assistantId = await CreateNamedStaffAsync(
            client, UserRole.Assistant, "Kurator " + assistantMark);

        var startDate = FutureStart(DayOfWeek.Monday);

        var curatorGroup = await CreateGroupAsync(client, new
        {
            name = "K-" + curatorGroupMark,
            startDate = startDate.ToString("O", CultureInfo.InvariantCulture),
            weekdays = new[] { "Monday", "Wednesday", "Friday" },
            startTime = StartTime,
            type = nameof(GroupType.Curator),
            durationMinutes = DurationMinutes,
            courseMonths = CourseMonths,
            assistantId,
            isActive = true,
        });

        var group = await CreateGroupAsync(client, new
        {
            name = "IT-" + groupMark,
            startDate = startDate.ToString("O", CultureInfo.InvariantCulture),
            weekdays = MondayWednesday,
            startTime = StartTime,
            type = nameof(GroupType.Group),
            durationMinutes = DurationMinutes,
            courseMonths = CourseMonths,
            teacherId,
            assistantId,
            curatorGroupId = curatorGroup.Group.Id,
            isActive = true,
        });

        var id = group.Group.Id;

        (await SearchGroupIdsAsync(client, groupMark))
            .Should().Contain(id, "guruh nomi bo'yicha qidiruv AVVALGIDEK ishlashi kerak");

        (await SearchGroupIdsAsync(client, teacherMark))
            .Should().Contain(id, "R22: ustoz ismi bo'yicha");

        (await SearchGroupIdsAsync(client, assistantMark))
            .Should().Contain(id, "R22: kurator ismi bo'yicha");

        (await SearchGroupIdsAsync(client, curatorGroupMark))
            .Should().Contain(id, "R22: biriktirilgan kurator guruhi nomi bo'yicha");

        (await SearchGroupIdsAsync(client, SearchMarker()))
            .Should().NotContain(id, "hech qayerda uchramaydigan satr hech nima topmasin");
    }

    /// <summary>
    /// ⚠️ Minimal uzunlik SAQLANDI (2 belgi) — qamrov kengaygani bilan
    /// shartnoma o'zgarmadi. Frontend qo'riqchisi (`TeacherGroupsPage`,
    /// `ManageGroupsPage`) aynan shu 400 ga tayanadi.
    /// </summary>
    [Fact]
    public async Task List_SearchShorterThanMinimum_ReturnsBadRequest()
    {
        using var client = await AdminClientAsync();

        var response = await client.GetAsync(new Uri("/api/v1/groups?search=a", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ================================================================= yordamchi

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    /// <summary>
    /// Boshqa testlar bilan TO'QNASHMAYDIGAN qidiruv satri.
    ///
    /// Baza testlar orasida BO'LISHILADI, ya'ni "Ustoz" kabi haqiqiy so'z
    /// begona qatorlarni ham tortib kelardi va `NotContain` tekshiruvi
    /// tasodifan yiqilardi. Harflar bilan boshlanadi — raqamli satr
    /// telefon/Id qidiruvi bilan chalkashmasin.
    /// </summary>
    private static string SearchMarker() => "qq" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<List<long>> SearchGroupIdsAsync(HttpClient client, string term)
    {
        var page = await client.GetFromJsonAsync<PagedGroups>(
            $"/api/v1/groups?pageSize=100&search={Uri.EscapeDataString(term)}");

        page.Should().NotBeNull();
        return [.. page!.Items.Select(g => g.Id)];
    }

    /// <summary>ANIQ ism bilan xodim — qidiruv testi shunga tayanadi.</summary>
    private static async Task<long> CreateNamedStaffAsync(
        HttpClient client, UserRole role, string fullName)
    {
        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            fullName,
            email = $"ig-{Guid.NewGuid():N}"[..16] + "@zinnur.uz",
            role = role.ToString(),
            phone = TestPhones.Next(),
        });

        await EnsureStatusAsync(response, HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreatedUserResponse>();
        return created!.User.Id;
    }

    /// <summary>Barcha darslari KELAJAKDA bo'ladigan boshlanish sanasi.</summary>
    private static DateOnly FutureStart(DayOfWeek weekday) =>
        NextWeekdayOnOrAfter(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7), weekday);

    /// <summary>Ikki oy oldin boshlangan kurs — jadvalda o'tgan darslar bo'ladi.</summary>
    private static DateOnly PastStart(DayOfWeek weekday) =>
        NextWeekdayOnOrAfter(DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-2), weekday);

    private static DateOnly NextWeekdayOnOrAfter(DateOnly from, DayOfWeek weekday)
    {
        while (from.DayOfWeek != weekday)
            from = from.AddDays(1);

        return from;
    }

    /// <summary>Standart jadval qoidasi bilan so'rov tanasi.</summary>
    private static object Payload(string name, DateOnly startDate, long? teacherId) => new
    {
        name,
        startDate = startDate.ToString("O", CultureInfo.InvariantCulture),
        weekdays = MondayWednesday,
        startTime = StartTime,
        type = nameof(GroupType.Group),
        durationMinutes = DurationMinutes,
        courseMonths = CourseMonths,
        teacherId,
        isActive = true,
    };

    private static async Task<CreateGroupResponse> CreateGroupAsync(HttpClient client, object payload)
    {
        var response = await client.PostAsJsonAsync("/api/v1/groups", payload);
        await EnsureStatusAsync(response, HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<CreateGroupResponse>())!;
    }

    private static async Task<UpdateGroupResponse> UpdateGroupAsync(
        HttpClient client, long id, object payload)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/groups/{id}", payload);
        await EnsureStatusAsync(response, HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<UpdateGroupResponse>())!;
    }

    /// <summary>
    /// Holatni tekshiradi va xato bo'lsa JAVOB TANASINI ko'rsatadi.
    ///
    /// Nima uchun alohida metod: tanani FluentAssertions ning `because`
    /// argumentiga berish MUMKIN EMAS — u satrni `string.Format` bilan
    /// qayta ishlaydi va JSON ichidagi `{` `}` belgilari testni assert
    /// o'rniga formatlash xatosi bilan yiqitadi.
    /// </summary>
    private static async Task EnsureStatusAsync(
        HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected) return;

        var body = await response.Content.ReadAsStringAsync();

        Assert.Fail(
            "Kutilgan holat " + expected.ToString()
            + ", olingan " + response.StatusCode.ToString()
            + ". Javob tanasi: " + body);
    }

    private static async Task<List<SessionResponse>> ScheduleAsync(HttpClient client, long groupId)
    {
        var schedule = await client.GetFromJsonAsync<List<SessionResponse>>(
            $"/api/v1/groups/{groupId}/schedule");

        schedule.Should().NotBeNull();
        return schedule!;
    }

    /// <summary>Dars kunlari — MAHALLIY zonada (UTC kuni chegarada siljishi mumkin).</summary>
    private static List<DayOfWeek> WeekdaysOf(IEnumerable<SessionResponse> sessions)
    {
        var tashkent = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");

        return [.. sessions
            .Select(s => TimeZoneInfo.ConvertTime(s.ScheduledStart, tashkent).DayOfWeek)
            .Distinct()
            .Order()];
    }

    private static async Task<long> CreateStaffAsync(HttpClient client, UserRole role) =>
        (await CreateUserAsync(client, role)).Id;

    private static async Task<(string Email, string Password)> CreateStaffWithLoginAsync(
        HttpClient client, UserRole role)
    {
        var created = await CreateUserAsync(client, role);
        return (created.Email, created.Password);
    }

    private static async Task<(long Id, string Email, string Password)> CreateUserAsync(
        HttpClient client, UserRole role)
    {
        var email = $"ig-{Guid.NewGuid():N}"[..16] + "@zinnur.uz";
        const string password = "Guruh!2345";

        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            fullName = "Test " + role.ToString(),
            email,
            role = role.ToString(),

            // 🔴 Xodim uchun telefon MAJBURIY (2026-08-13) — izoh `TestPhones` da.
            phone = TestPhones.Next(),
        });

        await EnsureStatusAsync(response, HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreatedUserResponse>();
        return (created!.User.Id, email, password);
    }

    private Task<long> FirstCourseIdAsync() =>
        factory.WithDbAsync(db => db.Courses.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync());

    /// <summary>
    /// Darsni YAKUNLANGAN qiladi — u davomat va chat tarixiga ega dars
    /// vazifasini bajaradi (qayta tuzishda saqlanishi shart).
    /// </summary>
    private Task<int> MarkEndedAsync(long sessionId) =>
        factory.WithDbAsync(async db =>
        {
            var session = await db.LiveSessions.FirstAsync(s => s.Id == sessionId);

            session.Status = SessionStatus.Ended;
            session.ActualStart = session.ScheduledStart;
            session.ActualEnd = session.ScheduledEnd;

            return await db.SaveChangesAsync();
        });

    // ---------------------------------------------------------------- javob shakllari

    private sealed record CreateGroupResponse(GroupResponse Group, int SessionsCreated);

    private sealed record UpdateGroupResponse(GroupResponse Group, ScheduleSummaryResponse Schedule);

    private sealed record GroupResponse(
        long Id,
        string Name,
        string Type,
        long? CourseId,
        long? TeacherId,
        long? AssistantId,
        long? CuratorGroupId,
        string? CuratorGroupName,
        DateOnly StartDate,
        DateOnly EndDate,
        List<string> Weekdays,
        int MemberCount,
        int SessionCount);

    private sealed record ScheduleSummaryResponse(
        bool ScheduleTouched,
        bool Regenerated,
        int Created,
        int Deleted,
        int Preserved,
        int HostsUpdated,
        int TitlesUpdated,
        string Reason);

    private sealed record SessionResponse(
        long Id,
        string? Title,
        string Type,
        string Status,
        DateTimeOffset ScheduledStart,
        DateTimeOffset ScheduledEnd,
        long? HostId,
        string RoomName);

    /// <summary>
    /// ★ <c>Email</c>/<c>Phone</c> — <c>string?</c>: ustoz javobida ikkalasi
    /// ham <c>null</c> (talab R27). Email bazada MAJBURIY, ya'ni bo'shlik
    /// faqat serverning kesganidan darak beradi.
    /// </summary>
    private sealed record MemberResponse(
        long Id,
        long StudentId,
        string FullName,
        string? Email,
        string? Phone,
        string Status,
        DateOnly? PausedUntil,
        long SourceGroupId,
        string SourceGroupName);

    private sealed record CandidateResponse(long Id, string Name, long? AssistantId);

    private sealed record PagedGroups(List<GroupResponse> Items, int Page, int Total);

    private sealed record CreatedUserResponse(UserRef User);

    private sealed record UserRef(long Id);
}
