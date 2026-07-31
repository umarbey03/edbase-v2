using Microsoft.Extensions.Options;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;

namespace Zinnur.Infrastructure.Options;

/// <summary>
/// <c>Telegram:*</c> — bazadan boshqariladigan qismi bilan birga.
///
/// ★ NIMA UCHUN: bot tokeni o'g'irlanganda uni @BotFather'da bekor qilib,
/// yangisini DARHOL qo'yish kerak. Ilgari bu serverga kirishni va qayta
/// joylashtirishni talab qilardi.
///
/// ★ QAYSI MAYDONLAR BAZADAN, QAYSILARI YO'Q:
///   • BotToken, WebhookSecret, MiniAppUrl, BotUsername — BAZADAN;
///   • ApiBaseUrl — MUHITDAN, va bu XAVFSIZLIK qarori: token so'rov
///     MANZILINING ichida ketadi (`/bot&lt;token&gt;/sendMessage`), ya'ni
///     manzilni bazadan boshqarish panelga kirgan odamga birinchi xabar
///     bilan birga TOKENNI ham berardi;
///   • TimeoutSeconds, InitDataMaxAgeHours — registrda yo'q, `HttpClient`
///     ga ishga tushishda beriladi.
/// </summary>
public sealed class RuntimeTelegramOptions(IRuntimeSettings runtime, IOptions<TelegramOptions> seed)
    : RuntimeOptions<TelegramOptions>(runtime, seed)
{
    protected override TelegramOptions Compose(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new TelegramOptions
        {
            BotToken = snapshot.Value(SettingsRegistry.Keys.TelegramBotToken) ?? string.Empty,
            WebhookSecret = snapshot.Value(SettingsRegistry.Keys.TelegramWebhookSecret) ?? string.Empty,
            MiniAppUrl = snapshot.Value(SettingsRegistry.Keys.TelegramMiniAppUrl) ?? string.Empty,
            BotUsername = snapshot.Value(SettingsRegistry.Keys.TelegramBotUsername) ?? string.Empty,

            ApiBaseUrl = Seed.ApiBaseUrl,
            TimeoutSeconds = Seed.TimeoutSeconds,
            InitDataMaxAgeHours = Seed.InitDataMaxAgeHours,
        };
    }
}
