using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// GURUH KATEGORIYALARI (R21b) — HAQIQIY baza bilan
/// ========================================================================
///
/// Bu yerdagi eng qimmat uchta tekshiruv:
///
///  1) <see cref="Delete_WhenGroupsAreAttached_IsRefused"/> — bazadagi FK
///     <c>ON DELETE SET NULL</c>, ya'ni to'siq bo'lmasa o'chirish JIMGINA
///     muvaffaqiyatli tugab, o'nlab guruh yorlig'ini yo'qotardi. Buni
///     unit test ushlay olmaydi: u AYNAN baza xatti-harakati haqida.
///
///  2) <see cref="Update_DoesNotDropTheCategoryWhenItIsResent"/> —
///     <c>PUT /groups/{id}</c> TO'LIQ ALMASHTIRISH. Bu test kategoriya
///     shu semantikaga TO'G'RI ulanganini qo'riqlaydi: qaytarib
///     yuborilganda SAQLANADI, yuborilmaganda esa TOZALANADI (ikkinchisi —
///     kutilgan xulq, lekin frontend uni `buildPayload` bilan yopgan).
///
///  3) <see cref="Create_WithDuplicateNameIgnoringCase_IsRefused"/> —
///     "IELTS" va "ielts" bitta yo'nalish. Bazadagi unikal indeks
///     registrga SEZGIR, ya'ni bu qoida faqat servisda yashaydi va
///     testsiz jimgina yo'qolishi mumkin.
/// </summary>
public sealed class GroupCategoryEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const string Base = "/api/v1/group-categories";

    // ================================================================= CRUD

    [Fact]
    public async Task Create_ReturnsTheCategoryWithZeroGroups()
    {
        using var admin = await AdminClientAsync();

        var created = await CreateCategoryAsync(admin, Unique("IELTS"));

        created.IsActive.Should().BeTrue();
        created.GroupCount.Should().Be(0, "yangi yo'nalishga hali guruh biriktirilmagan");
    }

    [Fact]
    public async Task List_ReturnsTheCreatedCategory()
    {
        using var admin = await AdminClientAsync();

        var created = await CreateCategoryAsync(admin, Unique("CEFR"));

        var all = await ListAsync(admin);

        all.Should().Contain(c => c.Id == created.Id);
    }

    /// <summary>
    /// <c>?isActive=true</c> — tanlagichlar uchun filtr. Arxivlangan
    /// yo'nalish yangi guruhga taklif qilinmasligi kerak.
    /// </summary>
    [Fact]
    public async Task List_WithActiveFilter_HidesArchivedCategories()
    {
        using var admin = await AdminClientAsync();

        var created = await CreateCategoryAsync(admin, Unique("Arxiv"));
        await UpdateCategoryAsync(admin, created.Id, created.Name, isActive: false);

        var active = await ListAsync(admin, isActive: true);
        var all = await ListAsync(admin);

        active.Should().NotContain(c => c.Id == created.Id);
        all.Should().Contain(c => c.Id == created.Id, "filtrsiz ro'yxatda u ko'rinadi");
    }

    /// <summary>Registrsiz takror — "IELTS" va "ielts" bitta yo'nalish.</summary>
    [Fact]
    public async Task Create_WithDuplicateNameIgnoringCase_IsRefused()
    {
        using var admin = await AdminClientAsync();

        var name = Unique("Grammatika");
        await CreateCategoryAsync(admin, name);

#pragma warning disable CA1308 // Test ATAYLAB kichik harfga o'giradi (registrsizlikni tekshiradi).
        var duplicate = await admin.PostAsJsonAsync(
            Base, new { name = name.ToLowerInvariant(), isActive = true });
#pragma warning restore CA1308

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "ikkita bir xil ko'ringan yo'nalish guruhlarni ikkiga bo'lib yuborardi");
    }

    [Fact]
    public async Task Create_WithBlankName_IsRefused()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(Base, new { name = "   ", isActive = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_WhenNoGroupsAreAttached_Succeeds()
    {
        using var admin = await AdminClientAsync();

        var created = await CreateCategoryAsync(admin, Unique("Vaqtinchalik"));

        var response = await admin.DeleteAsync(new Uri($"{Base}/{created.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var all = await ListAsync(admin);
        all.Should().NotContain(c => c.Id == created.Id);
    }

    /// <summary>
    /// 🔴 ENG MUHIM TEST. FK <c>SET NULL</c> bo'lgani uchun to'siqsiz
    /// o'chirish MUVAFFAQIYATLI tugab, guruhlarni jimgina yorliqsiz
    /// qoldirardi.
    /// </summary>
    [Fact]
    public async Task Delete_WhenGroupsAreAttached_IsRefused()
    {
        using var admin = await AdminClientAsync();

        var category = await CreateCategoryAsync(admin, Unique("Band"));
        var group = await CreateGroupAsync(admin, category.Id);

        var response = await admin.DeleteAsync(new Uri($"{Base}/{category.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Javob CHIQISH YO'LINI aytishi kerak — aks holda foydalanuvchi
        // "o'chirib bo'lmaydi" degan devorga urilib, nima qilishni bilmasdi.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ARXIVLANG");

        // Guruh baribir joyida va yorlig'i saqlangan.
        var reloaded = await GetGroupAsync(admin, group.Id);
        reloaded.CategoryId.Should().Be(category.Id);
    }

    /// <summary>Kategoriyasi bo'lmagan guruh ham yaratiladi (33 ta mavjud guruh holati).</summary>
    [Fact]
    public async Task CreateGroup_WithoutCategory_Succeeds()
    {
        using var admin = await AdminClientAsync();

        var group = await CreateGroupAsync(admin, categoryId: null);

        group.CategoryId.Should().BeNull();
        group.CategoryName.Should().BeNull();
    }

    [Fact]
    public async Task CreateGroup_WithUnknownCategory_IsRefused()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/groups", GroupPayload(categoryId: 999_999));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Kategoriya nomi guruh DTO'sida JOIN bilan keladi (N+1 yo'q).</summary>
    [Fact]
    public async Task CreateGroup_WithCategory_ReturnsTheCategoryName()
    {
        using var admin = await AdminClientAsync();

        var category = await CreateCategoryAsync(admin, Unique("ATF"));
        var group = await CreateGroupAsync(admin, category.Id);

        group.CategoryId.Should().Be(category.Id);
        group.CategoryName.Should().Be(category.Name);
    }

    // ================================================================= PUT semantikasi

    /// <summary>
    /// 🔴 <c>PUT /groups/{id}</c> — TO'LIQ ALMASHTIRISH.
    ///
    /// Ikkala tomon ham tekshiriladi:
    ///   • qaytarib yuborilgan `categoryId` SAQLANADI;
    ///   • yuborilmagan `categoryId` TOZALANADI — bu kutilgan xulq, va
    ///     aynan shu sabab frontend payloadni UCHALA bo'limdan yig'adi
    ///     (`group-sections.ts: buildPayload`). Bu test o'sha to'siqning
    ///     NEGA kerakligini kod bilan hujjatlaydi.
    /// </summary>
    [Fact]
    public async Task Update_DoesNotDropTheCategoryWhenItIsResent()
    {
        using var admin = await AdminClientAsync();

        var category = await CreateCategoryAsync(admin, Unique("Saqlanadi"));
        var group = await CreateGroupAsync(admin, category.Id);

        var kept = await UpdateGroupAsync(admin, group.Id, GroupPayload(category.Id, group.Name));

        kept.CategoryId.Should().Be(category.Id, "qaytarib yuborilgan yorliq saqlanadi");
    }

    [Fact]
    public async Task Update_ClearsTheCategoryWhenItIsOmitted()
    {
        using var admin = await AdminClientAsync();

        var category = await CreateCategoryAsync(admin, Unique("Tushadi"));
        var group = await CreateGroupAsync(admin, category.Id);

        var cleared = await UpdateGroupAsync(admin, group.Id, GroupPayload(null, group.Name));

        cleared.CategoryId.Should().BeNull(
            "PUT — to'liq almashtirish: yuborilmagan maydon `null` ga tushadi. "
            + "Frontendda bu `buildPayload` bilan yopilgan.");
    }

    /// <summary>
    /// ARXIVLANGAN kategoriya bilan guruhni SAQLASH mumkin bo'lishi SHART.
    ///
    /// Aks holda yo'nalish arxivlangan zahoti undagi HAR BIR guruh
    /// tahrirlab bo'lmaydigan holatga tushardi: forma joriy qiymatni
    /// qaytarib yuboradi va 400 olardi.
    /// </summary>
    [Fact]
    public async Task Update_WithArchivedCategory_IsStillAllowed()
    {
        using var admin = await AdminClientAsync();

        var category = await CreateCategoryAsync(admin, Unique("Arxivlanadi"));
        var group = await CreateGroupAsync(admin, category.Id);

        await UpdateCategoryAsync(admin, category.Id, category.Name, isActive: false);

        var saved = await UpdateGroupAsync(admin, group.Id, GroupPayload(category.Id, group.Name));

        saved.CategoryId.Should().Be(category.Id);
    }

    // ================================================================= ro'yxat filtri

    /// <summary>
    /// <c>GET /groups?categoryId=</c> — R21b filtri. SERVERDA: ro'yxat
    /// sahifalangan, ya'ni mijozdagi filtr faqat joriy sahifani ko'rardi.
    /// </summary>
    [Fact]
    public async Task GroupList_FiltersByCategory()
    {
        using var admin = await AdminClientAsync();

        var wanted = await CreateCategoryAsync(admin, Unique("Kerakli"));
        var other = await CreateCategoryAsync(admin, Unique("Boshqa"));

        var inWanted = await CreateGroupAsync(admin, wanted.Id);
        var inOther = await CreateGroupAsync(admin, other.Id);

        var page = await admin.GetFromJsonAsync<PagedGroups>(
            $"/api/v1/groups?pageSize=100&categoryId={wanted.Id}");

        page.Should().NotBeNull();
        page!.Items.Should().Contain(g => g.Id == inWanted.Id);
        page.Items.Should().NotContain(g => g.Id == inOther.Id);
        page.Items.Should().OnlyContain(g => g.CategoryId == wanted.Id);
    }

    // ================================================================= ruxsat

    /// <summary>Ustoz lug'atni O'QIY oladi — filtr tanlagichi shundan to'ladi.</summary>
    [Fact]
    public async Task List_IsReadableByATeacher()
    {
        using var admin = await AdminClientAsync();
        var teacher = await CreateUserAsync(admin, UserRole.Teacher);

        using var client = await ClientAsync(teacher);

        var response = await client.GetAsync(new Uri(Base, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Ustoz lug'atni O'ZGARTIRA olmaydi — markaz yo'nalishlari uning ishi emas.</summary>
    [Fact]
    public async Task Create_IsRefusedForATeacher()
    {
        using var admin = await AdminClientAsync();
        var teacher = await CreateUserAsync(admin, UserRole.Teacher);

        using var client = await ClientAsync(teacher);

        var response = await client.PostAsJsonAsync(
            Base, new { name = Unique("Ruxsatsiz"), isActive = true });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>O'quvchi lug'atga UMUMAN kira olmaydi (sinf darvozasi).</summary>
    [Fact]
    public async Task List_IsRefusedForAStudent()
    {
        using var admin = await AdminClientAsync();
        var student = await CreateUserAsync(admin, UserRole.Student);

        using var client = await ClientAsync(student);

        var response = await client.GetAsync(new Uri(Base, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================= yordamchi

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private async Task<HttpClient> ClientAsync(TestUserRef user)
    {
        var tokens = await factory.LoginAsync(user.Email);
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    /// <summary>
    /// Boshqa testlar bilan TO'QNASHMAYDIGAN nom: baza testlar orasida
    /// BO'LISHILADI va "IELTS" kabi haqiqiy so'z takror xatosini berardi.
    /// </summary>
    private static string Unique(string prefix) =>
        $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}";

    private static async Task<CategoryResponse> CreateCategoryAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(Base, new { name, isActive = true });

        response.StatusCode.Should().Be(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    private static async Task<CategoryResponse> UpdateCategoryAsync(
        HttpClient client, long id, string name, bool isActive)
    {
        var response = await client.PutAsJsonAsync($"{Base}/{id}", new { name, isActive });

        response.StatusCode.Should().Be(
            HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    private static async Task<IReadOnlyList<CategoryResponse>> ListAsync(
        HttpClient client, bool? isActive = null)
    {
        var url = isActive is { } value ? $"{Base}?isActive={value}" : Base;

        return (await client.GetFromJsonAsync<IReadOnlyList<CategoryResponse>>(url))!;
    }

    /// <summary>
    /// Guruh so'rov tanasi.
    ///
    /// ⚠️ <paramref name="categoryId"/> <c>null</c> bo'lsa maydon UMUMAN
    /// YUBORILMAYDI (anonim tur ichida `null` bo'lib ketadi, ya'ni JSON'da
    /// `"categoryId": null` bo'ladi) — ikkalasi ham server uchun BIR XIL
    /// natija beradi va PUT testi aynan shunga tayanadi.
    /// </summary>
    private static object GroupPayload(long? categoryId, string? name = null) => new
    {
        name = name ?? Unique("KAT-GURUH"),
        startDate = "2026-01-05",
        weekdays = new[] { "Monday", "Wednesday" },
        startTime = "19:00:00",
        type = nameof(GroupType.Group),
        durationMinutes = 80,

        // 1 oy — jadval kichik bo'lsin (`WorldBuilder` dagi bilan AYNI sabab).
        courseMonths = 1,
        categoryId,
        isActive = true,
    };

    private static async Task<GroupBrief> CreateGroupAsync(HttpClient client, long? categoryId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/groups", GroupPayload(categoryId));

        response.StatusCode.Should().Be(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CreatedGroupResponse>())!.Group;
    }

    private static async Task<GroupBrief> UpdateGroupAsync(
        HttpClient client, long id, object payload)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/groups/{id}", payload);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<UpdatedGroupResponse>())!.Group;
    }

    private static async Task<GroupBrief> GetGroupAsync(HttpClient client, long id) =>
        (await client.GetFromJsonAsync<GroupBrief>($"/api/v1/groups/{id}"))!;

    private static async Task<TestUserRef> CreateUserAsync(HttpClient client, UserRole role)
    {
        var email = $"gc-{Guid.NewGuid():N}"[..16] + "@zinnur.uz";

        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            fullName = "Kategoriya " + role.ToString(),
            email,
            role = role.ToString(),

            // 🔴 Xodim uchun telefon MAJBURIY (2026-08-13) — izoh `TestPhones` da.
            phone = TestPhones.Next(),
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var created = (await response.Content.ReadFromJsonAsync<CreatedUserResponse>())!;
        return new TestUserRef(created.User.Id, email);
    }

    // ---------------------------------------------------------------- javob shakllari

    private sealed record CategoryResponse(
        long Id, string Name, int Position, bool IsActive, int GroupCount);

    private sealed record CreatedGroupResponse(GroupBrief Group);

    private sealed record UpdatedGroupResponse(GroupBrief Group);

    private sealed record GroupBrief(long Id, string Name, long? CategoryId, string? CategoryName);

    private sealed record PagedGroups(List<GroupBrief> Items, int Total);

    private sealed record CreatedUserResponse(UserRef User);

    private sealed record UserRef(long Id);

    private sealed record TestUserRef(long Id, string Email);
}
