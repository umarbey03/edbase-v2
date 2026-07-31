using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Telegram.Services;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="ITelegramUpdateLog"/> portining EF amalga oshirilishi.
///
/// ★ SCOPED va ATAYLAB <c>ApplicationDbContext</c> GA TAYANADI (interfeysga
/// emas — u bu jadvalni ko'rmaydi). Bu <c>OutboxWriter</c> dagi AYNI naqsh:
/// yozuv JORIY so'rovning kuzatuvchisiga tushadi va bog'lash bilan bitta
/// <c>SaveChanges</c> da saqlanadi.
///
/// ★ <c>SaveChanges</c> BU YERDA CHAQIRILMAYDI.
/// </summary>
public sealed class TelegramUpdateLog(ApplicationDbContext db, TimeProvider clock) : ITelegramUpdateLog
{
    /// <inheritdoc />
    public async Task<bool> TryBeginAsync(long updateId, CancellationToken ct = default)
    {
        // ═════════════════════════════════════════════════════════════
        // TAKRORNI IKKI BOSQICHDA TO'SAMIZ (`OutboxWriter` bilan bir xil):
        //
        //  1) KUZATUVCHIDA — ayni so'rovda ikki marta chaqirilsa bazaga
        //     bormasdan to'siladi.
        //  2) BAZADA — oldingi so'rovda yozilganmi.
        //
        // Uchinchi to'siq — BIRLAMCHI KALITNING O'ZI: ikki instansiya ayni
        // vaqtda tekshirsa, ikkalasi ham "yo'q" deb ko'radi va ikkinchi
        // `SaveChanges` yiqiladi. Bu ONGLI tanlov: jimgina ikki marta
        // ishlashdan ko'ra ochiq xato afzal (chaqiruvchi uni ushlab,
        // Telegram'ga baribir 200 qaytaradi).
        // ═════════════════════════════════════════════════════════════
        foreach (var tracked in db.TelegramUpdates.Local)
        {
            if (tracked.UpdateId == updateId) return false;
        }

        var exists = await db.TelegramUpdates
            .AsNoTracking()
            .AnyAsync(u => u.UpdateId == updateId, ct)
            .ConfigureAwait(false);

        if (exists) return false;

        db.TelegramUpdates.Add(new TelegramUpdate
        {
            UpdateId = updateId,
            ReceivedAt = clock.GetUtcNow(),
        });

        return true;
    }
}
