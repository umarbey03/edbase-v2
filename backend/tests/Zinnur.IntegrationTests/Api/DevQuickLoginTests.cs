using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Persistence;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>Ro'yxat javobining bitta qatori.</summary>
internal sealed record QuickAccount(string Role, string RoleLabel, string FullName, string? Phone);

/// <summary>`GET /api/v1/auth/dev/quick-login` javobi.</summary>
internal sealed record QuickList(string Warning, string Environment, IReadOnlyList<QuickAccount> Accounts);

/// <summary>
/// Kalit YOQILGAN fixture (muhit — odatiy <c>Development</c>) + namunaviy
/// ma'lumot. Ikkalasi ham kerak: kalitsiz endpoint yo'q, namunasiz esa
/// kiriladigan hisob yo'q.
/// </summary>
public sealed class DevQuickLoginApiFactory : ZinnurApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        new("Dev:QuickLogin", "true"),
        new(DemoDataSeeder.EnabledKey, "true"),
    ];
}

/// <summary>
/// Kalit YOQILGAN, LEKIN muhit — <c>Production</c>.
///
/// ⚠️ `Bootstrap:AdminPhone` MAJBURIY: prod'da uning standarti ataylab
/// yo'q va usiz `DbInitializer` ishga tushishdayoq yiqilardi.
///
/// ★ NAMUNAVIY MA'LUMOT ATAYLAB YOZILMAYDI: darvoza bazaga TEGISHDAN
///   OLDIN ishlaydi, ya'ni 404 ma'lumot yo'qligidan emas, MUHITDAN
///   kelib chiqadi. (Buni isbotlash oson: kalit yoqilgan-u namuna yo'q
///   Development'da endpoint 404 emas, BO'SH RO'YXAT bilan 200 qaytaradi.)
/// </summary>
public sealed class DevQuickLoginProductionApiFactory : ZinnurApiFactory
{
    protected override string EnvironmentName => "Production";

    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        new("Dev:QuickLogin", "true"),
        new(DbInitializer.AdminPhoneKey, "+998900000001"),
    ];
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 SINOV UCHUN KIRISH — DARVOZALAR TESTI
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN BU TESTLARNING KO'PI "ISHLAYDIMI" EMAS, "ISHLAMAYDIMI"
///   NI TEKSHIRADI: bu endpoint — autentifikatsiyani chetlab o'tish yo'li.
///   Uning ishlashi qulaylik, ISHLAMASLIGI esa XAVFSIZLIK. Buzilganda
///   birinchisi darrov ko'rinadi (tugma ishlamaydi), ikkinchisi esa
///   HECH QACHON ko'rinmaydi — faqat hujumdan keyin. Shuning uchun
///   darvozalarning har biri alohida qulflangan.
/// </summary>
public sealed class DevQuickLoginTests(DevQuickLoginApiFactory factory)
    : IClassFixture<DevQuickLoginApiFactory>
{
    private const string Path = "/api/v1/auth/dev/quick-login";

    // ══════════════════════════════════════════════════════════ ro'yxat

    /// <summary>
    /// Har ROLGA bittadan hisob, loyiha egasi sanagan TARTIBDA.
    ///
    /// ★ TARTIB ham tekshiriladi: interfeys tugmalarni javobdagi tartibda
    ///   chizadi, ya'ni tartib — shartnomaning bir qismi. `UserRole` enum
    ///   tartibi teskari (`Student = 0` … `Admin = 4`), shuning uchun uni
    ///   tasodifan "soddalashtirib" qo'yish oson bo'lardi.
    /// </summary>
    [Fact]
    public async Task List_ReturnsOneAccountPerRole_InOwnersOrder()
    {
        using var client = factory.CreateClient();

        var list = await client.GetFromJsonAsync<QuickList>(Path);

        list!.Accounts.Select(a => a.Role).Should().Equal(
            "Admin", "Academic", "Teacher", "Assistant", "Student");

        list.Accounts.Select(a => a.RoleLabel).Should().Equal(
            "Administrator", "O'quv bo'limi", "Ustoz", "Kurator", "O'quvchi");

        // Loyiha egasiga aytilgan AYNI raqamlar (`DemoWorld` + `DbInitializer`).
        list.Accounts.Select(a => a.Phone).Should().Equal(
            "+998900000001", "+998901110001", "+998901110011",
            "+998901110021", "+998901110101");

        list.Accounts.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.FullName));
    }

    /// <summary>
    /// 🔴 JAVOBNING O'ZI "bu sinov yo'li" deb aytadi.
    ///
    /// Endpointni birinchi ko'rgan odam (yangi dasturchi, auditor,
    /// `curl` bilan qidirayotgan kishi) uni HUJJATDAN emas, JAVOBDAN
    /// ko'radi. Ogohlantirish yo'qolsa u jimgina "yana bitta kirish
    /// endpointi" bo'lib qolardi.
    /// </summary>
    [Fact]
    public async Task List_ResponseCarriesTestOnlyWarning()
    {
        using var client = factory.CreateClient();

        var list = await client.GetFromJsonAsync<QuickList>(Path);

        list!.Warning.Should().Contain("SINOV");
        list.Warning.Should().Contain("Production");
        list.Environment.Should().Be("Development");
    }

    // ══════════════════════════════════════════════════════════ kirish

    /// <summary>
    /// ★★ ASOSIY TEKSHIRUV: berilgan token HAQIQATAN ishlaydi.
    ///
    /// "200 qaytdi" hech nima anglatmaydi — token yaroqsiz bo'lsa ham
    /// javob 200 bo'lardi. Shuning uchun har rol uchun HIMOYALANGAN
    /// endpoint (`/auth/me`) uriladi va u AYNAN o'sha odamni qaytarishi
    /// tekshiriladi.
    /// </summary>
    [Theory]
    [InlineData("Admin", "admin@zinnur.uz")]
    [InlineData("Academic", "academic@zinnur.uz")]
    [InlineData("Teacher", "teacher@zinnur.uz")]
    [InlineData("Assistant", "curator1@zinnur.uz")]
    [InlineData("Student", "student@zinnur.uz")]
    public async Task QuickLogin_ByRole_IssuesWorkingToken(string role, string expectedEmail)
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(Path, new { role });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = await response.Content.ReadFromJsonAsync<AuthTokens>();
        tokens!.User.Role.Should().Be(role);

        using var authorized = factory.CreateAuthorizedClient(tokens.AccessToken);
        var me = await authorized.GetFromJsonAsync<AuthUser>("/api/v1/auth/me");

        me!.Email.Should().Be(expectedEmail,
            "token AYNAN so'ralgan rolning namunaviy hisobiga tegishli bo'lishi kerak");
        me.Id.Should().Be(tokens.User.Id);
    }

    /// <summary>
    /// Berilgan token ODATIY sessiya — `refresh` ham ishlaydi.
    ///
    /// ★ NIMA UCHUN MUHIM: agar sinov yo'li o'z tokenini alohida yasasa,
    ///   u `TokenVersion` ga bog'lanmasdan qolishi mumkin edi — ya'ni
    ///   "chiqish" uni bekor qila olmasdi. Bu test sessiya HAQIQIY
    ///   `AuthService.Build()` dan chiqqanini bilvosita isbotlaydi.
    /// </summary>
    [Fact]
    public async Task QuickLogin_IssuesRefreshableSession()
    {
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync(Path, new { role = "Teacher" });
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokens>();

        var refreshed = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = tokens!.RefreshToken });

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Aniq raqam bilan ham kirsa bo'ladi (`curl` uchun qulay).</summary>
    [Fact]
    public async Task QuickLogin_ByPhone_Works()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Path, new { phone = "+998901110012" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = await response.Content.ReadFromJsonAsync<AuthTokens>();
        tokens!.User.Email.Should().Be("teacher2@zinnur.uz",
            "ro'yxatda ko'rinmaydigan IKKINCHI ustozga ham raqam orqali kirsa bo'ladi");
    }

    // ══════════════════════════════════════════ 3-DARVOZA: faqat namuna

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// 🔴🔴 ENG MUHIM TEST: HAQIQIY MARKAZNING ADMINISTRATORI YETIB
    ///      BO'LMAYDIGAN BO'LIB QOLADI
    /// ════════════════════════════════════════════════════════════════
    ///
    /// Ssenariy: kalit YOQILGAN, muhit Development, ya'ni birinchi
    /// IKKALA darvoza ham OCHIQ. Bazada esa haqiqiy administrator bor —
    /// u `DemoDataSeeder` yozmagan, demak uning `TelegramId` si
    /// namunaviy diapazonda EMAS.
    ///
    /// Bu holat uydirma emas: demo yozilgan stendga keyin haqiqiy
    /// xodimlar qo'shilishi — odatiy yo'l.
    ///
    /// ★ ROL BO'YICHA so'ralganda ham u tanlanmasligi kerak: so'rov
    ///   `Id` bo'yicha saralaydi va haqiqiy admin `Id` si KATTA bo'lsa
    ///   ham, u umuman NOMZODLAR ORASIGA TUSHMAYDI.
    /// </summary>
    [Fact]
    public async Task QuickLogin_RefusesRealAdmin_EvenWhenFeatureIsOn()
    {
        const string realPhone = "+998977776655";

        var realAdminId = await factory.WithDbAsync(async db =>
        {
            var admin = new User
            {
                FullName = "Haqiqiy markaz administratori",
                Email = $"real-admin-{Guid.NewGuid():N}@markaz.uz",
                PasswordHash = "test-uchun-hash",
                Role = UserRole.Admin,
            };

            admin.SetPhone(realPhone);

            // HAQIQIY Telegram ID diapazoni (~10¹⁰) — namunaviydan
            // (7·10¹²) uzoq. Aynan shu farq himoyaning O'ZI.
            admin.LinkTelegram(telegramId: 8_123_456_789L, username: null, DateTimeOffset.UtcNow);

            db.Users.Add(admin);
            await db.SaveChangesAsync();

            return admin.Id;
        });

        using var client = factory.CreateClient();

        // (a) raqami bo'yicha — rad etiladi
        var byPhone = await client.PostAsJsonAsync(Path, new { phone = realPhone });
        byPhone.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "haqiqiy hisob namunaviy diapazonda emas — unga kirish yo'li BO'LMASLIGI kerak");

        // (b) roli bo'yicha — namunaviy admin keladi, haqiqiysi EMAS
        var byRole = await client.PostAsJsonAsync(Path, new { role = "Admin" });
        byRole.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = await byRole.Content.ReadFromJsonAsync<AuthTokens>();
        tokens!.User.Id.Should().NotBe(realAdminId);
        tokens.User.Email.Should().Be(DbInitializer.AdminEmail);

        // (c) ro'yxatda ham ko'rinmaydi
        var list = await client.GetFromJsonAsync<QuickList>(Path);
        list!.Accounts.Should().NotContain(a => a.Phone == realPhone);
    }

    /// <summary>
    /// Telegram'i UMUMAN ulanmagan haqiqiy foydalanuvchi ham rad etiladi.
    ///
    /// ★ ALOHIDA HOLAT: `TelegramId is null` — SQL da `NULL >= x`
    ///   solishtiruvi `false` emas, `UNKNOWN` beradi. Shart noto'g'ri
    ///   yozilsa (masalan `!= null` bilan "soddalashtirilsa") bu qator
    ///   jimgina o'tib ketishi mumkin edi.
    /// </summary>
    [Fact]
    public async Task QuickLogin_RefusesUserWithoutTelegram()
    {
        const string phone = "+998977771122";

        await factory.WithDbAsync(async db =>
        {
            var staff = new User
            {
                FullName = "Telegramsiz xodim",
                Email = $"no-telegram-{Guid.NewGuid():N}@markaz.uz",
                PasswordHash = "test-uchun-hash",
                Role = UserRole.Academic,
            };

            staff.SetPhone(phone);
            db.Users.Add(staff);

            return await db.SaveChangesAsync();
        });

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Path, new { phone });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// 🔴 DIAPAZONNING YUQORI CHEGARASI YOPIQ.
    ///
    /// Shart `>= min` bilan cheklanib qolsa (yuqori chegara unutilsa),
    /// diapazon "7·10¹² dan katta HAMMA narsa" bo'lib qolardi — ya'ni
    /// Telegram ID'lari kelajakda o'sganda himoya jimgina yo'qolardi.
    /// Bu test aynan o'sha unutishni qizartiradi.
    /// </summary>
    [Fact]
    public async Task QuickLogin_RefusesTelegramIdAboveDemoRange()
    {
        const string phone = "+998977773344";

        await factory.WithDbAsync(async db =>
        {
            var user = new User
            {
                FullName = "Diapazondan tashqarida",
                Email = $"above-range-{Guid.NewGuid():N}@markaz.uz",
                PasswordHash = "test-uchun-hash",
                Role = UserRole.Student,
            };

            user.SetPhone(phone);
            user.LinkTelegram(
                DemoDataSeeder.DemoTelegramIdMaxExclusive, username: null, DateTimeOffset.UtcNow);

            db.Users.Add(user);

            return await db.SaveChangesAsync();
        });

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Path, new { phone });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>O'chirilgan namunaviy profilga ham kirib bo'lmaydi.</summary>
    [Fact]
    public async Task QuickLogin_RefusesDeactivatedDemoAccount()
    {
        var phone = await factory.WithDbAsync(async db =>
        {
            var victim = await db.Users
                .Where(u => u.Email == "student12@zinnur.uz")
                .FirstAsync();

            victim.IsActive = false;
            await db.SaveChangesAsync();

            return victim.Phone!;
        });

        try
        {
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync(Path, new { phone });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            // Boshqa testlar AYNI bazani ishlatadi — holatni qaytaramiz.
            await factory.WithDbAsync(async db =>
            {
                var victim = await db.Users.Where(u => u.Email == "student12@zinnur.uz").FirstAsync();
                victim.IsActive = true;
                return await db.SaveChangesAsync();
            });
        }
    }

    // ══════════════════════════════════════════════════════════ so'rov shakli

    [Fact]
    public async Task QuickLogin_WithUnknownRole_IsForbidden()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Path, new { role = "Boshliq" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "noma'lum rol ham AYNI javobni olishi kerak — aks holda javob "
            + "qaysi rollar mavjudligini oshkor qilardi");
    }

    [Fact]
    public async Task QuickLogin_WithEmptyBody_IsBadRequest()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Path, new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 1-DARVOZA: KALIT BERILMAGAN — ENDPOINT UMUMAN YO'Q
/// ════════════════════════════════════════════════════════════════════════
///
/// Odatiy `ZinnurApiFactory` da `Dev:QuickLogin` UMUMAN berilmaydi, ya'ni
/// bu sinf AYNI paytda "standart qiymat `false` mi?" degan savolga ham
/// javob beradi — `DemoSeedDisabledTests` bilan AYNI naqsh.
///
/// ⚠️ Bu test qolgan 1500+ integratsion testni ham qo'riqlaydi: kalit
/// tasodifan yoqilib qolsa, HAR BIR test bazasida parolsiz admin eshigi
/// ochilgan bo'lardi.
/// </summary>
public sealed class DevQuickLoginDisabledTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const string Path = "/api/v1/auth/dev/quick-login";

    [Fact]
    public async Task WithoutSwitch_ListIsNotFound()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(Path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "xususiyat FAQAT oshkor kalit bilan yoqiladi");
    }

    [Fact]
    public async Task WithoutSwitch_LoginIsNotFound()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Path, new { role = "Admin" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// ★ DARVOZA BAZAGA TEGISHDAN OLDIN ISHLAYDI: bu bazada namunaviy
    ///   ma'lumot ham yo'q, ya'ni 404 "ma'lumot topilmadi" degani emas.
    ///   Javob matni AYNAN kalitni ko'rsatishi kerak — dev mashinasida
    ///   uni yoqishni unutgan dasturchi sababni javobdan bilib olsin.
    /// </summary>
    [Fact]
    public async Task WithoutSwitch_ResponseNamesTheSwitch()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(Path, UriKind.Relative));
        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();

        problem!.Detail.Should().Contain("Dev:QuickLogin");
    }
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 2-DARVOZA: `Production` DA KALIT YOQIQ BO'LSA HAM ESHIK OCHILMAYDI
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN BU AYNAN INTEGRATSION TEST: shart `IHostEnvironment` ga
///   tayanadi, ya'ni uni faqat ilovani HAQIQATAN `Production` muhitida
///   ko'tarib tekshirish mumkin. Sinfni "qo'lda" chaqirib tekshirish
///   `Program.cs` dagi ulanishni (DI ro'yxati, startdagi log) umuman
///   qamramasdi.
///
/// 🔴 BU TEST LOYIHA EGASI UCHUN MUHIM YOZUV: uning YANGI SERVERI
///    `ASPNETCORE_ENVIRONMENT=Production` bilan ishlasa, tugmalar
///    KO'RINMAYDI. Bu — nosozlik emas, ataylab qo'yilgan chegara.
/// </summary>
public sealed class DevQuickLoginProductionTests(DevQuickLoginProductionApiFactory factory)
    : IClassFixture<DevQuickLoginProductionApiFactory>
{
    private const string Path = "/api/v1/auth/dev/quick-login";

    [Fact]
    public async Task InProduction_ListIsNotFound_EvenWithSwitchOn()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(Path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InProduction_LoginIsNotFound_EvenWithSwitchOn()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Path, new { role = "Admin" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "muhit sharti kalitni RAD ETADI — ikkalasi mustaqil darvoza");
    }

    /// <summary>
    /// ★ Nazorat tekshiruvi: ilova o'zi TIRIK va so'rovlarga javob
    ///   beryapti. Busiz yuqoridagi ikki 404 ni "konteyner ko'tarilmagan"
    ///   deb ham talqin qilish mumkin bo'lardi.
    /// </summary>
    [Fact]
    public async Task InProduction_RealAuthEndpointsStillWork()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
