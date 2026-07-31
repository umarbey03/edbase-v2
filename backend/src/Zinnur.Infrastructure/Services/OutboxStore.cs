using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Infrastructure.Persistence;
using Zinnur.Infrastructure.Persistence.Configurations;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="IOutboxStore"/> port'ining Postgres amalga oshirilishi.
/// </summary>
public sealed class OutboxStore(ApplicationDbContext db, TimeProvider clock) : IOutboxStore
{
    /// <summary>
    /// ═══════════════════════════════════════════════════════════════
    /// ★ NAVBATDAN OLISH — <c>FOR UPDATE SKIP LOCKED</c>
    ///
    /// Ichki <c>SELECT</c> qatorlarni QULFLAYDI; <c>SKIP LOCKED</c> esa
    /// boshqa worker allaqachon qulflagan qatorni KUTMASDAN o'tkazib
    /// yuboradi. Natijada ikki instance parallel ishlaganda ham bir xabar
    /// ikki marta olinmaydi va hech kim navbatda turmaydi.
    ///
    /// Bunsiz ikki instance bir xil qatorlarni tanlab, xabarni IKKI MARTA
    /// yuborardi — eski tizimning "bir eslatma bir necha marta keldi"
    /// muammosining aynan takrori bo'lardi.
    ///
    /// <c>SET "NextAttemptAt" = lease</c> — ko'rinmaslik muddati: qator
    /// darhol kelajakka suriladi, ya'ni tranzaksiya tugagach ham (qulf
    /// bo'shagach) uni boshqa worker olmaydi. Worker qulasa qator muddat
    /// o'tgach O'ZI qaytib ko'rinadi.
    ///
    /// <c>ORDER BY "NextAttemptAt", "Id"</c> — eng kutgan xabar birinchi;
    /// <c>Id</c> tartibni ANIQ qiladi (bir xil vaqtli qatorlar uchun).
    ///
    /// ★ URINISH HISOBLAGICHI BU YERDA OSHMAYDI — faqat yiqilganda oshadi
    /// (sabab port izohida).
    ///
    /// ★ NIMA UCHUN EF EMAS, XOM SQL: `FOR UPDATE SKIP LOCKED` ni LINQ
    /// ifodalay olmaydi. `ExecuteUpdate` esa qulfsiz ishlaydi va ikki
    /// worker bir qatorni olib qo'yardi.
    /// ═══════════════════════════════════════════════════════════════
    /// </summary>
    private const string ClaimSql =
        """
        UPDATE "MessageOutbox" AS o
        SET "NextAttemptAt" = @leaseUntil,
            "UpdatedAt" = @now
        FROM (
            SELECT "Id"
            FROM "MessageOutbox"
            WHERE "Status" = 0 AND "NextAttemptAt" <= @now
            ORDER BY "NextAttemptAt", "Id"
            LIMIT @batchSize
            FOR UPDATE SKIP LOCKED
        ) AS c
        WHERE o."Id" = c."Id"
        RETURNING o."Id", o."Channel", o."RecipientUserId", o."RecipientAddress",
                  o."TemplateKey", o."Body", o."AttemptCount";
        """;

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxMessage>> ClaimAsync(
        int batchSize, TimeSpan lease, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lease, TimeSpan.Zero);

        var now = clock.GetUtcNow();

        // EF orqali ochamiz (o'zi ochgan bo'lsa qayta ochmaydi va
        // `CloseConnection` da ham EF hisobini yuritadi).
        await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();

            command.CommandText = ClaimSql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            command.Parameters.Add(new NpgsqlParameter("now", now));
            command.Parameters.Add(new NpgsqlParameter("leaseUntil", now + lease));
            command.Parameters.Add(new NpgsqlParameter("batchSize", batchSize));

            await using var reader = await command
                .ExecuteReaderAsync(ct)
                .ConfigureAwait(false);

            var claimed = new List<OutboxMessage>(batchSize);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                claimed.Add(Read(reader));

            return claimed;
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task MarkDeliveredAsync(long messageId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        // `ExecuteUpdate` — qatorni xotiraga YUKLAMASDAN yangilaydi.
        // Kuzatuvchi chetlab o'tilgani uchun `UpdatedAt` QO'LDA yoziladi
        // (`ApplicationDbContext.ApplyAuditTimestamps` bu yo'lda ishlamaydi).
        await db.MessageOutbox
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.Status, OutboxStatus.Sent)
                    .SetProperty(m => m.SentAt, now)
                    .SetProperty(m => m.UpdatedAt, now)

                    // Oldingi urinishning xatosi TOZALANADI: yozuv `Sent`
                    // bo'lib turib, yonida eski xato matni qolsa, uni
                    // ko'rgan odam muammo hali ham bor deb o'ylardi.
                    .SetProperty(m => m.LastError, (string?)null),
                ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkRejectedAsync(
        long messageId, string reason, TimeSpan? retryAfter, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        // Urinishlar tugagan bo'lsa xabar `Failed` ga o'tadi va boshqa
        // OLINMAYDI — "zaharli xabar" navbatni abadiy band qilmasin.
        var status = retryAfter is null ? OutboxStatus.Failed : OutboxStatus.Pending;
        var nextAttemptAt = now + (retryAfter ?? TimeSpan.Zero);
        var lastError = Shorten(reason);

        await db.MessageOutbox
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.Status, status)
                    .SetProperty(m => m.AttemptCount, m => m.AttemptCount + 1)
                    .SetProperty(m => m.LastError, lastError)
                    .SetProperty(m => m.NextAttemptAt, nextAttemptAt)
                    .SetProperty(m => m.UpdatedAt, now),
                ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PostponeAsync(
        IReadOnlyCollection<long> messageIds, TimeSpan delay, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        if (messageIds.Count == 0) return;

        var now = clock.GetUtcNow();

        // Manfiy kechikish kelib qolsa ham vaqt orqaga ketmasin.
        var nextAttemptAt = now + (delay > TimeSpan.Zero ? delay : TimeSpan.Zero);

        long[] ids = [.. messageIds];

        // `Status == Pending` sharti MAJBURIY: xabar shu orada boshqa
        // yo'l bilan yakunlangan bo'lsa (masalan qo'lda `Failed` qilingan),
        // uni qaytadan navbatga tortib olmaymiz.
        await db.MessageOutbox
            .Where(m => ids.Contains(m.Id) && m.Status == OutboxStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.NextAttemptAt, nextAttemptAt)
                    .SetProperty(m => m.UpdatedAt, now),
                ct)
            .ConfigureAwait(false);
    }

    /// <summary>Ustunlar tartibi <see cref="ClaimSql"/> dagi <c>RETURNING</c> bilan bir xil.</summary>
    private static OutboxMessage Read(DbDataReader reader) =>
        new(
            Id: reader.GetInt64(0),
            Channel: (NotificationChannel)reader.GetInt32(1),
            RecipientUserId: reader.IsDBNull(2) ? null : reader.GetInt64(2),
            RecipientAddress: reader.IsDBNull(3) ? null : reader.GetString(3),
            TemplateKey: reader.GetString(4),
            Body: reader.GetString(5),
            AttemptCount: reader.GetInt32(6));

    /// <summary>
    /// Xato matnini ustun chegarasiga sig'diradi. Uzun matn to'liq holda
    /// LOGDA qoladi — bazadagi ustun esa "nega yuborilmayapti" degan
    /// savolga javob berish uchun yetarli.
    /// </summary>
    private static string Shorten(string? reason)
    {
        var text = (reason ?? string.Empty).Trim();

        if (text.Length == 0) return "Sabab ko'rsatilmagan.";

        const int max = MessageOutboxConfiguration.LastErrorMaxLength;

        if (text.Length <= max) return text;

        // Emoji (surrogat juftlik) o'rtasidan kesilmasin — bunday satr
        // Postgres'ga yozilganda buziladi.
        var cut = max;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;

        return text[..cut];
    }
}
