using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Telegram;

/// <summary>
/// Mini App kirishi: <c>initData</c> imzosi → MAVJUD auth oqimi → JWT.
///
/// ★ IKKINCHI AUTH YO'LI YARATILMAGANINING DALILI: bu yerda olingan
/// kirish tokeni ODATIY endpointlarda (<c>/api/v1/auth/me</c>) ishlashi
/// tekshiriladi — ya'ni token AYNI mexanizm bilan yasalgan.
/// </summary>
public sealed class TelegramMiniAppAuthTests(TelegramApiFactory factory)
    : IClassFixture<TelegramApiFactory>
{
    [Fact]
    public async Task MiniApp_WithLinkedStudent_ReturnsTokens()
    {
        var telegramId = NewTelegramId();
        await factory.CreateUserAsync(
            UserRole.Student, "+998902220001", telegramId: telegramId, fullName: "Mini App O'quvchi");

        var response = await factory.PostMiniAppAuthAsync(
            TelegramApiFactory.BuildInitData(telegramId));

        response.Status.Should().Be(HttpStatusCode.OK, response.Body);

        var tokens = response.As<AuthTokens>();

        tokens!.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.User.Role.Should().Be("Student");
    }

    /// <summary>Token MAVJUD endpointlarda ishlashi kerak — parallel auth yo'q.</summary>
    [Fact]
    public async Task MiniApp_IssuedToken_WorksOnRegularEndpoints()
    {
        var telegramId = NewTelegramId();
        await factory.CreateUserAsync(UserRole.Student, "+998902220002", telegramId: telegramId);

        var response = await factory.PostMiniAppAuthAsync(
            TelegramApiFactory.BuildInitData(telegramId));

        response.Status.Should().Be(HttpStatusCode.OK, response.Body);

        var tokens = response.As<AuthTokens>()!;

        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);
        var me = await client.GetFromJsonAsync<AuthUser>("/api/v1/auth/me");

        me!.Role.Should().Be("Student");
    }

    // ================================================================= rad etish

    [Fact]
    public async Task MiniApp_WithTamperedSignature_IsUnauthorized()
    {
        var telegramId = NewTelegramId();
        await factory.CreateUserAsync(UserRole.Student, "+998902220003", telegramId: telegramId);

        var initData = TelegramApiFactory.BuildInitData(telegramId);
        var broken = initData[..^1] + (initData[^1] == 'a' ? 'b' : 'a');

        var response = await factory.PostMiniAppAuthAsync(broken);

        response.Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Boshqa bot tokeni bilan imzolangan `initData` — ya'ni hujumchi o'z
    /// botini yasab, bizning API'ga urinmoqda.
    /// </summary>
    [Fact]
    public async Task MiniApp_SignedWithForeignBotToken_IsUnauthorized()
    {
        var telegramId = NewTelegramId();
        await factory.CreateUserAsync(UserRole.Student, "+998902220004", telegramId: telegramId);

        var initData = TelegramApiFactory.BuildInitData(
            telegramId, botToken: "999999999:ZZZ-hujumchining-boti-qqqqqqqqq");

        (await factory.PostMiniAppAuthAsync(initData)).Status
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MiniApp_WithExpiredInitData_IsUnauthorized()
    {
        var telegramId = NewTelegramId();
        await factory.CreateUserAsync(UserRole.Student, "+998902220005", telegramId: telegramId);

        var initData = TelegramApiFactory.BuildInitData(
            telegramId, authDate: DateTimeOffset.UtcNow.AddHours(-25));

        (await factory.PostMiniAppAuthAsync(initData)).Status
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("salom")]
    public async Task MiniApp_WithGarbageInitData_IsUnauthorized(string? initData)
    {
        (await factory.PostMiniAppAuthAsync(initData)).Status
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Imzo TO'G'RI, lekin bu Telegram akkaunt hech kimga bog'lanmagan.
    /// 401 EMAS, 409 — klient "avval botda raqamingizni ulashing" ekranini
    /// ochishi kerak.
    /// </summary>
    [Fact]
    public async Task MiniApp_WhenNotLinked_ReturnsConflict()
    {
        var response = await factory.PostMiniAppAuthAsync(
            TelegramApiFactory.BuildInitData(NewTelegramId()));

        response.Status.Should().Be(HttpStatusCode.Conflict);

        // Apostrof JSON'da `'` bo'lib chiqadi — shuning uchun
        // tekshiruv apostrofsiz so'zlar bo'yicha.
        response.Body.Should().Contain("Avval botda telefon raqamingizni ulashing");
    }

    /// <summary>
    /// ★★ XODIM TELEGRAM ORQALI KIRA OLMAYDI.
    ///
    /// Hatto bog'lash bosqichi biror yo'l bilan chetlab o'tilgan (bazaga
    /// qo'lda yozilgan) taqdirda ham token BERILMAYDI — bu ikkinchi,
    /// mustaqil to'siq.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Academic)]
    [InlineData(UserRole.Teacher)]
    [InlineData(UserRole.Assistant)]
    public async Task MiniApp_WithStaffAccount_IsForbidden(UserRole role)
    {
        var telegramId = NewTelegramId();
        await factory.CreateUserAsync(role, telegramId: telegramId);

        var response = await factory.PostMiniAppAuthAsync(
            TelegramApiFactory.BuildInitData(telegramId));

        response.Status.Should().Be(HttpStatusCode.Forbidden,
            "Telegram kanali orqali xodim roli HECH QACHON berilmaydi");
    }

    [Fact]
    public async Task MiniApp_WithInactiveStudent_IsForbidden()
    {
        var telegramId = NewTelegramId();
        await factory.CreateUserAsync(
            UserRole.Student, "+998902220006", isActive: false, telegramId: telegramId);

        (await factory.PostMiniAppAuthAsync(TelegramApiFactory.BuildInitData(telegramId)))
            .Status.Should().Be(HttpStatusCode.Forbidden);
    }

    private static long NewTelegramId() =>
        7_100_000_000L + TelegramApiFactory.NextUpdateId() % 900_000_000L;
}
