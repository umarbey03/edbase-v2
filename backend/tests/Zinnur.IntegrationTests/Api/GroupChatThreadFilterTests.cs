using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// R38 — "CHATLAR" RO'YXATI FILTRI (guruh turi va yo'nalishi)
/// ========================================================================
///
/// Talab: *"chatlar qismga ham filter qo'shilishi kerak, guruh tur va
/// kategoriyalar bo'yicha"*.
///
/// ── 🔴 NIMA UCHUN BU TESTLAR INTEGRATSION ──────────────────────────────
///
/// Filtrning butun qiymati SERVER TOMONDA qo'llanishida:
/// <c>GroupChatService.MaxThreads = 200</c> ro'yxatni SARALASHDAN KEYIN
/// kesadi. Mijozdagi filtr faqat o'sha 200 qatorni ko'rardi, ya'ni
/// 201-o'rindagi guruh filtrga to'liq mos kelsa ham natijada UMUMAN
/// chiqmasdi. Bu — ma'lumot yo'qolishi, va uni faqat HAQIQIY so'rov
/// zanjiri (HTTP -> servis -> SQL) tekshira oladi.
///
/// ── ⚠️ KURATOR TURI ────────────────────────────────────────────────────
///
/// <see cref="GroupType.Curator"/> chatda ISTISNO qoidasi: bunday guruh
/// ro'yxatga umuman tushmaydi (<c>AccessibleThreadsAsync</c> ning uchala
/// shoxida). Shu sababli u bo'yicha filtrlash DOIM bo'sh ro'yxat berardi va
/// server uni 400 bilan rad etadi — jimgina bo'sh natija foydalanuvchini
/// "chatlarim yo'qolibdi" degan xulosaga olib kelardi
/// (<see cref="Threads_FilteredByCuratorType_IsRefused"/>).
/// </summary>
public sealed class GroupChatThreadFilterTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ================================================================= ustunlar

    /// <summary>
    /// Qator TUR va YO'NALISHNI olib yuradi. Ular bo'lmasa UI filtrni
    /// ko'rsata olmasdi va tanlangan yo'nalish qatorda ko'rinmasdi.
    /// </summary>
    [Fact]
    public async Task Threads_CarryGroupTypeAndCategory()
    {
        var world = await WorldBuilder.CreateAsync(factory, "r38-ustun");
        var category = await AttachCategoryAsync(world.GroupId, "Ustun");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var threads = await GroupChatApi.ThreadsAsync(teacher);
        var mine = threads.Single(t => t.GroupId == world.GroupId);

        mine.GroupType.Should().Be(nameof(GroupType.Group),
            "API enum'ni SATR ko'rinishida qaytaradi");
        mine.CategoryId.Should().Be(category.Id);
        mine.CategoryName.Should().Be(category.Name);
    }

    /// <summary>Yorliqsiz guruhda ikkala maydon ham <c>null</c> (mavjud guruhlarning holati).</summary>
    [Fact]
    public async Task Threads_WithoutCategory_ReturnNulls()
    {
        var world = await WorldBuilder.CreateAsync(factory, "r38-yorliqsiz");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var mine = (await GroupChatApi.ThreadsAsync(teacher))
            .Single(t => t.GroupId == world.GroupId);

        mine.CategoryId.Should().BeNull();
        mine.CategoryName.Should().BeNull();
    }

    // ================================================================= kategoriya filtri

    /// <summary>
    /// Kategoriya filtri MOS KELMAYDIGAN guruhlarni ro'yxatdan chiqaradi.
    ///
    /// Ikki guruh, ikki xil yo'nalish, BITTA ustoz — ya'ni filtr ruxsat
    /// qoidasidan emas, AYNAN yo'nalishdan kelib chiqib ishlashi tekshiriladi.
    /// </summary>
    [Fact]
    public async Task Threads_FilteredByCategory_HideOtherCategories()
    {
        var world = await WorldBuilder.CreateAsync(factory, "r38-kat");
        var secondGroupId = await GroupChatApi.AddGroupAsync(factory, world, "r38-kat2");

        var wanted = await AttachCategoryAsync(world.GroupId, "Kerakli");
        await AttachCategoryAsync(secondGroupId, "Boshqa");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        // Filtrsiz — ikkalasi ham bor (boshlang'ich holat isbotlanadi).
        var all = await GroupChatApi.ThreadsAsync(teacher);
        all.Should().Contain(t => t.GroupId == world.GroupId);
        all.Should().Contain(t => t.GroupId == secondGroupId);

        var filtered = await GroupChatApi.ThreadsAsync(teacher, categoryId: wanted.Id);

        filtered.Should().Contain(t => t.GroupId == world.GroupId);
        filtered.Should().NotContain(t => t.GroupId == secondGroupId);
        filtered.Should().OnlyContain(t => t.CategoryId == wanted.Id);
    }

    /// <summary>
    /// Yorliqsiz guruh kategoriya filtriga TUSHMAYDI.
    ///
    /// ★ Bu ataylab: "yorliqsizlar" alohida so'rov emas va sun'iy sentinel
    /// (masalan <c>categoryId=0</c>) qo'shilmagan — sabab
    /// <c>GroupListQuery.CategoryId</c> izohida.
    /// </summary>
    [Fact]
    public async Task Threads_FilteredByCategory_ExcludeUnlabelledGroups()
    {
        var world = await WorldBuilder.CreateAsync(factory, "r38-yorliq");
        var labelledId = await GroupChatApi.AddGroupAsync(factory, world, "r38-yorliqli");

        var category = await AttachCategoryAsync(labelledId, "Yorliqli");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var filtered = await GroupChatApi.ThreadsAsync(teacher, categoryId: category.Id);

        filtered.Should().Contain(t => t.GroupId == labelledId);
        filtered.Should().NotContain(t => t.GroupId == world.GroupId,
            "kategoriyasiz guruh yo'nalish filtriga tushmaydi");
    }

    /// <summary>Mavjud bo'lmagan kategoriya — BO'SH ro'yxat, 404 emas (GET ro'yxat qoidasi).</summary>
    [Fact]
    public async Task Threads_FilteredByUnknownCategory_ReturnEmptyList()
    {
        var world = await WorldBuilder.CreateAsync(factory, "r38-yoq");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var filtered = await GroupChatApi.ThreadsAsync(teacher, categoryId: 999_999);

        filtered.Should().BeEmpty();
    }

    // ================================================================= tur filtri

    /// <summary>Tur filtri: <c>Individual</c> so'ralganda oddiy guruh chiqmaydi.</summary>
    [Fact]
    public async Task Threads_FilteredByType_HideOtherTypes()
    {
        var world = await WorldBuilder.CreateAsync(factory, "r38-tur");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var asGroup = await GroupChatApi.ThreadsAsync(teacher, GroupType.Group);
        var asIndividual = await GroupChatApi.ThreadsAsync(teacher, GroupType.Individual);

        asGroup.Should().Contain(t => t.GroupId == world.GroupId);
        asGroup.Should().OnlyContain(t => t.GroupType == nameof(GroupType.Group));

        asIndividual.Should().NotContain(t => t.GroupId == world.GroupId);
    }

    /// <summary>
    /// 🔴 <c>type=Curator</c> — 400, bo'sh ro'yxat EMAS.
    ///
    /// Kurator turidagi guruhning alohida chati yo'q va u ro'yxatga hech
    /// qachon tushmaydi. Jimgina bo'sh natija "chatlarim yo'qolibdi" degan
    /// noto'g'ri xulosaga olib kelardi; aniq xato esa sababni aytadi
    /// (servisning umumiy falsafasi: ruxsat etilmagan kanal ham jimgina
    /// almashtirilmaydi, 403 oladi).
    /// </summary>
    [Fact]
    public async Task Threads_FilteredByCuratorType_IsRefused()
    {
        var world = await WorldBuilder.CreateAsync(factory, "r38-kurator");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.GetAsync(GroupChatApi.ThreadsUrl(GroupType.Curator));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Kurator", "javob SABABNI aytishi kerak");
    }

    // ================================================================= birgalikda

    /// <summary>Ikki filtr BIRGA ishlaydi (VA, YOKI emas).</summary>
    [Fact]
    public async Task Threads_FilteredByTypeAndCategory_ApplyBoth()
    {
        var world = await WorldBuilder.CreateAsync(factory, "r38-ikki");
        var category = await AttachCategoryAsync(world.GroupId, "Ikkala");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var matching = await GroupChatApi.ThreadsAsync(teacher, GroupType.Group, category.Id);
        var mismatching = await GroupChatApi.ThreadsAsync(teacher, GroupType.Individual, category.Id);

        matching.Should().Contain(t => t.GroupId == world.GroupId);
        mismatching.Should().NotContain(t => t.GroupId == world.GroupId,
            "tur mos kelmasa kategoriya mos kelgani YETARLI EMAS");
    }

    /// <summary>
    /// O'QUVCHIDA ham filtr ishlaydi — u boshqa SQL shoxidan o'tadi
    /// (<c>GroupMembers</c> orqali), ya'ni filtrni u yerda unutish oson edi.
    ///
    /// Qo'shimcha tekshiruv: o'quvchida bitta guruh IKKI qator beradi
    /// (Ustoz va Kurator oqimlari) va filtr ularning IKKALASINI ham
    /// qoldirishi kerak — u GURUHGA tegishli, kanalga emas.
    ///
    /// ⚠️ 2026-08-22: o'quvchi endi IKKI guruhda bo'la olmaydi
    /// (<c>GroupChatApi.AddGroupAsync</c> izohi), shuning uchun "ikkinchi
    /// guruh chiqmadi" ni ISBOT sifatida olib bo'lmaydi — u ruxsat
    /// qoidasi bo'yicha ham chiqmasdi. Filtrning KESAYOTGANI shuning
    /// uchun ALOHIDA so'rov bilan tekshiriladi (pastda): o'quvchining O'Z
    /// guruhi MOS KELMAYDIGAN yo'nalish bo'yicha so'ralganda ro'yxat
    /// BO'SH bo'lishi kerak. Aynan shu — filtr o'quvchi shoxida
    /// qo'llanayotganining yagona haqiqiy dalili.
    /// </summary>
    [Fact]
    public async Task Threads_ForAStudent_AreFilteredToo()
    {
        var world = await WorldBuilder.CreateAsync(factory, "r38-oquvchi");
        var secondGroupId = await GroupChatApi.AddGroupAsync(factory, world, "r38-oquvchi2");

        var wanted = await AttachCategoryAsync(world.GroupId, "Talaba");
        var other = await AttachCategoryAsync(secondGroupId, "Talaba2");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var filtered = await GroupChatApi.ThreadsAsync(student, categoryId: wanted.Id);

        filtered.Count(t => t.GroupId == world.GroupId).Should().Be(2,
            "o'quvchida har guruh IKKI oqim beradi va filtr GURUHGA tegishli");

        filtered.Should().NotContain(t => t.GroupId == secondGroupId,
            "o'quvchi a'zo bo'lmagan guruh ro'yxatga umuman tushmaydi");

        // 🔴 FILTR HAQIQATAN KESYAPTIMI — mos kelmaydigan yo'nalish.
        var mismatching = await GroupChatApi.ThreadsAsync(student, categoryId: other.Id);

        mismatching.Should().BeEmpty(
            "o'quvchining yagona guruhi bu yo'nalishga tegishli emas, "
            + "ya'ni filtr uning IKKALA oqimini ham kesishi kerak");
    }

    /// <summary>
    /// ADMIN shoxi ham filtrlanadi. U BARCHA guruhlarni ko'radi, ya'ni
    /// aynan uning ro'yxati `MaxThreads = 200` chegarasiga birinchi bo'lib
    /// yetadi — filtr serverda ekani eng ko'p shu yerda ahamiyatga ega.
    /// </summary>
    [Fact]
    public async Task Threads_ForAnAdmin_AreFilteredToo()
    {
        var world = await WorldBuilder.CreateAsync(factory, "r38-admin");
        var otherId = await GroupChatApi.AddGroupAsync(factory, world, "r38-admin2");

        var wanted = await AttachCategoryAsync(world.GroupId, "Admin");
        await AttachCategoryAsync(otherId, "Admin2");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var filtered = await GroupChatApi.ThreadsAsync(admin, categoryId: wanted.Id);

        filtered.Should().Contain(t => t.GroupId == world.GroupId);
        filtered.Should().NotContain(t => t.GroupId == otherId);
        filtered.Should().OnlyContain(t => t.CategoryId == wanted.Id);
    }

    // ================================================================= yordamchi

    /// <summary>
    /// Yangi kategoriya yaratib, uni guruhga biriktiradi — TO'G'RIDAN-TO'G'RI
    /// bazaga.
    ///
    /// ★ NEGA <c>PUT /groups/{id}</c> ORQALI EMAS: u TO'LIQ ALMASHTIRISH,
    /// ya'ni bu yerda butun jadval qoidasini (sana, kunlar, soat, ustoz,
    /// kurator) qaytadan yig'ib yuborish kerak bo'lardi va bitta maydon
    /// tushib qolsa test SABABSIZ yiqilardi. PUT semantikasining O'ZI
    /// alohida testda tekshirilgan (`GroupCategoryEndpointsTests`).
    /// </summary>
    private Task<CategoryRef> AttachCategoryAsync(long groupId, string prefix) =>
        factory.WithDbAsync(async db =>
        {
            var category = new GroupCategory
            {
                // Baza testlar orasida BO'LISHILADI va nom UNIKAL — takrorlanmas
                // qo'shimcha bo'lmasa ikkinchi test 409 olardi.
                Name = $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}",
            };

            db.GroupCategories.Add(category);
            await db.SaveChangesAsync();

            var group = await db.Groups.FirstAsync(g => g.Id == groupId);
            group.CategoryId = category.Id;
            await db.SaveChangesAsync();

            return new CategoryRef(category.Id, category.Name);
        });

    private sealed record CategoryRef(long Id, string Name);
}
