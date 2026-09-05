using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Zinnur.Application.Recordings.Services;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="IRecordingCompositionStore"/> port'ining Postgres amalga
/// oshirilishi.
///
/// ★ NIMA UCHUN XOM SQL, EF EMAS — <c>OutboxStore</c> dagi AYNI sabab:
/// <c>FOR UPDATE SKIP LOCKED</c> ni LINQ ifodalay olmaydi, <c>ExecuteUpdate</c>
/// esa qulfsiz ishlaydi va ikki ishchi bitta qatorni olib qo'yardi.
/// </summary>
public sealed class RecordingCompositionStore(ApplicationDbContext db, TimeProvider clock)
    : IRecordingCompositionStore
{
    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════
    /// EGALLASH — BITTA BAYONOT (SPEC-RECORDING-V2 §4.4)
    /// ═══════════════════════════════════════════════════════════════════
    ///
    /// Ichki <c>SELECT</c> qatorni QULFLAYDI; <c>SKIP LOCKED</c> boshqa
    /// ishchi qulflagan qatorni KUTMASDAN o'tkazib yuboradi. Natijada ikki
    /// kompozitor parallel ishlaganda ham bir yozuvni ikkalasi ola
    /// olmaydi va hech kim navbatda turmaydi.
    ///
    /// <c>ORDER BY "CreatedAt", "Id"</c> — ENG ESKISI BIRINCHI. Bu
    /// SPEC ning oshkor talabi: tungi oynaga sig'magan ish keyingi kechada
    /// o'zidan keyin kelganlardan OLDIN olinadi.
    ///
    /// Shartlar: navbatdagi (<c>1 = Queued</c>) YOKI ijarasi ESKIRGAN
    /// ishlayotgan (<c>2 = Running</c>) qator. Ikkinchisi — QULAGAN
    /// ISHCHIDAN qolgan ish.
    ///
    /// ── SPEC DAGI MATNDAN IKKI FARQ (ataylab, sababi bilan) ────────────
    ///
    ///   1) <c>UPDATE … WHERE "Id" = (SELECT …)</c> o'rniga
    ///      <c>UPDATE … FROM (SELECT …)</c>. Shart, tartib, <c>LIMIT</c> va
    ///      <c>FOR UPDATE SKIP LOCKED</c> AYNAN o'sha; farq faqat
    ///      shaklda va u KERAK, chunki <c>RETURNING</c> ga qatorning
    ///      ESKI holati kerak (pastdagi 2-band). Bu shakl loyihada
    ///      allaqachon bor — <c>OutboxStore.ClaimSql</c>.
    ///
    ///   2) <c>RETURNING</c> da qatorning EGALLASHDAN OLDINGI holati ham
    ///      qaytadi. Usiz "bu navbatdagi ish edimi yoki qulagan
    ///      ishchidan qolganmi" degan savolga javob bo'lmasdi, javobsiz
    ///      esa: (a) urinishlar hisoblagichi hech qachon oshmasdi va har
    ///      kecha aynan o'sha joyda qulaydigan ish ABADIY qaytaverardi;
    ///      (b) yakuniy kalitdagi YARIM fayl o'chirilmasdi.
    ///
    /// ⚠️ HISOBLAGICH VA IJARA AYNI BAYONOTDA YOZILADI. Ularni ikkinchi
    ///    <c>UPDATE</c> ga chiqarish ikkisining orasida qulagan ishchi
    ///    uchun hisoblagichni jimgina yo'qotardi. Ayni o'tish qoidasi
    ///    <c>SessionRecording.TryClaimComposition</c> da yozilgan va unit
    ///    testlari bilan qoplangan; integratsiya testi ikkalasi BIR XIL
    ///    natija berishini tekshiradi.
    /// ═══════════════════════════════════════════════════════════════════
    /// </summary>
    private const string ClaimSql =
        """
        UPDATE "SessionRecordings" AS r
        SET "CompositionStatus"    = 2,
            "CompositionStartedAt" = @now,
            "CompositionLeaseUntil" = @leaseUntil,
            "CompositionAttempts"  = r."CompositionAttempts"
                                     + CASE WHEN c."CompositionStatus" = 2 THEN 1 ELSE 0 END,
            "CompositionError"     = CASE WHEN c."CompositionStatus" = 2
                                          THEN @takeoverNote
                                          ELSE r."CompositionError" END,
            "UpdatedAt"            = @now
        FROM (
            SELECT "Id", "CompositionStatus"
            FROM "SessionRecordings"
            WHERE "Pipeline" = 1
              AND ( "CompositionStatus" = 1
                 OR ("CompositionStatus" = 2 AND "CompositionLeaseUntil" < @now) )
            ORDER BY "CreatedAt" ASC, "Id" ASC
            LIMIT 1
            FOR UPDATE SKIP LOCKED
        ) AS c
        WHERE r."Id" = c."Id"
        RETURNING r."Id", r."CompositionStartedAt", (c."CompositionStatus" = 2);
        """;

    /// <summary>
    /// Ijarani uzaytirish.
    ///
    /// 🔴 UCH SHARTNING UCHALASI HAM KERAK:
    ///   • <c>"CompositionStatus" = 2</c> — qator hali ishlanmoqda
    ///     (yakunlangan yoki navbatga qaytarilganini tiriltirmaslik);
    ///   • <c>"CompositionStartedAt" = @claimedAt</c> — EGALIK CHIPTASI,
    ///     ya'ni bu AYNAN BIZNING egallashimiz (port izohiga qarang);
    ///   • <c>"CompositionLeaseUntil" &gt;= @now</c> — ijaramiz hali
    ///     tirik. Eskirgan ijarani "uzaytirish" boshqa ishchi allaqachon
    ///     qo'lga olgan ishni qaytarib olishga urinish bo'lardi.
    /// </summary>
    private const string RenewSql =
        """
        UPDATE "SessionRecordings"
        SET "CompositionLeaseUntil" = @leaseUntil,
            "UpdatedAt" = @now
        WHERE "Id" = @id
          AND "CompositionStatus" = 2
          AND "CompositionStartedAt" = @claimedAt
          AND "CompositionLeaseUntil" >= @now;
        """;

    /// <summary>
    /// Egallash oldingi urinish uzilib qolgani uchun bo'lsa, xodim
    /// ro'yxatda shu matnni ko'radi.
    ///
    /// ⚠️ <c>SessionRecording.TryClaimComposition</c> DAGI MATN BILAN
    /// AYNI: bir hodisa ikki xil nomlanmasin.
    /// </summary>
    private const string TakeoverNote =
        "Oldingi yig'ish urinishi uzilib qoldi — boshidan boshlanmoqda.";

    /// <inheritdoc />
    public async Task<CompositionClaim?> ClaimAsync(
        TimeSpan lease, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lease, TimeSpan.Zero);

        var now = clock.GetUtcNow();

        // EF orqali ochamiz: u ulanishni o'zi ochgan bo'lsa qayta ochmaydi
        // va yopishda ham hisobini yuritadi (`OutboxStore` dagi AYNI naqsh).
        await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();

            command.CommandText = ClaimSql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            command.Parameters.Add(new NpgsqlParameter("now", now));
            command.Parameters.Add(new NpgsqlParameter("leaseUntil", now + lease));
            command.Parameters.Add(new NpgsqlParameter("takeoverNote", TakeoverNote));

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return null;

            return new CompositionClaim(
                RecordingId: reader.GetInt64(0),
                TookOverExpiredLease: reader.GetBoolean(2),
                ClaimedAt: reader.GetFieldValue<DateTimeOffset>(1));
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RenewAsync(
        long recordingId, DateTimeOffset claimedAt, TimeSpan lease, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lease, TimeSpan.Zero);

        var now = clock.GetUtcNow();

        await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();

            command.CommandText = RenewSql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            command.Parameters.Add(new NpgsqlParameter("id", recordingId));
            command.Parameters.Add(new NpgsqlParameter("claimedAt", claimedAt));
            command.Parameters.Add(new NpgsqlParameter("now", now));
            command.Parameters.Add(new NpgsqlParameter("leaseUntil", now + lease));

            var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            return affected > 0;
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
