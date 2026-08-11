using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Enums;

namespace Zinnur.IntegrationTests.Telegram;

/// <summary>
/// ========================================================================
/// TELEGRAM <c>@username</c> — BOG'LANISHDA VA HAR MULOQOTDA YOZILADI
/// ========================================================================
///
/// NIMA UCHUN KERAK: xodim o'quvchi bilan bog'lanishi kerak bo'lganda
/// raqamli `TelegramId` bilan hech nima qila olmaydi — u Telegram
/// qidiruvida ishlamaydi. Username esa bosiladigan havola.
///
/// 🔴 NIMA UCHUN HAR MULOQOTDA QAYTA YOZILADI: username foydalanuvchi
/// tomonidan istalgan payt o'zgartiriladi va bo'shatilgan nom Telegram'da
/// BOSHQA odam tomonidan band qilinishi mumkin. Eskirgan nom profilda
/// turib qolsa, xodim begona odamga yozib qo'yishi mumkin bo'lardi.
///
/// JSON QO'LDA yoziladi: Telegram `snake_case` ishlatadi
/// (`TelegramApiFactory` dagi yordamchilar bilan ayni sabab), va bu yerda
/// bizga `from.username` maydoni kerak.
/// </summary>
public sealed class TelegramUsernameTests(TelegramApiFactory factory)
    : IClassFixture<TelegramApiFactory>
{
    private const string Phone = "+998901112233";

    /// <summary>Kontakt ulashilganda username BIRGA yoziladi.</summary>
    [Fact]
    public async Task ContactShared_StoresUsernameAndLinkTime()
    {
        var userId = await factory.CreateUserAsync(UserRole.Student, Phone);
        var telegramId = TelegramApiFactory.NextUpdateId();

        var response = await factory.PostUpdateAsync(ContactUpdateWithUsername(
            TelegramApiFactory.NextUpdateId(), telegramId, Phone, "ali_valiyev"));

        response.Status.Should().Be(HttpStatusCode.OK, response.Body);

        var state = await StateOfAsync(userId);

        state.TelegramId.Should().Be(telegramId);
        state.TelegramUsername.Should().Be("ali_valiyev");
        state.TelegramLinkedAt.Should().NotBeNull("bog'lanish vaqti ham yozilishi kerak");
    }

    /// <summary>Username'siz akkaunt ham bog'lanadi (maydon <c>null</c> qoladi).</summary>
    [Fact]
    public async Task ContactShared_WithoutUsername_StillLinks()
    {
        var userId = await factory.CreateUserAsync(UserRole.Student, "+998901112244");
        var telegramId = TelegramApiFactory.NextUpdateId();

        await factory.PostUpdateAsync(ContactUpdateWithUsername(
            TelegramApiFactory.NextUpdateId(), telegramId, "+998901112244", username: null));

        var state = await StateOfAsync(userId);

        state.TelegramId.Should().Be(telegramId);
        state.TelegramUsername.Should().BeNull();
    }

    /// <summary>
    /// 🔴 <c>/start</c> — username O'ZGARGAN bo'lsa YANGILANADI.
    ///
    /// Bu "har kirishda yangilanadi" talabining jonli tekshiruvi: bog'lanish
    /// bir marta bo'ladi, muloqot esa har kuni.
    /// </summary>
    [Fact]
    public async Task Start_RefreshesChangedUsername()
    {
        var userId = await factory.CreateUserAsync(UserRole.Student, "+998901112255");
        var telegramId = TelegramApiFactory.NextUpdateId();

        await factory.PostUpdateAsync(ContactUpdateWithUsername(
            TelegramApiFactory.NextUpdateId(), telegramId, "+998901112255", "eski_nom"));

        (await StateOfAsync(userId)).TelegramUsername.Should().Be("eski_nom");

        // Foydalanuvchi Telegram'da nomini o'zgartirdi va botga /start yozdi.
        var response = await factory.PostUpdateAsync(StartUpdateWithUsername(
            TelegramApiFactory.NextUpdateId(), telegramId, "yangi_nom"));

        response.Status.Should().Be(HttpStatusCode.OK, response.Body);

        (await StateOfAsync(userId)).TelegramUsername.Should().Be("yangi_nom",
            "eskirgan nom xodimni boshqa odamga yo'llab qo'ymasligi kerak");
    }

    /// <summary>
    /// Username bo'shatilsa (Telegram'da olib tashlansa) profilda ham
    /// tozalanadi — yo'q nomni ko'rsatib turish yolg'on bo'lardi.
    /// </summary>
    [Fact]
    public async Task Start_ClearsUsernameWhenRemovedOnTelegram()
    {
        var userId = await factory.CreateUserAsync(UserRole.Student, "+998901112266");
        var telegramId = TelegramApiFactory.NextUpdateId();

        await factory.PostUpdateAsync(ContactUpdateWithUsername(
            TelegramApiFactory.NextUpdateId(), telegramId, "+998901112266", "bor_edi"));

        await factory.PostUpdateAsync(StartUpdateWithUsername(
            TelegramApiFactory.NextUpdateId(), telegramId, username: null));

        (await StateOfAsync(userId)).TelegramUsername.Should().BeNull();
    }

    // ================================================================= yordamchi

    private Task<TelegramState> StateOfAsync(long userId) =>
        factory.WithDbAsync(db => db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new TelegramState(u.TelegramId, u.TelegramUsername, u.TelegramLinkedAt))
            .FirstAsync());

    private sealed record TelegramState(
        long? TelegramId, string? TelegramUsername, DateTimeOffset? TelegramLinkedAt);

    private static string ContactUpdateWithUsername(
        long updateId, long fromId, string phone, string? username) =>
        $$"""
        {
          "update_id": {{Num(updateId)}},
          "message": {
            "message_id": 20,
            "from": {
              "id": {{Num(fromId)}}, "is_bot": false, "first_name": "Abbos"
              {{UsernameField(username)}}
            },
            "chat": { "id": {{Num(fromId)}}, "type": "private" },
            "contact": {
              "phone_number": "{{phone}}",
              "first_name": "Abbos",
              "user_id": {{Num(fromId)}}
            }
          }
        }
        """;

    private static string StartUpdateWithUsername(long updateId, long fromId, string? username) =>
        $$"""
        {
          "update_id": {{Num(updateId)}},
          "message": {
            "message_id": 21,
            "from": {
              "id": {{Num(fromId)}}, "is_bot": false, "first_name": "Abbos"
              {{UsernameField(username)}}
            },
            "chat": { "id": {{Num(fromId)}}, "type": "private" },
            "text": "/start"
          }
        }
        """;

    private static string UsernameField(string? username) =>
        username is null ? string.Empty : ", \"username\": \"" + username + "\"";

    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);
}
