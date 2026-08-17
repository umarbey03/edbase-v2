using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Infrastructure.Persistence;
using Zinnur.Infrastructure.Persistence.Configurations;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="INotificationOutbox"/> port'ining EF amalga oshirilishi.
///
/// ★ SCOPED va ATAYLAB <c>ApplicationDbContext</c> GA TAYANADI (interfeysga
/// emas): yozuv AYNI so'rovning kuzatuvchisiga qo'shilishi shart, aks holda
/// biznes o'zgarishi va xabar ikki xil tranzaksiyaga tushib, commit-then-send
/// kafolati buzilardi. Bu <c>FinanceSettingsStore</c> dagi bilan bir xil
/// naqsh: u yerda ham sozlama va audit izi bitta <c>SaveChanges</c> bilan
/// yoziladi.
///
/// ★ <c>SaveChanges</c> BU YERDA CHAQIRILMAYDI.
/// </summary>
public sealed class OutboxWriter(ApplicationDbContext db, TimeProvider clock) : INotificationOutbox
{
    /// <inheritdoc />
    public async Task<bool> EnqueueAsync(NotificationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = Require(request.IdempotencyKey, nameof(request.IdempotencyKey),
            MessageOutboxConfiguration.IdempotencyKeyMaxLength);

        var templateKey = Require(request.TemplateKey, nameof(request.TemplateKey),
            MessageOutboxConfiguration.TemplateKeyMaxLength);

        // Matn KESILMAYDI, RAD ETILADI: tayyor matnni oxiridan qirqish ochiq
        // qolgan `<b>` tegini qoldirib, xabarni Telegram uchun yaroqsiz
        // qilardi. Parametrni qirqish `NotificationText.Parameter` ning ishi.
        var body = Require(request.Body, nameof(request.Body), NotificationText.MaxBodyLength);

        var callbackData = request.CallbackData;

        if (callbackData is { Length: > 0 }
            && callbackData.Length > MessageOutboxConfiguration.CallbackDataMaxLength)
        {
            throw new ArgumentException(
                $"Xabarning `CallbackData` maydoni {MessageOutboxConfiguration.CallbackDataMaxLength} "
                + $"belgidan uzun ({callbackData.Length}).",
                nameof(request));
        }

        if (request.RecipientUserId is null && string.IsNullOrWhiteSpace(request.RecipientAddress))
        {
            throw new ArgumentException(
                "Xabar qabul qiluvchisiz navbatga qo'yilmaydi: `RecipientUserId` yoki "
                + "`RecipientAddress` dan kamida bittasi bo'lishi shart.",
                nameof(request));
        }

        // ═════════════════════════════════════════════════════════════
        // TAKRORNI IKKI BOSQICHDA TO'SAMIZ.
        //
        // 1) KUZATUVCHIDA (Local): ayni tranzaksiyada bir kalit ikki marta
        //    qo'shilsa, baza chaqiruvigacha bormasdan to'siladi. Bunsiz
        //    `SaveChanges` unikal indeksga urilib, BUTUN biznes
        //    tranzaksiyasini yiqitardi.
        // 2) BAZADA: oldingi so'rovlarda yozilgan kalit bormi.
        //
        // Uchinchi to'siq — unikal indeksning O'ZI: ikki INSTANCE ayni
        // vaqtda tekshirsa, ikkalasi ham "yo'q" deb ko'radi. O'shanda
        // ikkinchi `SaveChanges` yiqiladi va chaqiruvchi amalni qaytadan
        // bajaradi — ikkinchi urinishda bu tekshiruv uni to'sadi. Bu
        // ONGLI tanlov: jimgina ikki marta yuborishdan ko'ra, ochiq xato
        // va qayta urinish afzal.
        // ═════════════════════════════════════════════════════════════
        foreach (var tracked in db.MessageOutbox.Local)
        {
            if (string.Equals(tracked.IdempotencyKey, key, StringComparison.Ordinal))
                return false;
        }

        var exists = await db.MessageOutbox
            .AsNoTracking()
            .AnyAsync(m => m.IdempotencyKey == key, ct)
            .ConfigureAwait(false);

        if (exists) return false;

        var now = clock.GetUtcNow();

        db.MessageOutbox.Add(new MessageOutbox
        {
            Channel = request.Channel,
            RecipientUserId = request.RecipientUserId,
            RecipientAddress = Trim(request.RecipientAddress),
            TemplateKey = templateKey,
            Body = body,
            CallbackData = string.IsNullOrWhiteSpace(callbackData) ? null : callbackData,
            IdempotencyKey = key,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,

            // Birinchi urinish KECHIKTIRILMAYDI (sabab: OutboxRetryPolicy).
            // Rejalashtirilgan xabar uchun esa `SendAfter` beriladi.
            NextAttemptAt = request.SendAfter ?? now,
            CreatedAt = now,
        });

        return true;
    }

    /// <summary>
    /// Majburiy maydonni tekshiradi.
    ///
    /// ★ ISTISNO TURI <see cref="ArgumentException"/> — <c>ValidationException</c>
    /// EMAS: bu qiymatlarni FOYDALANUVCHI kiritmaydi, ularni kod yasaydi.
    /// 400 qaytarish "so'rovingiz noto'g'ri" degan yolg'on xabar bo'lardi;
    /// bu esa dasturchi xatosi va u 500 bo'lib logga tushishi kerak.
    /// </summary>
    private static string Require(string? value, string field, int maxLength)
    {
        var text = value?.Trim();

        if (string.IsNullOrEmpty(text))
            throw new ArgumentException($"Xabarning `{field}` maydoni bo'sh.", nameof(value));

        if (text.Length > maxLength)
        {
            throw new ArgumentException(
                $"Xabarning `{field}` maydoni {maxLength} belgidan uzun ({text.Length}).",
                nameof(value));
        }

        return text;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
