using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Recordings.Services;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="ILiveKitWebhookLog"/> portining EF amalga oshirilishi.
///
/// ★ SCOPED va ATAYLAB <c>ApplicationDbContext</c> GA TAYANADI (portga
/// emas — <c>IApplicationDbContext</c> bu jadvalni umuman ko'rmaydi). Bu
/// <c>TelegramUpdateLog</c> dagi AYNI naqsh: yozuv JORIY so'rovning
/// kuzatuvchisiga tushadi va yozuv holatining o'zgarishi bilan BITTA
/// <c>SaveChanges</c> — ya'ni bitta tranzaksiya — da saqlanadi.
///
/// ★ <c>SaveChanges</c> BU YERDA CHAQIRILMAYDI.
/// </summary>
public sealed class LiveKitWebhookLog(ApplicationDbContext db, TimeProvider clock)
    : ILiveKitWebhookLog
{
    /// <inheritdoc />
    public async Task<bool> TryBeginAsync(string eventId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);

        // Kalit uzunligi CHEGARALANADI. Buzuq (yoki qasddan ulkan) `id`
        // maydoni bilan kelgan hodisa `SaveChanges` ni 22001 xatosi bilan
        // yiqitardi va LiveKit uni CHEKSIZ qayta yuborardi. Kesish
        // xavfsiz: qiymat faqat "buni ko'rganmidik?" savoliga xizmat
        // qiladi, mazmuni esa hech qayerda ishlatilmaydi.
        var key = eventId.Length <= RecordingWebhookEvent.MaxEventIdLength
            ? eventId
            : eventId[..RecordingWebhookEvent.MaxEventIdLength];

        // ═════════════════════════════════════════════════════════════
        // TAKRORNI IKKI BOSQICHDA TO'SAMIZ (`TelegramUpdateLog` bilan
        // bir xil):
        //
        //  1) KUZATUVCHIDA — ayni so'rovda ikki marta chaqirilsa bazaga
        //     bormasdan to'siladi;
        //  2) BAZADA — oldingi so'rovda yozilganmi.
        //
        // Uchinchi to'siq — BIRLAMCHI KALITNING O'ZI: ikki instansiya ayni
        // vaqtda tekshirsa, ikkalasi ham "yo'q" deb ko'radi va ikkinchi
        // `SaveChanges` yiqiladi. Bu ONGLI tanlov: jimgina ikki marta
        // ishlashdan ko'ra ochiq xato afzal (controller uni ushlab,
        // LiveKit'ga baribir 200 qaytaradi).
        // ═════════════════════════════════════════════════════════════
        foreach (var tracked in db.RecordingWebhookEvents.Local)
        {
            if (string.Equals(tracked.EventId, key, StringComparison.Ordinal))
                return false;
        }

        var exists = await db.RecordingWebhookEvents
            .AsNoTracking()
            .AnyAsync(e => e.EventId == key, ct)
            .ConfigureAwait(false);

        if (exists) return false;

        db.RecordingWebhookEvents.Add(new RecordingWebhookEvent
        {
            EventId = key,
            ReceivedAt = clock.GetUtcNow(),
        });

        return true;
    }
}
