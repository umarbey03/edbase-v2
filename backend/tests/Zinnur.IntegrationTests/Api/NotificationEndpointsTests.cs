using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Persistence;
using Zinnur.IntegrationTests.Infrastructure;
using Zinnur.WebApi.Hubs;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// R35 / R36 — BILDIRISHNOMA: BAHOLASHDAN EKRANGACHA
/// ========================================================================
///
/// Loyiha egasi: *"vazifa tekshirilgan avtomatik studentda ham yangilanish
/// kerak, page refresh kerak bo'lmasin, va notification kelsin"*.
///
/// Shu bitta gapda UCHTA mustaqil va'da bor va ular UCHTA yo'l bilan
/// bajariladi. Bu yerda uchalasi ham sinaladi:
///
///   1) QO'NG'IROQCHA — bazadagi qator + REST ro'yxati ("notification");
///   2) REALTIME — SignalR hodisasi ("page refresh kerak bo'lmasin");
///   3) TELEGRAM — navbat yozuvi (ilova ochiq bo'lmasa ham xabar boradi).
///
/// ★ Ular ATAYLAB bir-biriga bog'liq emas: Telegram sozlanmagan bo'lsa ham
///   qo'ng'iroqcha ishlaydi, hub yiqilsa ham Telegram xabari ketadi.
///   Testlar shu mustaqillikni ham qulflaydi.
/// </summary>
public sealed class NotificationEndpointsTests(NotificationApiFactory factory)
    : IClassFixture<NotificationApiFactory>
{
    // ================================================================== 1) baholash -> uch yo'l

    /// <summary>
    /// 🔴 ASOSIY TEST: ustoz baholaydi — o'quvchida bildirishnoma paydo
    /// bo'ladi.
    ///
    /// Bugungacha bu YO'Q edi: butun outbox infratuzilmasi qurilgan bo'lsa
    /// ham, YAGONA chaqiruvchi Telegram botining javobi edi. Ya'ni birorta
    /// biznes hodisasi bildirishnoma yaratmasdi.
    /// </summary>
    [Fact]
    public async Task Grade_CreatesInAppNotificationForStudent()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-a");

        await world.GradeAsync(4.5m, "Yaxshi, oxirgi savol chala.");

        using var student = await world.StudentClientAsync();

        var page = await student.GetFromJsonAsync<NotificationPageResponse>(
            new Uri("/api/v1/notifications", UriKind.Relative));

        var row = page!.Items.Should().ContainSingle().Subject;

        row.Kind.Should().Be(nameof(NotificationKind.SubmissionGraded));
        row.Read.Should().BeFalse();
        row.EntityId.Should().Be(world.SubmissionId, "bosilganda AYNAN shu javobga o'tiladi");
        row.Body.Should().Contain("4.5/5");
        row.Body.Should().Contain("Yaxshi, oxirgi savol chala.");

        page.UnreadCount.Should().Be(1);
    }

    /// <summary>
    /// 🔴 COMMIT-THEN-SEND: hodisa tarqatilgan PAYTDA qator BAZADA bo'lishi
    /// shart va uning `Id` si HAQIQIY bo'lishi kerak.
    ///
    /// Teskarisi jimgina buziladi: tranzaksiya orqaga qaytsa o'quvchining
    /// ekranida BAZADA YO'Q baho paydo bo'lardi va u sahifani
    /// yangilamaguncha shunday turardi. Bu tartib shu test bilan
    /// QULFLANADI — kimdir `notifier` chaqiruvini `SaveChanges` dan
    /// yuqoriga ko'chirsa test yiqiladi.
    /// </summary>
    [Fact]
    public async Task Grade_BroadcastsAfterCommit_WithPersistedRow()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-b");

        factory.Hub.Clear();

        await world.GradeAsync(5m, null);

        var broadcast = factory.Hub.Take().Should().ContainSingle().Subject;

        broadcast.Method.Should().Be("NotificationCreated");
        broadcast.Notification.Id.Should().BePositive("tarqatilayotgan qatorda haqiqiy Id bo'lishi kerak");
        broadcast.ExistsInDatabase.Should().BeTrue(
            "bildirishnoma tarqatilgan PAYTDA bazada bo'lishi shart (commit-then-send)");
    }

    /// <summary>
    /// 🔴 XABAR AYNAN O'QUVCHIGA KETADI — `Clients.User`, `Clients.All` emas.
    ///
    /// Bu bo'lmasa har bir baho BUTUN o'quv markazining ekranida chiqardi:
    /// begona o'quvchining bahosi va ustozning izohi hammaga ko'rinardi.
    /// Soxta hub kontekstida `All`/`Group` yo'llari ATAYLAB istisno
    /// ko'taradi — ya'ni kimdir yo'nalishni kengaytirsa test darhol
    /// qizaradi, jimgina o'tib ketmaydi.
    /// </summary>
    [Fact]
    public async Task Grade_BroadcastsToTheStudentOnly()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-c");

        factory.Hub.Clear();

        await world.GradeAsync(3m, null);

        factory.Hub.Take().Should().ContainSingle().Which
            .UserId.Should().Be(
                world.StudentId.ToString(CultureInfo.InvariantCulture),
                "SignalR ulanish egasini `ClaimTypes.NameIdentifier` dan oladi "
                + "(mexanizm `NotificationUserIdTests` da alohida qulflangan)");
    }

    /// <summary>
    /// 🔴 TARQATISH YIQILSA BAHOLASH YIQILMAYDI.
    ///
    /// Bu `GroupChatNotifier` dagidan ham muhimroq: bu yerda 500 qaytsa
    /// ustoz "saqlanmadi" deb o'ylab QAYTA baholardi — ya'ni transport
    /// nosozligi ustozning ishini ikkilantirardi. Baho esa BAZADA
    /// allaqachon bor.
    /// </summary>
    [Fact]
    public async Task Grade_WhenBroadcastFails_StillSucceedsAndPersists()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-d");

        factory.Hub.Clear();
        factory.Hub.FailBroadcast = true;
        try
        {
            var response = await world.GradeResponseAsync(5m, null);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            factory.Hub.FailBroadcast = false;
        }

        (await world.NotificationCountAsync()).Should().Be(1,
            "hub yiqilsa ham qator bazada qolishi kerak");
    }

    // ================================================================== 2) Telegram navbati

    /// <summary>
    /// ★ TELEGRAM NAVBATIGA yozuv tushadi va u `submission_graded` kaliti
    /// bilan guruhlanadi.
    ///
    /// Matn TAYYOR holda saqlanadi (shablon kaliti + parametr JSON emas) —
    /// sabab `NotificationRequest` izohida: yozib olingan matn DALIL bo'ladi
    /// va shablon keyin o'zgarsa navbatdagi eski xabar qayta yasalib
    /// ketmaydi.
    /// </summary>
    [Fact]
    public async Task Grade_EnqueuesTelegramMessage_ForLinkedStudent()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-e", telegramId: 900_001);

        await world.GradeAsync(4m, "Rahmat");

        var queued = await world.OutboxAsync();

        var row = queued.Should().ContainSingle().Subject;

        row.TemplateKey.Should().Be(TelegramTemplates.SubmissionGraded);
        row.RecipientUserId.Should().Be(world.StudentId);
        row.RecipientAddress.Should().Be("900001", "Telegram uchun manzil — `chat_id`");
        row.Body.Should().Contain("<b>4</b> / 5");
        row.Body.Should().Contain("Rahmat");
        row.Body.Length.Should().BeLessThan(NotificationText.MaxBodyLength);
    }

    /// <summary>
    /// ★ BOG'LANMAGAN O'QUVCHIGA NAVBATGA YOZILMAYDI — LEKIN QO'NG'IROQCHA
    /// BARIBIR ISHLAYDI.
    ///
    /// `chat_id` yo'q qator faqat urinishlar chegarasini yeb, `Failed`
    /// bo'lardi va navbatni bekorga band qilardi. Ikki yo'lning
    /// mustaqilligi ham AYNAN shu yerda ko'rinadi.
    /// </summary>
    [Fact]
    public async Task Grade_WithoutTelegramLink_SkipsQueueButStillNotifiesInApp()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-f", telegramId: null);

        await world.GradeAsync(5m, null);

        (await world.OutboxAsync()).Should().BeEmpty();
        (await world.NotificationCountAsync()).Should().Be(1);
    }

    /// <summary>
    /// 🔴 ENG NOZIK QAROR: TAKRORGA QARSHI KALITDA URINISH RAQAMI BOR.
    ///
    /// AYNI urinish ichida bahoni tuzatish — TAKROR xabar yubormaydi
    /// (Telegram turtki kanal, bir vazifa uchun uch marta "tekshirildi"
    /// deyish spam bo'lardi).
    ///
    /// LEKIN qayta ochilgan va qayta topshirilgan javob YANGI hodisa va u
    /// YANGI xabar olishi SHART. Kalitda urinish raqami bo'lmasa
    /// (`submission_graded:{id}`) ikkinchi baho JIMGINA rad etilardi —
    /// "himoya ishladi" emas, MA'LUMOT YO'QOLISHI bo'lardi.
    /// </summary>
    [Fact]
    public async Task Grade_SameAttemptTwice_EnqueuesOnce_ButNewAttemptEnqueuesAgain()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-g", telegramId: 900_002);

        await world.GradeAsync(3m, "Birinchi");
        await world.GradeAsync(4m, "Tuzatilgan baho");

        (await world.OutboxAsync()).Should().HaveCount(1,
            "ayni urinish ichidagi tuzatish Telegram'ga TAKROR xabar yubormaydi");

        // Qayta ochish + qayta topshirish => `AttemptNumber` 2 ga o'sadi.
        await world.ReopenAsync("Qayta ishlang");
        await world.ResubmitAsync("Ikkinchi urinish");
        await world.GradeAsync(5m, "Endi zo'r");

        var queued = await world.OutboxAsync();

        queued.Should().HaveCount(2, "qayta baholangan YANGI urinish — yangi hodisa");
        queued.Select(m => m.IdempotencyKey).Should().OnlyHaveUniqueItems();
        queued.Should().Contain(m => m.IdempotencyKey.EndsWith(":1", StringComparison.Ordinal));
        queued.Should().Contain(m => m.IdempotencyKey.EndsWith(":2", StringComparison.Ordinal));
    }

    /// <summary>
    /// ★ QO'NG'IROQCHADA BIR JAVOB UCHUN BITTA O'QILMAGAN QATOR.
    ///
    /// Ustoz bahoni tuzatishi odatiy hol. Har tuzatish yangi qator yozsa,
    /// o'quvchining ro'yxatida bir vazifa uchun uch-to'rt bir xil yozuv
    /// turardi va qaysi biri OXIRGI ekani ko'rinmasdi. Shuning uchun eski
    /// O'QILMAGAN qator o'chiriladi va o'rniga yangisi qo'yiladi.
    ///
    /// ★ TELEGRAM YO'LI BILAN FARQI ATAYLAB: u yerda takror xabar
    ///   YUBORILMAYDI, bu yerda esa qator YANGILANADI. Sabab — Telegram
    ///   turtki kanal, qo'ng'iroqcha esa passiv ro'yxat.
    /// </summary>
    [Fact]
    public async Task Grade_SameAttemptTwice_ReplacesUnreadNotification()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-h");

        await world.GradeAsync(3m, "Birinchi");
        await world.GradeAsync(4m, "Tuzatilgan");

        using var student = await world.StudentClientAsync();

        var page = await student.GetFromJsonAsync<NotificationPageResponse>(
            new Uri("/api/v1/notifications", UriKind.Relative));

        var row = page!.Items.Should().ContainSingle().Subject;

        row.Body.Should().Contain("Tuzatilgan", "ro'yxatda OXIRGI holat turishi kerak");
        row.Body.Should().NotContain("Birinchi");
        page.UnreadCount.Should().Be(1);
    }

    /// <summary>
    /// ★ O'QILGAN QATOR TARIX — u o'chirilmaydi.
    ///
    /// Aks holda o'quvchi "menga xabar kelgan edi" desa, bazada isbot
    /// qolmasdi. Ya'ni tuzatish YANGI qator qo'shadi va eskisi joyida
    /// qoladi.
    /// </summary>
    [Fact]
    public async Task Grade_AfterStudentRead_KeepsHistoryAndAddsNewRow()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-i");

        await world.GradeAsync(3m, "Birinchi");

        using var student = await world.StudentClientAsync();

        await MarkReadAsync(student, ids: null);

        await world.GradeAsync(5m, "Tuzatilgan");

        var page = await student.GetFromJsonAsync<NotificationPageResponse>(
            new Uri("/api/v1/notifications", UriKind.Relative));

        page!.Items.Should().HaveCount(2, "o'qilgan qator TARIX sifatida qoladi");
        page.UnreadCount.Should().Be(1);
        page.Items[0].Body.Should().Contain("Tuzatilgan", "yangisi TEPADA turadi");
    }

    // ================================================================== 3) ro'yxat endpointi

    /// <summary>
    /// ★ KURSORLI SAHIFALASH: `beforeId` bilan keyingi sahifa ustma-ust
    /// tushmaydi va qator tushib qolmaydi.
    ///
    /// Ofsetli sahifalashda ustoz 50 ta ishni ketma-ket baholayotganda
    /// oyna surilib, ko'rilgan qator qayta chiqardi.
    /// </summary>
    [Fact]
    public async Task List_WithCursor_PagesWithoutOverlapOrGaps()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-j");
        await world.SeedNotificationsAsync(count: 5);

        using var student = await world.StudentClientAsync();

        var first = await student.GetFromJsonAsync<NotificationPageResponse>(
            new Uri("/api/v1/notifications?take=2", UriKind.Relative));

        first!.Items.Should().HaveCount(2);
        first.HasMore.Should().BeTrue();
        first.NextBeforeId.Should().Be(first.Items[^1].Id);

        var second = await student.GetFromJsonAsync<NotificationPageResponse>(
            new Uri(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"/api/v1/notifications?take=2&beforeId={first.NextBeforeId}"),
                UriKind.Relative));

        second!.Items.Should().HaveCount(2);
        second.Items.Select(i => i.Id).Should().NotIntersectWith(first.Items.Select(i => i.Id));
        second.Items[0].Id.Should().BeLessThan(first.Items[^1].Id, "tartib YANGIDAN ESKIGA");

        var last = await student.GetFromJsonAsync<NotificationPageResponse>(
            new Uri(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"/api/v1/notifications?take=2&beforeId={second.NextBeforeId}"),
                UriKind.Relative));

        last!.Items.Should().HaveCount(1);
        last.HasMore.Should().BeFalse();
        last.NextBeforeId.Should().BeNull();
    }

    /// <summary>
    /// ★ SANOQ SAHIFAGA BOG'LIQ EMAS: `UnreadCount` HAR sahifada UMUMIY
    /// o'qilmaganlar sonini beradi. Aks holda qo'ng'iroqcha nishoni
    /// sahifalash bilan o'zgarib turardi.
    /// </summary>
    [Fact]
    public async Task List_UnreadCount_IsGlobalNotPageLocal()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-k");
        await world.SeedNotificationsAsync(count: 5);

        using var student = await world.StudentClientAsync();

        var page = await student.GetFromJsonAsync<NotificationPageResponse>(
            new Uri("/api/v1/notifications?take=2", UriKind.Relative));

        page!.Items.Should().HaveCount(2);
        page.UnreadCount.Should().Be(5);
    }

    [Fact]
    public async Task List_UnreadOnly_FiltersReadRows()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-l");
        var ids = await world.SeedNotificationsAsync(count: 3);

        using var student = await world.StudentClientAsync();

        await MarkReadAsync(student, [ids[0]]);

        var page = await student.GetFromJsonAsync<NotificationPageResponse>(
            new Uri("/api/v1/notifications?unreadOnly=true", UriKind.Relative));

        page!.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(i => !i.Read);
    }

    /// <summary>
    /// 🔴 BEGONA BILDIRISHNOMA KO'RINMAYDI. Ro'yxat HAR DOIM tokendagi
    /// `userId` bo'yicha filtrlanadi — so'rovdan kelgan hech qanday
    /// parametr uni kengaytira olmaydi.
    /// </summary>
    [Fact]
    public async Task List_DoesNotLeakOtherUsersNotifications()
    {
        var mine = await NotificationWorld.CreateAsync(factory, "ntf-m");
        var other = await NotificationWorld.CreateAsync(factory, "ntf-n");

        await other.SeedNotificationsAsync(count: 3);

        using var student = await mine.StudentClientAsync();

        var page = await student.GetFromJsonAsync<NotificationPageResponse>(
            new Uri("/api/v1/notifications", UriKind.Relative));

        page!.Items.Should().BeEmpty();
        page.UnreadCount.Should().Be(0);
    }

    // ================================================================== 4) "o'qildi"

    [Fact]
    public async Task UnreadCount_ReflectsMarkRead()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-o");
        var ids = await world.SeedNotificationsAsync(count: 3);

        using var student = await world.StudentClientAsync();

        (await UnreadAsync(student)).Should().Be(3);

        var result = await MarkReadAsync(student, [ids[0], ids[1]]);

        result.MarkedCount.Should().Be(2);
        result.UnreadCount.Should().Be(1);

        (await UnreadAsync(student)).Should().Be(1);
    }

    /// <summary>
    /// ★ TANASIZ so'rov = "HAMMASINI o'qildi qil". Alohida `/read-all`
    /// endpointi qo'shilsa AYNI mantiq ikki joyda yozilardi.
    /// </summary>
    [Fact]
    public async Task MarkRead_WithoutIds_MarksEverything()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-p");
        await world.SeedNotificationsAsync(count: 4);

        using var student = await world.StudentClientAsync();

        (await MarkReadAsync(student, ids: null)).MarkedCount.Should().Be(4);
        (await UnreadAsync(student)).Should().Be(0);
    }

    /// <summary>
    /// ★ IDEMPOTENT: takroriy so'rov `0` qaytaradi va o'qish vaqtini QAYTA
    /// YOZMAYDI. Klient bir necha so'rovni parallel yuborishi mumkin
    /// (ekran ochildi + hub hodisasi keldi).
    /// </summary>
    [Fact]
    public async Task MarkRead_Twice_IsIdempotent()
    {
        var world = await NotificationWorld.CreateAsync(factory, "ntf-q");
        await world.SeedNotificationsAsync(count: 2);

        using var student = await world.StudentClientAsync();

        (await MarkReadAsync(student, ids: null)).MarkedCount.Should().Be(2);
        (await MarkReadAsync(student, ids: null)).MarkedCount.Should().Be(0);
    }

    /// <summary>
    /// 🔴 BOSHQA ODAMNING QATORINI BELGILAB BO'LMAYDI.
    ///
    /// 403 emas, JIMGINA e'tiborsiz (`MarkedCount = 0`): 403 qaytarish
    /// hujumchiga "bu Id mavjud" deb aytardi. Muhimi — begona qator
    /// O'QILMAGAN bo'lib qolishi.
    /// </summary>
    [Fact]
    public async Task MarkRead_WithForeignIds_ChangesNothing()
    {
        var mine = await NotificationWorld.CreateAsync(factory, "ntf-r");
        var other = await NotificationWorld.CreateAsync(factory, "ntf-s");

        var foreignIds = await other.SeedNotificationsAsync(count: 2);

        using var student = await mine.StudentClientAsync();

        (await MarkReadAsync(student, foreignIds)).MarkedCount.Should().Be(0);

        using var victim = await other.StudentClientAsync();
        (await UnreadAsync(victim)).Should().Be(2, "begona qator tegilmagan bo'lishi kerak");
    }

    [Fact]
    public async Task Endpoints_RequireAuthentication()
    {
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync(new Uri("/api/v1/notifications", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================== yordamchi

    private static async Task<int> UnreadAsync(HttpClient client)
    {
        var body = await client.GetFromJsonAsync<NotificationUnreadResponse>(
            new Uri("/api/v1/notifications/unread-count", UriKind.Relative));

        return body!.UnreadCount;
    }

    private static async Task<NotificationReadResponse> MarkReadAsync(
        HttpClient client, IReadOnlyList<long>? ids)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/notifications/read", UriKind.Relative), new { ids });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<NotificationReadResponse>())!;
    }

    // ---------------------------------------------------------------- javob shakllari

    private sealed record NotificationItemResponse(
        long Id, string Kind, string Title, string Body, long? EntityId, bool Read, DateTimeOffset CreatedAt);

    private sealed record NotificationPageResponse(
        IReadOnlyList<NotificationItemResponse> Items, bool HasMore, long? NextBeforeId, int UnreadCount);

    private sealed record NotificationUnreadResponse(int UnreadCount);

    private sealed record NotificationReadResponse(int MarkedCount, int UnreadCount);
}

// ========================================================================= test infratuzilmasi

/// <summary>
/// Bildirishnoma dunyosi: guruh, o'quvchi, ustoz, vazifa va topshirilgan
/// javob. Har test O'Z prefiksi bilan yasaydi — bitta baza bo'ylab
/// izolyatsiya shu tarzda ta'minlanadi.
/// </summary>
internal sealed class NotificationWorld
{
    private NotificationApiFactory _factory = null!;

    public long StudentId { get; private set; }

    public long TeacherId { get; private set; }

    public long AssignmentId { get; private set; }

    public long SubmissionId { get; private set; }

    private string StudentEmail => $"{_prefix}-student@zinnur.uz";

    private string _prefix = string.Empty;

    public static async Task<NotificationWorld> CreateAsync(
        NotificationApiFactory factory, string prefix, long? telegramId = null)
    {
        var world = new NotificationWorld { _factory = factory, _prefix = prefix };

        await world.SeedAsync(telegramId);

        return world;
    }

    private async Task SeedAsync(long? telegramId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var courseId = await db.Courses.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync();

        var teacher = new User
        {
            FullName = $"{_prefix} ustoz",
            Email = $"{_prefix}-teacher@zinnur.uz",
            Phone = TestPhones.Next(),
            Role = UserRole.Teacher,
            IsActive = true,

            // Parol bilan kirish 2026-08-13 da OLIB TASHLANDI (R26) — ustun
            // esa bazada `NOT NULL` bo'lib qoldi. To'ldiruvchi qiymat hech
            // qachon tekshirilmaydi: token `factory.LoginAsync` da
            // to'g'ridan-to'g'ri yasaladi.
            PasswordHash = "test-only-placeholder",
        };

        var student = new User
        {
            FullName = $"{_prefix} o'quvchi",
            Email = StudentEmail,
            Phone = TestPhones.Next(),
            Role = UserRole.Student,
            IsActive = true,
            PasswordHash = "test-only-placeholder",
        };

        db.Users.AddRange(teacher, student);
        await db.SaveChangesAsync();

        if (telegramId is { } chatId)
        {
            // Domain metodi orqali — `TelegramId`/`TelegramLinkedAt` uchligi
            // BIRGA yoziladi (qo'lda yozilsa "bog'langan, sanasi yo'q" holati
            // paydo bo'lardi).
            student.LinkTelegram(chatId, $"{_prefix}_tg", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var group = new Group
        {
            Name = $"{_prefix} guruh",
            CourseId = courseId,
            TeacherId = teacher.Id,
            IsActive = true,
        };

        db.Groups.Add(group);
        await db.SaveChangesAsync();

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = group.Id,
            StudentId = student.Id,
            Status = MemberStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        var assignment = new Assignment
        {
            GroupId = group.Id,
            Title = $"{_prefix} vazifasi",
            MaxScore = 5m,
            AllowedFormats = AnswerFormats.Text,
        };

        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var submission = new Submission
        {
            AssignmentId = assignment.Id,
            StudentId = student.Id,
            Text = "Javob",
            Status = SubmissionStatus.Submitted,
            SubmittedAt = DateTimeOffset.UtcNow,
            AttemptNumber = 1,
        };

        db.Submissions.Add(submission);
        await db.SaveChangesAsync();

        StudentId = student.Id;
        TeacherId = teacher.Id;
        AssignmentId = assignment.Id;
        SubmissionId = submission.Id;
    }

    public async Task<HttpClient> StudentClientAsync()
    {
        var tokens = await _factory.LoginAsync(StudentEmail);
        return _factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> TeacherClientAsync()
    {
        var tokens = await _factory.LoginAsync($"{_prefix}-teacher@zinnur.uz");
        return _factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    public async Task GradeAsync(decimal score, string? feedback)
    {
        var response = await GradeResponseAsync(score, feedback);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
    }

    public async Task<HttpResponseMessage> GradeResponseAsync(decimal score, string? feedback)
    {
        using var teacher = await TeacherClientAsync();

        return await teacher.PostAsJsonAsync(
            new Uri(
                string.Create(CultureInfo.InvariantCulture, $"/api/v1/submissions/{SubmissionId}/grade"),
                UriKind.Relative),
            new { score, feedback });
    }

    public async Task ReopenAsync(string note)
    {
        using var teacher = await TeacherClientAsync();

        var response = await teacher.PostAsJsonAsync(
            new Uri(
                string.Create(CultureInfo.InvariantCulture, $"/api/v1/submissions/{SubmissionId}/reopen"),
                UriKind.Relative),
            new { note });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
    }

    public async Task ResubmitAsync(string text)
    {
        using var student = await StudentClientAsync();

        using var content = new MultipartFormDataContent { { new StringContent(text), "text" } };

        var response = await student.PostAsync(
            new Uri(
                string.Create(CultureInfo.InvariantCulture, $"/api/v1/assignments/{AssignmentId}/submit"),
                UriKind.Relative),
            content);

        response.IsSuccessStatusCode.Should().BeTrue(
            await response.Content.ReadAsStringAsync());
    }

    public Task<int> NotificationCountAsync() => _factory.WithDbAsync(db =>
        db.Notifications.CountAsync(n => n.UserId == StudentId));

    /// <summary>Shu o'quvchiga atalgan navbat yozuvlari.</summary>
    public Task<List<MessageOutbox>> OutboxAsync() => _factory.WithDbAsync(db =>
        db.MessageOutbox.AsNoTracking()
            .Where(m => m.RecipientUserId == StudentId)
            .OrderBy(m => m.Id)
            .ToListAsync());

    /// <summary>
    /// Sahifalash testlari uchun tayyor qatorlar (baholashsiz — bu yerda
    /// tekshirilayotgani RO'YXAT, hodisa emas).
    /// </summary>
    public async Task<IReadOnlyList<long>> SeedNotificationsAsync(int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTimeOffset.UtcNow;
        var rows = new List<Notification>(count);

        for (var i = 0; i < count; i++)
        {
            rows.Add(Notification.Create(
                StudentId,
                NotificationKind.SubmissionGraded,
                $"Sarlavha {i}",
                $"Tana {i}",
                SubmissionId,
                now.AddSeconds(i)));
        }

        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync();

        return [.. rows.Select(r => r.Id)];
    }
}

/// <summary>
/// SignalR hub konteksti YOZIB BORUVCHI soxta bilan almashtirilgan API.
///
/// ★ NIMA UCHUN `INotificationNotifier` emas, aynan `IHubContext`
/// almashtiriladi: port almashtirilsa HAQIQIY `NotificationNotifier`
/// umuman bajarilmasdi va uning eng nozik ikki qismi — `Clients.User`
/// yo'nalishi hamda istisnoni yutish — sinovsiz qolardi
/// (`GroupChatRealtimeFactory` dagi AYNI qaror).
/// </summary>
public sealed class NotificationApiFactory : ZinnurApiFactory
{
    public RecordingNotificationHub Hub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            // `AddSignalR` `IHubContext<>` ni OCHIQ generic sifatida yozadi;
            // yopiq (aniq tur uchun) ro'yxat undan ustun turadi.
            services.RemoveAll<IHubContext<NotificationHub>>();

            services.AddSingleton(sp =>
            {
                Hub.UseScopes(sp.GetRequiredService<IServiceScopeFactory>());
                return Hub;
            });
            services.AddSingleton<IHubContext<NotificationHub>>(
                sp => sp.GetRequiredService<RecordingNotificationHub>());
        });
    }
}

/// <summary>Bitta tarqatish haqidagi yozuv.</summary>
/// <param name="UserId">
/// SignalR "foydalanuvchi" identifikatori — `Clients.User(...)` ga
/// berilgan satr. Aynan shu yo'nalish dalili.
/// </param>
/// <param name="ExistsInDatabase">
/// Tarqatish PAYTIDA qator bazada bormi — commit-then-send dalili.
/// </param>
public sealed record NotificationBroadcast(
    string UserId,
    string Method,
    NotificationDto Notification,
    bool ExistsInDatabase);

/// <summary>
/// <c>IHubContext&lt;NotificationHub&gt;</c> o'rnini bosuvchi yozib boruvchi.
///
/// 🔴 FAQAT <c>User</c> yo'li qo'llab-quvvatlanadi. Qolganlari
/// (<c>All</c>, <c>Group</c>, ...) ATAYLAB istisno ko'taradi: kimdir
/// tarqatishni kengaytirsa, begona o'quvchining bahosi va ustozning izohi
/// hammaga ko'rinardi — bunday o'zgarish JIMGINA o'tib ketmasin.
/// </summary>
public sealed class RecordingNotificationHub : IHubContext<NotificationHub>
{
    private readonly ConcurrentQueue<NotificationBroadcast> _broadcasts = new();

    private IServiceScopeFactory? _scopeFactory;

    internal void UseScopes(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <summary>Keyingi tarqatish istisno bilan yiqilsinmi.</summary>
    public bool FailBroadcast { get; set; }

    public IHubClients Clients => new RecordingClients(this);

    public IGroupManager Groups { get; } = new UnsupportedGroupManager();

    public void Clear() => _broadcasts.Clear();

    public IReadOnlyList<NotificationBroadcast> Take() => [.. _broadcasts];

    private async Task RecordAsync(
        string userId, string method, object?[] args, CancellationToken ct)
    {
        if (args.Length > 0 && args[0] is NotificationDto dto)
            _broadcasts.Enqueue(new NotificationBroadcast(
                userId, method, dto, await ExistsAsync(dto.Id, ct)));

        if (FailBroadcast)
            throw new InvalidOperationException("Soxta SignalR nosozligi (test).");
    }

    /// <summary>
    /// YANGI scope — so'rovning o'z <c>DbContext</c> i emas: shu sababli
    /// javob haqiqatan BAZAGA yozilganini bildiradi, kuzatuvchidagi keshni
    /// emas.
    /// </summary>
    private async Task<bool> ExistsAsync(long notificationId, CancellationToken ct)
    {
        if (_scopeFactory is null) return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Notifications.AsNoTracking().AnyAsync(n => n.Id == notificationId, ct);
    }

    private static NotSupportedException Unsupported() =>
        new("Bildirishnoma FAQAT `Clients.User(...)` orqali yuboriladi.");

    private sealed class RecordingClients(RecordingNotificationHub owner) : IHubClients
    {
        public IClientProxy User(string userId) => new RecordingProxy(owner, userId);

        public IClientProxy Users(IReadOnlyList<string> userIds) => throw Unsupported();

        public IClientProxy All => throw Unsupported();

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
            throw Unsupported();

        public IClientProxy Client(string connectionId) => throw Unsupported();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw Unsupported();

        public IClientProxy Group(string groupName) => throw Unsupported();

        public IClientProxy GroupExcept(
            string groupName, IReadOnlyList<string> excludedConnectionIds) => throw Unsupported();

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw Unsupported();
    }

    private sealed class RecordingProxy(RecordingNotificationHub owner, string userId) : IClientProxy
    {
        public Task SendCoreAsync(
            string method, object?[] args, CancellationToken cancellationToken = default) =>
            owner.RecordAsync(userId, method, args, cancellationToken);
    }

    private sealed class UnsupportedGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(
            string connectionId, string groupName, CancellationToken ct = default) =>
            throw Unsupported();

        public Task RemoveFromGroupAsync(
            string connectionId, string groupName, CancellationToken ct = default) =>
            throw Unsupported();
    }
}
