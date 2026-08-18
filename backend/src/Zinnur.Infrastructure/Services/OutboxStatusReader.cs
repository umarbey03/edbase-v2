using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="IOutboxStatusReader"/> port'ining amalga oshirilishi.
///
/// ★ FAQAT O'QIYDI va faqat KALIT bo'yicha: navbat jadvali Application
/// qatlamiga ochilmaydi (sabab port izohida).
/// </summary>
public sealed class OutboxStatusReader(ApplicationDbContext db) : IOutboxStatusReader
{
    /// <summary>
    /// Bir so'rovda so'raladigan eng ko'p kalit.
    ///
    /// Ro'yxat sahifalangan (20-100 qator), shuning uchun bu chegara
    /// amalda hech qachon urilmaydi — u faqat noto'g'ri chaqiruv butun
    /// jadvalni so'rab qolishidan himoya.
    /// </summary>
    private const int MaxKeys = 500;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, OutboxStatusDto>> GetStatusesAsync(
        IReadOnlyCollection<string> idempotencyKeys, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKeys);

        var keys = idempotencyKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxKeys)
            .ToList();

        if (keys.Count == 0)
            return new Dictionary<string, OutboxStatusDto>(StringComparer.Ordinal);

        var rows = await db.MessageOutbox
            .AsNoTracking()
            .Where(m => keys.Contains(m.IdempotencyKey))
            .Select(m => new
            {
                m.IdempotencyKey,
                m.Status,
                m.SentAt,
                m.AttemptCount,
                m.LastError,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // `enum.ToString()` XOTIRADA — so'rov ichida yozilsa SQL'ga
        // tarjima qilishga majburlardi (loyihadagi AYNI qoida).
        return rows.ToDictionary(
            m => m.IdempotencyKey,
            m => new OutboxStatusDto(m.Status.ToString(), m.SentAt, m.AttemptCount, m.LastError),
            StringComparer.Ordinal);
    }
}
