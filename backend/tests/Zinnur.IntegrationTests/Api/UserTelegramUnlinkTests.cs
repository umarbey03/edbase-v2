using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// TELEGRAM BOG'LANISHINI UZISH — <c>POST /users/{id}/telegram/unlink</c>
/// ========================================================================
///
/// Talab: "ulanishni uzish imkoni (uzilgach o'quvchi platformaga kira
/// olmaydi)". O'quvchi platformaga FAQAT Telegram orqali kiradi, ya'ni bu
/// amal — kirish huquqini olib qo'yish.
///
/// 🔴 SHU SABABLI TEST UCHDAN-UCHGACHA: `TelegramId` ni `null` qilish
/// YETARLI EMAS. O'quvchining qo'lidagi kirish tokeni yana 15 daqiqa
/// yaroqli bo'lib turardi va u shu vaqt ichida darsga kirib, chatga yozib
/// yurardi. Aynan shu turkumdagi zaiflik loyihada bir marta jonli
/// tekshiruvda topilgan (`AccessTokenRevocationTests` izohi).
/// </summary>
public sealed class UserTelegramUnlinkTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    /// <summary>
    /// 🔴 ASOSIY TEST: uzishdan keyin ESKI TOKEN O'LADI.
    ///
    /// `TokenVersion` bazada oshgani va tokenning amalda rad etilishi
    /// IKKALASI tekshiriladi: birinchisi bo'lmasa ikkinchisi tasodifan
    /// (masalan kesh tufayli) o'tib ketishi mumkin, ikkinchisi bo'lmasa
    /// esa hisoblagich oshib, lekin tekshiruv ishlamayotgan bo'lishi mumkin.
    /// </summary>
    [Fact]
    public async Task Unlink_KillsExistingAccessToken()
    {
        var world = await WorldBuilder.CreateAsync(factory, "unlink-token");

        await ProfileWorldBuilder.LinkTelegramAsync(
            factory, world.Student.Id, ProfileWorldBuilder.NextTelegramId(), "kech_qolgan");

        // Bog'lash `TokenVersion` ga tegmaydi, shuning uchun o'quvchi
        // hozirgi paroli bilan kira oladi va bizda "tirik" token bo'ladi.
        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        (await student.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.OK, "uzishdan OLDIN token ishlashi kerak");

        var before = await ProfileWorldBuilder.TokenVersionOfAsync(factory, world.Student.Id);

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.PostAsJsonAsync(
            UnlinkUri(world.Student.Id), new { reason = "Raqam boshqa odamga o'tgan" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        var body = (await response.Content.ReadFromJsonAsync<UnlinkResponse>())!;
        body.TelegramId.Should().BeNull();
        body.TelegramUsername.Should().BeNull();

        var after = await ProfileWorldBuilder.TokenVersionOfAsync(factory, world.Student.Id);
        after.Should().Be(before + 1, "sessiya versiyasi oshishi SHART");

        // ★ UCHDAN-UCHGACHA: eski token endi ishlamaydi.
        var afterUnlink = await student.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        afterUnlink.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "uzilgan o'quvchi eski token bilan ichkarida qolmasligi kerak");
    }

    [Fact]
    public async Task Unlink_ClearsIdAndUsernameInDatabase()
    {
        var world = await WorldBuilder.CreateAsync(factory, "unlink-baza");

        await ProfileWorldBuilder.LinkTelegramAsync(
            factory, world.Student.Id, ProfileWorldBuilder.NextTelegramId(), "eski_nom");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.PostAsJsonAsync(UnlinkUri(world.Student.Id), new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        var state = await factory.WithDbAsync(db => db.Users
            .AsNoTracking()
            .Where(u => u.Id == world.Student.Id)
            .Select(u => new { u.TelegramId, u.TelegramUsername, u.TelegramLinkedAt })
            .FirstAsync());

        state.TelegramId.Should().BeNull();
        state.TelegramUsername.Should().BeNull();
        state.TelegramLinkedAt.Should().BeNull();
    }

    /// <summary>
    /// 🔴 Bog'lanmagan profilda — 409 va tushunarli sabab.
    ///
    /// 200 qaytarish "hech nima qilinmadi, lekin muvaffaqiyat" degan
    /// yolg'on bo'lardi: xodim tugmani bosib, ish bajarildi deb o'ylardi.
    /// </summary>
    [Fact]
    public async Task Unlink_WhenNotLinked_IsConflict()
    {
        var world = await WorldBuilder.CreateAsync(factory, "unlink-yoq");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.PostAsJsonAsync(UnlinkUri(world.Student.Id), new { });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await WorldBuilder.Body(response));

        // ⚠️ Apostrof JSON'da `'` bo'lib qochiriladi, shuning uchun
        //    tekshiruv apostrofsiz bo'lakka qaraydi.
        var problem = await response.Content.ReadAsStringAsync();
        problem.Should().Contain("Telegram hisobi",
            "409 sababi `detail` da tushunarli bo'lishi kerak");

        var version = await ProfileWorldBuilder.TokenVersionOfAsync(factory, world.Student.Id);
        version.Should().Be(0, "bo'lmagan amal sessiyalarni bekor qilmasligi kerak");
    }

    [Fact]
    public async Task Unlink_ForMissingUser_IsNotFound()
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.PostAsJsonAsync(
            "/api/v1/users/99999999/telegram/unlink", new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Ustoz uzish huquqiga ega EMAS (darvoza sinf atributida).</summary>
    [Fact]
    public async Task Unlink_ByTeacher_IsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "unlink-ustoz");

        await ProfileWorldBuilder.LinkTelegramAsync(
            factory, world.Student.Id, ProfileWorldBuilder.NextTelegramId());

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.PostAsJsonAsync(UnlinkUri(world.Student.Id), new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(response));
    }

    /// <summary>
    /// Audit izi SAQLANADI va profilda KO'RINADI: "kim, qachon, nima
    /// sababdan uzdi". Yozuv faqat bazada qolsa uni hech kim ko'rmasdi.
    /// </summary>
    [Fact]
    public async Task Unlink_LeavesAuditTrailVisibleInProfile()
    {
        var world = await WorldBuilder.CreateAsync(factory, "unlink-audit");

        await ProfileWorldBuilder.LinkTelegramAsync(
            factory, world.Student.Id, ProfileWorldBuilder.NextTelegramId(), "audit_nom");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.PostAsJsonAsync(
            UnlinkUri(world.Student.Id), new { reason = "Ota-onasi so'radi" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        var audit = await factory.WithDbAsync(db => db.TelegramUnlinkAudits
            .AsNoTracking()
            .Where(a => a.UserId == world.Student.Id)
            .Select(a => new { a.OldTelegramId, a.OldTelegramUsername, a.Reason, a.ActorId })
            .FirstAsync());

        audit.OldTelegramUsername.Should().Be("audit_nom");
        audit.Reason.Should().Be("Ota-onasi so'radi");
        audit.OldTelegramId.Should().BePositive();
        audit.ActorId.Should().BePositive();

        var profile = await ProfileWorldBuilder.GetProfileAsync(admin, world.Student.Id);

        profile.Telegram.Linked.Should().BeFalse();
        profile.Telegram.UnlinkedAt.Should().NotBeNull();
        profile.Telegram.UnlinkedByName.Should().NotBeNullOrEmpty();
        profile.Telegram.UnlinkReason.Should().Be("Ota-onasi so'radi");
    }

    /// <summary>Bog'langan profilning username'i va bog'lanish vaqti profilda ko'rinadi.</summary>
    [Fact]
    public async Task Profile_ShowsTelegramUsernameAndLinkedState()
    {
        var world = await WorldBuilder.CreateAsync(factory, "unlink-holat");
        var telegramId = ProfileWorldBuilder.NextTelegramId();

        await ProfileWorldBuilder.LinkTelegramAsync(
            factory, world.Student.Id, telegramId, "@ali_valiyev");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var profile = await ProfileWorldBuilder.GetProfileAsync(admin, world.Student.Id);

        profile.Telegram.Linked.Should().BeTrue();
        profile.Telegram.TelegramId.Should().Be(telegramId);
        profile.Telegram.Username.Should().Be("ali_valiyev", "`@` saqlanmaydi");
        profile.Telegram.LinkedAt.Should().NotBeNull();

        // Ro'yxat DTO'sida ham bor (ro'yxatda `@username` ko'rsatish uchun).
        profile.User.TelegramUsername.Should().Be("ali_valiyev");
    }

    private static string UnlinkUri(long userId) =>
        "/api/v1/users/" + userId.ToString(CultureInfo.InvariantCulture) + "/telegram/unlink";
}
