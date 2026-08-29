using System.Globalization;
using Zinnur.Application.Telegram.Services;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="ITelegramBotLink"/> portining amalga oshirilishi.
///
/// ★★ NOM HAR CHAQIRUVDA QAYTA O'QILADI
/// (<see cref="IRuntimeOptions{TOptions}"/>, <c>IOptions&lt;T&gt;</c> EMAS)
/// — <see cref="TelegramInitDataValidator"/> bilan AYNI sabab: qiymat
/// panelda almashtiriladi (bot ko'chirilishi, nom o'zgarishi) va eski
/// nom ushlab qolinsa, kirish havolasi MAVJUD BO'LMAGAN botga olib
/// borardi. Buni hech qanday xato ham ko'rsatmasdi: Telegram shunchaki
/// "bunday foydalanuvchi yo'q" deydi.
///
/// HOLATSIZ — Singleton.
/// </summary>
public sealed class TelegramBotLink(IRuntimeOptions<TelegramOptions> options) : ITelegramBotLink
{
    /// <inheritdoc />
    public bool IsConfigured => Username.Length > 0;

    /// <inheritdoc />
    public string? DeepLink(string payload)
    {
        var username = Username;

        if (username.Length == 0)
            return null;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"https://t.me/{username}?start={Uri.EscapeDataString(payload ?? string.Empty)}");
    }

    /// <summary>
    /// Sozlamadagi nom — bosh/oxirgi bo'shliqlarsiz va boshidagi
    /// <c>@</c> siz.
    ///
    /// ★ <c>@</c> ATAYLAB OLIB TASHLANADI: sozlamani to'ldiradigan odam
    /// uni Telegram'da ko'rgani kabi (<c>@zinnur_bot</c>) yozishi tabiiy,
    /// lekin <c>t.me/@zinnur_bot</c> — buzilgan havola. Bitta belgi
    /// butun kirish oqimini o'chirib qo'yardi.
    /// </summary>
    private string Username =>
        options.Current.BotUsername.Trim().TrimStart('@');
}
