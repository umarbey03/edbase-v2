using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// FOYDALANUVCHILAR RO'YXATIDAGI YANGI FILTRLAR (BLOK F)
/// ========================================================================
///
/// <c>GET /api/v1/users?groupId=...&amp;telegramLinked=...</c>
///
/// ★ ENG MUHIM QOIDA: <c>groupId</c> filtri FAQAT <c>Active</c> a'zoni
/// qaytaradi. Chiqarilgan (<c>Stopped</c>) yoki ko'chirilgan (<c>Moved</c>)
/// o'quvchi ro'yxatda ko'rinsa, xodim uni hali shu guruhda o'qiyapti deb
/// o'ylardi — guruh ro'yxatlari, davomat rejasi va to'lov kutilmalari
/// jimgina noto'g'ri bo'lardi.
/// </summary>
public sealed class UserListFilterTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ================================================================= guruh

    [Fact]
    public async Task GroupFilter_ReturnsOnlyActiveMembersOfThatGroup()
    {
        var world = await WorldBuilder.CreateAsync(factory, "filtr-guruh");
        var classmate = await WorldBuilder.AddStudentAsync(factory, world.GroupId, "filtr-sinf");
        var other = await WorldBuilder.CreateAsync(factory, "filtr-boshqa");

        var page = await ListAsync(groupId: world.GroupId);

        var ids = page.Items.ConvertAll(u => u.Id);

        ids.Should().Contain(world.Student.Id).And.Contain(classmate.Id);
        ids.Should().NotContain(other.Student.Id, "boshqa guruh o'quvchisi kirmasligi kerak");
        ids.Should().NotContain(world.Teacher.Id, "ustoz guruh A'ZOSI emas");

        page.Total.Should().Be(2);
    }

    /// <summary>🔴 Guruhdan CHIQARILGAN o'quvchi filtrda KO'RINMAYDI.</summary>
    [Fact]
    public async Task GroupFilter_ExcludesStoppedMember()
    {
        var world = await WorldBuilder.CreateAsync(factory, "filtr-chiqar");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var remove = await admin.DeleteAsync(new Uri(
            $"/api/v1/groups/{world.GroupId}/members/{world.Student.Id}", UriKind.Relative));

        remove.IsSuccessStatusCode.Should().BeTrue(await WorldBuilder.Body(remove));

        var page = await ListAsync(groupId: world.GroupId);

        page.Items.Should().BeEmpty("chiqarilgan o'quvchi \"guruhda o'qiyapti\" deb ko'rinmasligi kerak");
        page.Total.Should().Be(0);
    }

    /// <summary>
    /// Boshqa guruhga KO'CHIRILGAN o'quvchi eski guruh filtrida yo'q,
    /// yangisida bor.
    /// </summary>
    [Fact]
    public async Task GroupFilter_AfterMove_ShowsStudentOnlyInTargetGroup()
    {
        var source = await WorldBuilder.CreateAsync(factory, "filtr-manba");
        var target = await WorldBuilder.CreateAsync(factory, "filtr-nishon");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var move = await admin.PostAsJsonAsync(
            $"/api/v1/groups/{source.GroupId}/members/{source.Student.Id}/move",
            new { targetGroupId = target.GroupId });

        move.IsSuccessStatusCode.Should().BeTrue(await WorldBuilder.Body(move));

        (await ListAsync(groupId: source.GroupId)).Items
            .Should().BeEmpty("ko'chirilgan o'quvchi eski guruhda ko'rinmasligi kerak");

        (await ListAsync(groupId: target.GroupId)).Items
            .ConvertAll(u => u.Id).Should().Contain(source.Student.Id);
    }

    [Fact]
    public async Task GroupFilter_WithUnknownGroup_ReturnsEmptyPage()
    {
        await WorldBuilder.CreateAsync(factory, "filtr-notanish");

        var page = await ListAsync(groupId: 99_999_999);

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(0);
    }

    // ================================================================= Telegram

    [Fact]
    public async Task TelegramFilter_SplitsLinkedAndUnlinked()
    {
        var world = await WorldBuilder.CreateAsync(factory, "filtr-tg");
        var unlinked = await WorldBuilder.AddStudentAsync(factory, world.GroupId, "filtr-tgyoq");

        await ProfileWorldBuilder.LinkTelegramAsync(
            factory, world.Student.Id, ProfileWorldBuilder.NextTelegramId(), "boglangan_nom");

        var linkedPage = await ListAsync(telegramLinked: true);
        var linkedIds = linkedPage.Items.ConvertAll(u => u.Id);

        linkedIds.Should().Contain(world.Student.Id);
        linkedIds.Should().NotContain(unlinked.Id);

        var unlinkedPage = await ListAsync(telegramLinked: false);
        var unlinkedIds = unlinkedPage.Items.ConvertAll(u => u.Id);

        unlinkedIds.Should().Contain(unlinked.Id);
        unlinkedIds.Should().NotContain(world.Student.Id);
    }

    /// <summary>
    /// Uzishdan keyin o'quvchi "bog'lanmaganlar" ro'yxatiga O'TADI —
    /// filtr hosila maydonga (`TelegramId != null`) tayanadi, eski holat
    /// keshiga emas.
    /// </summary>
    [Fact]
    public async Task TelegramFilter_AfterUnlink_MovesStudentToUnlinked()
    {
        var world = await WorldBuilder.CreateAsync(factory, "filtr-uzil");

        await ProfileWorldBuilder.LinkTelegramAsync(
            factory, world.Student.Id, ProfileWorldBuilder.NextTelegramId(), "uzilgan_nom");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var unlink = await admin.PostAsJsonAsync(
            $"/api/v1/users/{world.Student.Id}/telegram/unlink", new { });

        unlink.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(unlink));

        (await ListAsync(telegramLinked: true)).Items
            .ConvertAll(u => u.Id).Should().NotContain(world.Student.Id);

        (await ListAsync(telegramLinked: false)).Items
            .ConvertAll(u => u.Id).Should().Contain(world.Student.Id);
    }

    // ================================================================= birgalikda

    /// <summary>
    /// Filtrlar BIRGALIKDA "VA" bo'lib ishlaydi (bir-birini almashtirmaydi):
    /// "shu guruhda o'qiydigan, Telegram bog'lamagan o'quvchilar" —
    /// o'quv bo'limining kundalik so'rovi.
    /// </summary>
    [Fact]
    public async Task GroupAndTelegramFilters_CombineWithAnd()
    {
        var world = await WorldBuilder.CreateAsync(factory, "filtr-birga");
        var withTelegram = await WorldBuilder.AddStudentAsync(factory, world.GroupId, "filtr-bor");
        var other = await WorldBuilder.CreateAsync(factory, "filtr-tashqi");

        await ProfileWorldBuilder.LinkTelegramAsync(
            factory, withTelegram.Id, ProfileWorldBuilder.NextTelegramId(), "bor_nom");

        await ProfileWorldBuilder.LinkTelegramAsync(
            factory, other.Student.Id, ProfileWorldBuilder.NextTelegramId(), "tashqi_nom");

        var page = await ListAsync(groupId: world.GroupId, telegramLinked: false);

        var ids = page.Items.ConvertAll(u => u.Id);

        ids.Should().ContainSingle().And.Contain(world.Student.Id);
        ids.Should().NotContain(withTelegram.Id, "Telegram bog'langan a'zo chiqmasligi kerak");
        ids.Should().NotContain(other.Student.Id, "boshqa guruh a'zosi chiqmasligi kerak");
    }

    /// <summary>Mavjud filtrlar (rol) yangilari bilan birga ishlaydi.</summary>
    [Fact]
    public async Task GroupFilterWithRoleFilter_NarrowsToStudents()
    {
        var world = await WorldBuilder.CreateAsync(factory, "filtr-rol");

        var page = await ListAsync(groupId: world.GroupId, role: UserRole.Teacher);

        page.Items.Should().BeEmpty("guruh a'zolari — o'quvchilar, ustoz a'zo emas");

        var students = await ListAsync(groupId: world.GroupId, role: UserRole.Student);

        students.Items.ConvertAll(u => u.Id).Should().Contain(world.Student.Id);
    }

    /// <summary>Filtr berilmasa ro'yxat o'zgarmaydi (regressiya himoyasi).</summary>
    [Fact]
    public async Task WithoutNewFilters_ListStillReturnsEveryone()
    {
        var world = await WorldBuilder.CreateAsync(factory, "filtr-yoq");

        var page = await ListAsync();

        page.Items.ConvertAll(u => u.Id).Should().Contain(world.Student.Id);
        page.Total.Should().BeGreaterThan(1);
    }

    // ================================================================= yordamchi

    private async Task<UserListResponse> ListAsync(
        long? groupId = null,
        bool? telegramLinked = null,
        UserRole? role = null,
        int pageSize = 100)
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var query = new List<string>
        {
            "pageSize=" + pageSize.ToString(CultureInfo.InvariantCulture),
        };

        if (groupId is { } id)
            query.Add("groupId=" + id.ToString(CultureInfo.InvariantCulture));

        if (telegramLinked is { } linked)
            query.Add("telegramLinked=" + (linked ? "true" : "false"));

        if (role is { } value)
            query.Add("role=" + value.ToString());

        var response = await admin.GetAsync(
            new Uri("/api/v1/users?" + string.Join('&', query), UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<UserListResponse>())!;
    }
}
