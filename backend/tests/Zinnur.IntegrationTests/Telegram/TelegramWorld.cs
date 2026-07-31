using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;
using Zinnur.WebApi.Controllers;

namespace Zinnur.IntegrationTests.Telegram;

/// <summary>
/// Telegram testlari uchun umumiy fixture va yordamchilar.
///
/// ── NIMA UCHUN HAQIQIY BOT TOKENI KERAK EMAS ──────────────────────────
/// Bu yerdagi hamma narsa — imzo, sir va bog'lash mantig'i — BIZNING
/// tomonimizda hisoblanadi. Telegram serveriga chiqish faqat xabar
/// YUBORISHDA kerak, u esa `Notifications:Enabled=false` bilan o'chirilgan.
/// Shu tufayli testlar tarmoqsiz, to'liq takrorlanadigan holda ishlaydi.
///
/// `Telegram:ApiBaseUrl` ATAYLAB "hech kim eshitmaydigan" portga qaratilgan:
/// agar kimdir kelajakda yuborishni tasodifan yoqib qo'ysa, test tashqi
/// tarmoqqa chiqib ketmasin.
/// </summary>
public class TelegramApiFactory : ZinnurApiFactory
{
    /// <summary>Testdagi bot tokeni — `initData` imzosi AYNAN shundan yasaladi.</summary>
    public const string BotToken = "123456789:AAH-integration-test-bot-token-xyz";

    /// <summary>Webhook siri (Telegram ruxsat etgan belgilar: A-Za-z0-9_-).</summary>
    public const string WebhookSecret = "zinnur_integration_webhook_secret_2026";

    /// <summary>Mini App manzili — `web_app` tugmasi uchun HTTPS majburiy.</summary>
    public const string MiniAppUrl = "https://app.zinnur.test/tg";

    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        new("Telegram:BotToken", BotToken),
        new("Telegram:WebhookSecret", WebhookSecret),
        new("Telegram:MiniAppUrl", MiniAppUrl),
        new("Telegram:BotUsername", "zinnur_test_bot"),

        // 9-port ("discard") — hech qachon javob bermaydi.
        new("Telegram:ApiBaseUrl", "http://127.0.0.1:9"),
        new("Telegram:TimeoutSeconds", "3"),

        // Fon worker'i O'CHIQ: testlar navbatga YOZILGANINI tekshiradi,
        // yuborishni emas (u `OutboxDispatchTests` da sinalgan).
        new("Notifications:Enabled", "false"),
    ];

    // ---------------------------------------------------------------- foydalanuvchi

    /// <summary>
    /// Bazaga to'g'ridan-to'g'ri foydalanuvchi yozadi.
    ///
    /// NIMA UCHUN API ORQALI EMAS: bizga ANIQ holatlar kerak — telefonsiz,
    /// oldindan bog'langan Telegram bilan, o'chirilgan profil. Ularning
    /// bir qismini API umuman yarata olmaydi (`TelegramId` ni admin
    /// o'rnata olmaydi — bu ATAYLAB shunday).
    /// </summary>
    public Task<long> CreateUserAsync(
        UserRole role,
        string? rawPhone = null,
        bool isActive = true,
        long? telegramId = null,
        string? fullName = null) =>
        WithDbAsync(async db =>
        {
            var user = new User
            {
                FullName = fullName ?? $"Test {role}",
                Email = $"tg-{Guid.NewGuid():N}@zinnur.test",

                // Haqiqiy ko'rinishdagi BCrypt hash (parol bilan kirish bu
                // testlarda kerak emas).
                PasswordHash = "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy",
                Role = role,
                IsActive = isActive,
                TelegramId = telegramId,
            };

            user.SetPhone(rawPhone);

            db.Users.Add(user);
            await db.SaveChangesAsync();

            return user.Id;
        });

    /// <summary>Foydalanuvchining bazadagi Telegram ID'si.</summary>
    public Task<long?> TelegramIdOfAsync(long userId) =>
        WithDbAsync(db => db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TelegramId)
            .FirstAsync());

    // ---------------------------------------------------------------- navbat

    /// <summary>Navbatga tushgan javob xabarining turi (<c>TemplateKey</c>).</summary>
    public Task<string?> QueuedTemplateAsync(long updateId) =>
        WithDbAsync(async db =>
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"tg_update:{updateId}");

            return await db.MessageOutbox
                .AsNoTracking()
                .Where(m => m.IdempotencyKey == key)
                .Select(m => m.TemplateKey)
                .FirstOrDefaultAsync();
        });

    /// <summary>Shu yangilanish uchun navbatga tushgan xabarlar soni.</summary>
    public Task<int> QueuedCountAsync(long updateId) =>
        WithDbAsync(db =>
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"tg_update:{updateId}");

            return db.MessageOutbox.CountAsync(m => m.IdempotencyKey == key);
        });

    // ---------------------------------------------------------------- webhook

    /// <summary>Webhook'ga xom JSON yuboradi.</summary>
    public async Task<WebhookResponse> PostUpdateAsync(string json, string? secret = WebhookSecret)
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/telegram/webhook")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        if (secret is not null)
            request.Headers.TryAddWithoutValidation(TelegramController.SecretHeader, secret);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        return new WebhookResponse(response.StatusCode, body);
    }

    /// <summary>Mini App kirish so'rovi.</summary>
    public async Task<WebhookResponse> PostMiniAppAuthAsync(string? initData)
    {
        using var client = CreateClient();

        var payload = JsonSerializer.Serialize(new { initData });

        using var response = await client.PostAsync(
            new Uri("/api/v1/telegram/mini-app/auth", UriKind.Relative),
            new StringContent(payload, Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();

        return new WebhookResponse(response.StatusCode, body);
    }

    // ---------------------------------------------------------------- update JSON
    //
    // JSON QO'LDA yoziladi: Telegram `snake_case` ishlatadi, anonim
    // obyektlar esa bizning global camelCase sozlamamiz bilan
    // serializatsiya bo'lardi va webhook hech narsa ko'rmasdi.

    /// <summary>Kontakt ulashilgan xabar.</summary>
    public static string ContactUpdate(
        long updateId,
        long fromId,
        string phone,
        long? contactUserId,
        long? chatId = null,
        string chatType = "private") =>
        $$"""
        {
          "update_id": {{updateId.ToString(CultureInfo.InvariantCulture)}},
          "message": {
            "message_id": 10,
            "from": { "id": {{fromId.ToString(CultureInfo.InvariantCulture)}}, "is_bot": false, "first_name": "Abbos" },
            "chat": { "id": {{(chatId ?? fromId).ToString(CultureInfo.InvariantCulture)}}, "type": "{{chatType}}" },
            "contact": {
              "phone_number": "{{phone}}",
              "first_name": "Abbos"
              {{(contactUserId is null
                  ? string.Empty
                  : ", \"user_id\": " + contactUserId.Value.ToString(CultureInfo.InvariantCulture))}}
            }
          }
        }
        """;

    /// <summary>Matnli xabar (<c>/start</c> yoki oddiy matn).</summary>
    public static string TextUpdate(long updateId, long fromId, string text) =>
        $$"""
        {
          "update_id": {{updateId.ToString(CultureInfo.InvariantCulture)}},
          "message": {
            "message_id": 11,
            "from": { "id": {{fromId.ToString(CultureInfo.InvariantCulture)}}, "is_bot": false, "first_name": "Abbos" },
            "chat": { "id": {{fromId.ToString(CultureInfo.InvariantCulture)}}, "type": "private" },
            "text": "{{text}}"
          }
        }
        """;

    /// <summary>Biz tushunmaydigan yangilanish (kanal posti).</summary>
    public static string UnknownUpdate(long updateId) =>
        $$"""
        {
          "update_id": {{updateId.ToString(CultureInfo.InvariantCulture)}},
          "channel_post": { "message_id": 1, "text": "salom" }
        }
        """;

    /// <summary>Har test uchun betakror <c>update_id</c>.</summary>
    public static long NextUpdateId() => Interlocked.Increment(ref _updateId);

    private static long _updateId = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;

    // ---------------------------------------------------------------- initData
    //
    // Imzo MUSTAQIL yasaladi (ilova kodidan foydalanilmaydi) — algoritm
    // buzilsa test darhol qizaradi.

    /// <summary>
    /// Haqiqiy `initData` yasaydi:
    /// <c>secret_key = HMAC_SHA256("WebAppData", bot_token)</c>.
    /// </summary>
    public static string BuildInitData(
        long telegramUserId,
        DateTimeOffset? authDate = null,
        string botToken = BotToken)
    {
        var when = authDate ?? DateTimeOffset.UtcNow;

        var user = string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"id":{{telegramUserId}},"first_name":"Abbos","is_bot":false}""");

        List<KeyValuePair<string, string>> fields =
        [
            new("auth_date", when.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new("query_id", "AAHtest"),
            new("user", user),
        ];

        var dataCheckString = string.Join('\n', fields
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{f.Key}={f.Value}"));

        var secret = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));

        var hash = Convert
            .ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(dataCheckString)))
            .ToLowerInvariant();

        var query = string.Join('&', fields.Select(f =>
            $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));

        return query + "&hash=" + hash;
    }
}

/// <summary>Webhook / Mini App javobi.</summary>
public sealed record WebhookResponse(HttpStatusCode Status, string Body)
{
    /// <summary>Bir marta yaratiladi (CA1869: har chaqiruvda yangi obyekt qimmat).</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Javob tanasini kutilgan turga o'giradi.</summary>
    public T? As<T>() =>
        string.IsNullOrWhiteSpace(Body) ? default : JsonSerializer.Deserialize<T>(Body, JsonOptions);

    /// <summary>Javobdagi <c>outcome</c> maydoni (webhook uchun).</summary>
    public string? Outcome
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Body)) return null;

            try
            {
                using var document = JsonDocument.Parse(Body);

                return document.RootElement.TryGetProperty("outcome", out var value)
                    ? value.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
