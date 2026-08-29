using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Common.Interfaces;

namespace Zinnur.Application.Auth.Services;

/// <summary>
/// <see cref="ITelegramLoginTicketStore"/> — Redis (<see cref="ICacheService"/>) ustida.
///
/// ★ TUZILISH ATAYLAB <see cref="PhoneLoginCodeStore"/> NI TAKRORLAYDI:
/// tuzlangan hash, hisoblagichni oldindan oshirish, doimiy vaqtli
/// taqqoslash. Ikki oqim bir xil qiymatni (6 xonali kod) himoya qiladi,
/// ya'ni ularning himoyasi ham bir xil bo'lishi shart — biriga qo'shilgan
/// chora ikkinchisiga qo'shilmasa, hujumchi zaifrogini tanlardi.
/// </summary>
public sealed class TelegramLoginTicketStore(ICacheService cache, TimeProvider clock)
    : ITelegramLoginTicketStore
{
    /// <summary>
    /// Chiptaning umri.
    ///
    /// ★ 15 DAQIQA — 5 daqiqalik kod umridan UZUNROQ, va bu ataylab:
    /// chipta odam BOTNI OCHGUNIGA qadar yashashi kerak (Telegramni
    /// topish, kirish, tugmani bosish), kod esa faqat u kelgandan keyin
    /// boshlanadi. Ikkalasini tenglashtirsak, botni sekinroq ochgan odam
    /// "havola eskirgan" xabarini olardi.
    /// </summary>
    public static readonly TimeSpan TicketTtl = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Bitta chipta uchun ruxsat etilgan kod urinishlari.
    /// Sabab va hisob-kitob — <see cref="PhoneLoginCodeStore.MaxAttempts"/>.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <inheritdoc />
    public Task CreateAsync(string token, CancellationToken ct = default) =>
        cache.SetAsync(
            TicketKey(Slug(token)),
            new StoredTicket(TelegramLoginStatuses.Waiting, null, null, null, clock.GetUtcNow()),
            TicketTtl,
            ct);

    /// <inheritdoc />
    public async Task<TelegramLoginTicket?> GetAsync(string token, CancellationToken ct = default)
    {
        var stored = await cache
            .GetAsync<StoredTicket>(TicketKey(Slug(token)), ct)
            .ConfigureAwait(false);

        return stored is null
            ? null
            : new TelegramLoginTicket(stored.Status, stored.UserId, stored.CreatedAt);
    }

    /// <inheritdoc />
    public async Task SaveStatusAsync(
        string token, string status, long? userId, CancellationToken ct = default)
    {
        var slug = Slug(token);

        var stored = await cache.GetAsync<StoredTicket>(TicketKey(slug), ct).ConfigureAwait(false);

        // Chipta yo'q — hech nima qilmaymiz (sabab interfeys izohida).
        if (stored is null)
            return;

        /*
          ★ TTL QAYTA BOSHLANMAYDI — u `Remaining` bilan hisoblanadi.
            Aks holda har `/start` chiptaning umrini yana 15 daqiqaga
            cho'zardi va uni cheksiz tirik saqlash mumkin bo'lardi.
        */
        await cache.SetAsync(
            TicketKey(slug),
            stored with { Status = status, UserId = userId ?? stored.UserId },
            Remaining(stored.CreatedAt),
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveCodeAsync(
        string token, long userId, string code, CancellationToken ct = default)
    {
        var slug = Slug(token);

        var stored = await cache.GetAsync<StoredTicket>(TicketKey(slug), ct).ConfigureAwait(false);

        if (stored is null)
            return;

        var salt = RandomNumberGenerator.GetBytes(SaltLength);

        // Urinishlar hisoblagichi AVVAL tozalanadi — sabab
        // `PhoneLoginCodeStore.SaveAsync` izohida (eski koddan qolgan
        // urinishlar yangi kodni bir bosishda o'ldirib qo'yardi).
        await cache.RemoveAsync(AttemptKey(slug), ct).ConfigureAwait(false);

        await cache.SetAsync(
            TicketKey(slug),
            stored with
            {
                Status = TelegramLoginStatuses.CodeSent,
                UserId = userId,
                Salt = Convert.ToHexString(salt),
                Hash = Convert.ToHexString(Hash(salt, code)),
            },
            Remaining(stored.CreatedAt),
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<(PhoneCodeCheck Check, long? UserId)> ConsumeAsync(
        string token, string code, CancellationToken ct = default)
    {
        var slug = Slug(token);

        // Hisoblagich O'QISHDAN OLDIN oshiriladi: "chipta yo'q" shoxi ham
        // urinish sifatida sanalsin (sabab `PhoneLoginCodeStore` izohida).
        var attempts = await cache
            .IncrementAsync(AttemptKey(slug), TicketTtl, ct)
            .ConfigureAwait(false);

        if (attempts > MaxAttempts)
        {
            await cache.RemoveAsync(TicketKey(slug), ct).ConfigureAwait(false);
            return (PhoneCodeCheck.TooManyAttempts, null);
        }

        var stored = await cache.GetAsync<StoredTicket>(TicketKey(slug), ct).ConfigureAwait(false);

        if (stored?.Salt is null || stored.Hash is null || stored.UserId is null)
            return (PhoneCodeCheck.Invalid, null);

        var expected = Convert.FromHexString(stored.Hash);
        var actual = Hash(Convert.FromHexString(stored.Salt), code);

        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            return (PhoneCodeCheck.Invalid, null);

        // BIR MARTALIK. Poyga haqidagi halol izoh — `PhoneLoginCodeStore`
        // dagi bilan AYNI: ikkita bir vaqtdagi TO'G'RI kod ikkita token
        // beradi, bu esa "ikki qurilmada kirdim" holati, xavf emas.
        await cache.RemoveAsync(TicketKey(slug), ct).ConfigureAwait(false);
        await cache.RemoveAsync(AttemptKey(slug), ct).ConfigureAwait(false);

        return (PhoneCodeCheck.Ok, stored.UserId);
    }

    /// <inheritdoc />
    public Task SetPendingAsync(long telegramUserId, string token, CancellationToken ct = default) =>
        cache.SetAsync(PendingKey(telegramUserId), token, TicketTtl, ct);

    /// <inheritdoc />
    public async Task<string?> TakePendingAsync(long telegramUserId, CancellationToken ct = default)
    {
        var key = PendingKey(telegramUserId);

        var token = await cache.GetAsync<string>(key, ct).ConfigureAwait(false);

        if (token is null)
            return null;

        // ★ O'QISH VA O'CHIRISH ATOMAR EMAS — va bu zararsiz: eng yomon
        //   holatda AYNI chiptaga ikkita kod yuboriladi, ikkinchisi
        //   birinchisining ustiga yoziladi va foydalanuvchi oxirgisini
        //   ishlatadi. Atomar `GETDEL` uchun `ICacheService` shartnomasini
        //   kengaytirish kerak bo'lardi.
        await cache.RemoveAsync(key, ct).ConfigureAwait(false);

        return token;
    }

    // ================================================================ ichki

    /// <summary>Tuz uzunligi — 16 bayt (sabab <see cref="PhoneLoginCodeStore"/> da).</summary>
    private const int SaltLength = 16;

    /// <summary>
    /// Chiptaning qolgan umri. Kamida bir soniya — <c>SetAsync</c> ga
    /// nol yoki manfiy TTL berilsa kalit umuman yozilmasdi.
    /// </summary>
    private TimeSpan Remaining(DateTimeOffset createdAt)
    {
        var left = createdAt + TicketTtl - clock.GetUtcNow();
        return left < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : left;
    }

    private static byte[] Hash(byte[] salt, string? code)
    {
        var payload = Encoding.UTF8.GetBytes(code ?? string.Empty);

        var buffer = new byte[salt.Length + payload.Length];
        salt.CopyTo(buffer, 0);
        payload.CopyTo(buffer, salt.Length);

        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Tokendan KALIT BO'LAGI. Tokenning O'ZI kalitga tushmaydi — sabab
    /// interfeys izohida. 24 belgi = 96 bit, to'qnashuv amalda imkonsiz.
    /// </summary>
    private static string Slug(string? token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty));
        return Convert.ToHexString(digest)[..24];
    }

    private static string TicketKey(string slug) =>
        string.Create(CultureInfo.InvariantCulture, $"auth:tg:ticket:{slug}");

    private static string AttemptKey(string slug) =>
        string.Create(CultureInfo.InvariantCulture, $"auth:tg:attempt:{slug}");

    /// <summary>
    /// «Raqam kutilyapti» belgisi. Telegram ID — maxfiy emas (u xabarning
    /// O'ZIDA keladi), lekin kalit MAKONI baribir alohida: bu yozuv
    /// chiptadan boshqa umrga va boshqa ma'noga ega.
    /// </summary>
    private static string PendingKey(long telegramUserId) =>
        string.Create(CultureInfo.InvariantCulture, $"auth:tg:pending:{telegramUserId}");

    /// <summary>
    /// Redisda yotadigan yozuv.
    ///
    /// <c>Salt</c>/<c>Hash</c> — kod BERILGANDAN keyin to'ladi; bo'sh
    /// bo'lishi normal holat (bot hali kod yubormagan).
    /// </summary>
    private sealed record StoredTicket(
        string Status,
        long? UserId,
        string? Salt,
        string? Hash,
        DateTimeOffset CreatedAt);
}
