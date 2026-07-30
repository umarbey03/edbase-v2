using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// QARZDORLIK DARVOZASI (FAZA 4.3)
/// ========================================================================
///
/// Uch shart BIRGALIKDA: qarz chegaradan OSHGAN + o'quvchi istisno emas +
/// sozlamadagi qamrov so'ralayotgan turkumni O'Z ICHIGA OLADI.
///
/// ★ ESKI TIZIM MUAMMOSI: bu shartlar endpointlar bo'ylab tarqalgan edi va
/// ba'zi joyda `&gt;=`, ba'zi joyda `&gt;` yozilgandi — bir xil qarzli
/// o'quvchi bir sahifada bloklanib, boshqasida o'tib ketardi. Endi qoida
/// Domain'da bitta joyda, bu testlar esa uni ENDPOINT darajasida qotiradi.
///
/// ★ HAR TEST O'Z O'QUVCHISINI YARATADI. Seed'dagi demo o'quvchini qayta
/// ishlatish vasvasasi bor edi, lekin sinf ichidagi testlar BITTA bazani
/// bo'lishadi: bir test qarzni to'lasa, boshqasi qarzdorsiz qolib
/// sababsiz yiqilardi. Sozlama esa umumiy — shuning uchun har test uni
/// O'ZI uchun OSHKOR o'rnatadi (tartibga tayanmaydi).
/// </summary>
public sealed class PaymentBlockTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>Standart chegara 540 000; qarz undan OSHISHI uchun 600 000.</summary>
    private const decimal DebtAmount = 600_000m;

    private const decimal DefaultThreshold = 540_000m;
    private const string Period = "2026-04";

    /// <summary>
    /// ★ Qarzdor o'quvchi VIDEO darslarga (kurs daraxtiga) kira olmaydi,
    /// va 403 matnida QARZ, CHEGARA va nima qilish kerakligi yoziladi —
    /// "Ruxsat yo'q" degan quruq matn qo'ng'iroqlar oqimini keltirardi.
    /// </summary>
    [Fact]
    public async Task Debtor_IsBlockedFromCourseContent_AndUnblockedAfterPaying()
    {
        await SetSettingsAsync(DefaultThreshold, PaymentBlockScope.Video);

        var world = await NewDebtorAsync();
        var courseId = await FirstCourseIdAsync();

        using var student = await ClientAsync(world.Email, world.Password);
        var courseUri = new Uri($"/api/v1/courses/{courseId}", UriKind.Relative);

        var blocked = await student.GetAsync(courseUri);

        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var message = await blocked.Content.ReadAsStringAsync();
        message.Should().Contain("600000");
        message.Should().Contain("540000");

        // To'lov qilinsa — darvoza darhol ochiladi.
        await PayAsync(world.StudentId, DebtAmount);

        var allowed = await student.GetAsync(courseUri);

        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// ★ QAMROV IERARXIYASI ENDPOINT DARAJASIDA: standart qamrov
    /// <c>Video</c> jonli darsni O'Z ICHIGA OLMAYDI — qarzdor o'quvchi
    /// jonli darsga baribir kiradi. <c>Platform</c> qilinsa — kirmaydi.
    ///
    /// Ikki holat bitta testda ATAYLAB: ular bitta qoidaning ikki tomoni va
    /// ajratilsa, ikkinchisi qo'shilishi unutilardi.
    ///
    /// ★ TEKSHIRUV JOYI MUHIM: LiveKit tokeni berilgandan keyin klient
    /// serverdan o'tib, to'g'ridan-to'g'ri media serverga ulanadi — ya'ni
    /// "yo'q" deyishning oxirgi imkoniyati aynan shu endpoint.
    /// </summary>
    [Fact]
    public async Task LiveJoin_IsBlockedOnlyWhenScopeCoversLive()
    {
        await SetSettingsAsync(DefaultThreshold, PaymentBlockScope.Video);

        var world = await NewDebtorAsync();
        var sessionId = await StartedSessionAsync(world.GroupId);

        using var student = await ClientAsync(world.Email, world.Password);
        var tokenUri = new Uri($"/api/v1/live-sessions/{sessionId}/token", UriKind.Relative);

        // 1) Qamrov = Video -> jonli dars YOPILMAYDI.
        var allowed = await student.PostAsync(tokenUri, content: null);

        allowed.StatusCode.Should().Be(HttpStatusCode.OK,
            "Video qamrovi jonli darsni o'z ichiga olmaydi");

        // 2) Qamrov = Platform -> hammasi yopiladi.
        await SetSettingsAsync(DefaultThreshold, PaymentBlockScope.Platform);

        var denied = await student.PostAsync(tokenUri, content: null);

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var message = await denied.Content.ReadAsStringAsync();
        message.Should().Contain("jonli darsga kirish");
    }

    /// <summary>
    /// ★ CHEGARA BAZADAN o'qiladi: uni ko'tarish DARHOL kuchga kiradi
    /// (relizsiz, serverga tegmasdan). Aynan shu sabab sozlama
    /// konfiguratsiyada emas.
    /// </summary>
    [Fact]
    public async Task RaisingThresholdInSettings_TakesEffectImmediately()
    {
        await SetSettingsAsync(DefaultThreshold, PaymentBlockScope.Video);

        var world = await NewDebtorAsync();
        var courseId = await FirstCourseIdAsync();

        using var student = await ClientAsync(world.Email, world.Password);
        var courseUri = new Uri($"/api/v1/courses/{courseId}", UriKind.Relative);

        var before = await student.GetAsync(courseUri);
        before.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await SetSettingsAsync(1_000_000m, PaymentBlockScope.Video);

        var after = await student.GetAsync(courseUri);
        after.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await student.GetFromJsonAsync<BlockResponse>(
            $"/api/v1/payments/students/{world.StudentId}/block?scope=Video");

        status!.Blocked.Should().BeFalse();
        status.Threshold.Should().Be(1_000_000m);
        status.Debt.Should().Be(DebtAmount);
    }

    /// <summary>Istisno qilingan o'quvchi HECH QACHON bloklanmaydi.</summary>
    [Fact]
    public async Task ExemptStudent_IsNeverBlocked()
    {
        await SetSettingsAsync(DefaultThreshold, PaymentBlockScope.Video);

        var world = await NewDebtorAsync();
        var courseId = await FirstCourseIdAsync();

        using var admin = await AdminClientAsync();

        var exempt = await admin.PostAsJsonAsync(
            $"/api/v1/payments/students/{world.StudentId}/exempt",
            new { exempt = true, reason = "Homiy to'laydi" });

        exempt.StatusCode.Should().Be(HttpStatusCode.OK);

        using var student = await ClientAsync(world.Email, world.Password);

        var response = await student.GetAsync(new Uri($"/api/v1/courses/{courseId}", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await student.GetFromJsonAsync<BlockResponse>(
            $"/api/v1/payments/students/{world.StudentId}/block?scope=Video");

        status!.Exempt.Should().BeTrue();
        status.Blocked.Should().BeFalse();
        status.Debt.Should().Be(DebtAmount, "istisno qarzni YO'QOTMAYDI, faqat blokni to'xtatadi");
    }

    /// <summary>
    /// Blok holati endpointi — 403 ga duch kelmasdan OLDIN tekshirish uchun.
    /// O'quvchi o'zining holatini ko'radi.
    /// </summary>
    [Fact]
    public async Task BlockStatus_IsVisibleToTheStudentBeforeHittingTheWall()
    {
        await SetSettingsAsync(DefaultThreshold, PaymentBlockScope.Video);

        var world = await NewDebtorAsync();

        using var student = await ClientAsync(world.Email, world.Password);

        var status = await student.GetFromJsonAsync<BlockResponse>(
            $"/api/v1/payments/students/{world.StudentId}/block?scope=Video");

        status!.Blocked.Should().BeTrue();
        status.Debt.Should().Be(DebtAmount);
        status.Threshold.Should().Be(DefaultThreshold);
        status.Enforced.Should().BeTrue();
        status.Reason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Ustoz sozlamaga ham, blok holatiga ham kira olmaydi.</summary>
    [Fact]
    public async Task Settings_AreClosedForTeachers()
    {
        var world = await NewDebtorAsync();

        using var teacher = await ClientAsync(world.TeacherEmail, world.Password);

        var read = await teacher.GetAsync(new Uri("/api/v1/payments/settings", UriKind.Relative));
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var write = await teacher.PutAsJsonAsync("/api/v1/payments/settings", new
        {
            blockThreshold = 0m,
            blockScope = "None",
        });

        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var status = await teacher.GetAsync(new Uri(
            $"/api/v1/payments/students/{world.StudentId}/block?scope=Video", UriKind.Relative));

        status.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- yordamchi

    /// <summary>Chegaradan OSHIQ qarzi bor YANGI o'quvchi (o'z guruhi bilan).</summary>
    private async Task<DebtorWorld> NewDebtorAsync()
    {
        using var admin = await AdminClientAsync();

        var teacher = await CreateUserAsync(admin, UserRole.Teacher);
        var student = await CreateUserAsync(admin, UserRole.Student);

        var courseId = await FirstCourseIdAsync();

        var groupResponse = await admin.PostAsJsonAsync("/api/v1/groups", new
        {
            name = "Blok-" + Guid.NewGuid().ToString("N")[..6],
            startDate = "2026-01-05",
            weekdays = new[] { "Monday", "Wednesday" },
            startTime = "19:00:00",
            courseId,
            teacherId = teacher.Id,

            // 1 oy — jadval qisqa bo'lsin (test tezligi uchun).
            courseMonths = 1,
        });

        groupResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var group = (await groupResponse.Content.ReadFromJsonAsync<CreateGroupResponse>())!;

        var member = await admin.PostAsJsonAsync(
            $"/api/v1/groups/{group.Group.Id}/members", new { studentId = student.Id });

        member.StatusCode.Should().Be(HttpStatusCode.Created);

        var tariff = await admin.PostAsJsonAsync("/api/v1/payments/tariffs", new
        {
            name = "Blok tarifi",
            amount = DebtAmount,
            activeFrom = "2020-01-01",
            groupId = group.Group.Id,
        });

        tariff.StatusCode.Should().Be(HttpStatusCode.Created);

        var opened = await admin.PostAsJsonAsync(
            "/api/v1/payments/periods/open",
            new { period = Period, groupId = group.Group.Id });

        opened.StatusCode.Should().Be(HttpStatusCode.OK);

        return new DebtorWorld(
            student.Id, student.Email, student.Password, teacher.Email, group.Group.Id);
    }

    private async Task SetSettingsAsync(decimal threshold, PaymentBlockScope scope)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync("/api/v1/payments/settings", new
        {
            blockThreshold = threshold,
            blockScope = scope.ToString(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task PayAsync(long studentId, decimal amount)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/payments", new
        {
            studentId,
            amount,
            method = "Cash",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Guruhning birinchi darsini JONLI holatga keltiradi. To'g'ridan-to'g'ri
    /// bazada: bu test darvozani tekshiradi, dars boshlash oqimini emas
    /// (u `LiveSessionEndpointsTests` da qoplangan).
    /// </summary>
    private Task<long> StartedSessionAsync(long groupId) =>
        factory.WithDbAsync(async db =>
        {
            var session = await db.LiveSessions
                .Where(s => s.GroupId == groupId)
                .OrderBy(s => s.Id)
                .FirstAsync();

            session.Status = SessionStatus.Live;
            session.ActualStart = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync();
            return session.Id;
        });

    private Task<long> FirstCourseIdAsync() =>
        factory.WithDbAsync(db => db.Courses.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync());

    private static async Task<(long Id, string Email, string Password)> CreateUserAsync(
        HttpClient client, UserRole role)
    {
        var email = $"blk-{Guid.NewGuid():N}"[..16] + "@zinnur.uz";
        const string password = "Blok!2345";

        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            fullName = "Blok " + role.ToString(),
            email,
            role = role.ToString(),
            password,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreatedUserResponse>();
        return (created!.User.Id, email, password);
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> ClientAsync(string email, string password)
    {
        var tokens = await factory.LoginAsync(email, password);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private sealed record DebtorWorld(
        long StudentId,
        string Email,
        string Password,
        string TeacherEmail,
        long GroupId);

    private sealed record CreateGroupResponse(GroupRef Group);

    private sealed record GroupRef(long Id);

    private sealed record CreatedUserResponse(UserRef User);

    private sealed record UserRef(long Id);

    internal sealed record BlockResponse(
        long StudentId,
        bool Blocked,
        decimal Debt,
        decimal Threshold,
        string ConfiguredScope,
        string RequestedScope,
        bool Exempt,
        bool Enforced,
        string? Reason);
}

/// <summary>
/// YUMSHOQ REJIM: <c>Payments:EnforceBlock=false</c>.
///
/// ★ NIMA UCHUN KALIT KONFIGURATSIYADA, BAZADA EMAS: staging bazasi odatda
/// prod nusxasidan tiklanadi. Kalit bazada tursa, prod'ning "qattiq rejim"
/// qiymati staging'ga ham ko'chib o'tardi va sinovchilar tasodifan
/// bloklanib qolardi. Konfiguratsiyada esa u MUHIT bilan birga keladi.
/// </summary>
public sealed class SoftModePaymentFactory : ZinnurApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        new("Payments:EnforceBlock", "false"),
    ];
}

/// <summary>Yumshoq rejimda qarz KO'RINADI, lekin hech kim bloklanmaydi.</summary>
public sealed class PaymentSoftModeTests(SoftModePaymentFactory factory)
    : IClassFixture<SoftModePaymentFactory>
{
    [Fact]
    public async Task WithEnforceDisabled_DebtorIsNotBlocked()
    {
        var adminTokens = await factory.LoginAsAdminAsync();
        using var admin = factory.CreateAuthorizedClient(adminTokens.AccessToken);

        var tariff = await admin.PostAsJsonAsync("/api/v1/payments/tariffs", new
        {
            name = "Demo tarif",
            amount = 600_000m,
            activeFrom = "2020-01-01",
        });

        tariff.StatusCode.Should().Be(HttpStatusCode.Created);

        var opened = await admin.PostAsJsonAsync(
            "/api/v1/payments/periods/open", new { period = "2026-04" });

        opened.StatusCode.Should().Be(HttpStatusCode.OK);

        var studentId = await factory.WithDbAsync(db => db.Users
            .Where(u => u.Email == "student@zinnur.uz")
            .Select(u => u.Id)
            .FirstAsync());

        var courseId = await factory.WithDbAsync(db => db.Courses
            .OrderBy(c => c.Id).Select(c => c.Id).FirstAsync());

        var studentTokens = await factory.LoginAsync("student@zinnur.uz", "Demo!2345");
        using var student = factory.CreateAuthorizedClient(studentTokens.AccessToken);

        var response = await student.GetAsync(new Uri($"/api/v1/courses/{courseId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "yumshoq rejimda hech kim bloklanmaydi");

        var status = await student.GetFromJsonAsync<PaymentBlockTests.BlockResponse>(
            $"/api/v1/payments/students/{studentId}/block?scope=Video");

        status!.Enforced.Should().BeFalse();
        status.Blocked.Should().BeFalse();
        status.Debt.Should().Be(600_000m, "qarz baribir hisoblanadi va ko'rinadi");
    }
}
