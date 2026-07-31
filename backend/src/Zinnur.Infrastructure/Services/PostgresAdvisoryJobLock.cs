using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;
using Zinnur.Application.Jobs;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="IJobLock"/> port'ining PostgreSQL amalga oshirilishi —
/// <c>pg_try_advisory_lock</c>.
///
/// Mexanizm tanlovining sabablari port izohida (<see cref="IJobLock"/>).
/// Bu yerda AMALGA OSHIRISHNING ikkita nozik joyi tushuntiriladi.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ 1) NIMA UCHUN ALOHIDA VA HOVUZSIZ (<c>Pooling=false</c>) ULANISH
///
/// Advisory lock SESSIYAGA (ya'ni ulanishga) bog'langan, EF Core esa
/// ulanishlar HOVUZIDAN foydalanadi. Agar qulf EF ning ulanishida olinsa:
///
///   • so'rov tugagach ulanish hovuzga QAYTADI va boshqa so'rovga beriladi,
///     lekin sessiya YOPILMAGANI uchun qulf hamon o'sha ulanishda turadi —
///     ya'ni butunlay boshqa kod qulfni tasodifan ushlab turgan bo'ladi;
///   • hovuz ulanishni "tozalash" (<c>DISCARD ALL</c>) bilan qaytarsa, qulf
///     ish O'RTASIDA bo'shab ketardi va ikkinchi instance vazifani parallel
///     boshlardi — ya'ni qulfning butun ma'nosi yo'qolardi.
///
/// Shuning uchun qulf uchun <c>Pooling=false</c> bilan ochilgan ALOHIDA
/// ulanish ishlatiladi: u hovuzga umuman tushmaydi, faqat qulf uchun
/// yashaydi va <see cref="IJobLockHandle.DisposeAsync"/> da FIZIK yopiladi.
/// Ulanish yopilishi = sessiya tugashi = qulf bo'shashi.
///
/// ★ 2) NIMA UCHUN "HEARTBEAT" KERAK EMAS
///
/// Qulfning muddati YO'Q — u vaqtga emas, ulanishga bog'liq. Ish 10 daqiqa
/// davom etsa ham qulf ushlab turiladi. Ulanishning O'ZI tirik qolishi
/// uchun Npgsql'ning <c>Keepalive</c> paketi ishlatiladi: uzoq jimlikda
/// oradagi NAT yoki yuk balanslagichi ulanishni jimgina uzib qo'ymasin.
/// Instance QULASA esa TCP uziladi va Postgres qulfni DARHOL bo'shatadi —
/// qulf jadvali bo'lganda tizim muddat tugagunicha kutib turardi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PostgresAdvisoryJobLock : IJobLock
{
    /// <summary>Qulf kalitlari makoni — hash'ga qo'shiladigan prefiks.</summary>
    private const string KeyNamespace = "zinnur:job:";

    /// <summary>
    /// Qulf so'rovlari uchun timeout (sekund). QISQA bo'lishi SHART: baza
    /// javob bermay qolsa fon xizmati unda mangu osilib turmasin —
    /// keyingi aylanishda qaytadan urinadi.
    /// </summary>
    private const int TimeoutSeconds = 10;

    /// <summary>Ulanish jimligida yuboriladigan tiriklik paketi (sekund).</summary>
    private const int KeepAliveSeconds = 30;

    private readonly string _connectionString;
    private readonly ILogger<PostgresAdvisoryJobLock> _logger;

    public PostgresAdvisoryJobLock(string connectionString, ILogger<PostgresAdvisoryJobLock> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        // ★ Ulanish satri QAYTA QURILADI, ilovaning odatdagi satri
        // ISHLATILMAYDI: hovuzlash o'chiriladi (sabab yuqorida) va
        // `ApplicationName` beriladi — `pg_stat_activity` da "bu ulanish
        // nima uchun ochiq turibdi?" degan savolga darhol javob bo'lsin.
        _connectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            ApplicationName = "zinnur-job-lock",
            KeepAlive = KeepAliveSeconds,
            Timeout = TimeoutSeconds,
            CommandTimeout = TimeoutSeconds,
        }.ConnectionString;
    }

    /// <inheritdoc />
    public async Task<IJobLockHandle?> TryAcquireAsync(
        string jobName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);

        var key = KeyOf(jobName);
        var connection = new NpgsqlConnection(_connectionString);

        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);

            // `pg_try_advisory_lock` — KUTMAYDI: qulf band bo'lsa darhol
            // `false` qaytaradi. Bloklaydigan `pg_advisory_lock` ishlatilsa
            // ikkinchi instance navbatda turib, keyin AYNI ishni qaytadan
            // bajarardi (birinchisi allaqachon bajarib bo'lgan bo'lsa ham).
            await using var command = new NpgsqlCommand(
                "SELECT pg_try_advisory_lock(@key)", connection);

            command.Parameters.Add(new NpgsqlParameter<long>("key", key));

            var acquired = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;

            if (!acquired)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            return new Handle(jobName, key, connection, _logger);
        }
        catch
        {
            // Ochilgan ulanish OSILIB QOLMASIN: xato yuqoriga ketadi, lekin
            // resurs shu yerda qaytariladi.
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Vazifa nomidan 64-bitli qulf kaliti.
    ///
    /// ★ NIMA UCHUN SHA-256, <c>string.GetHashCode()</c> EMAS: .NET'da satr
    /// hash'i HAR JARAYONDA boshqacha (hash flooding'dan himoya). Ikki
    /// konteyner bir xil vazifa uchun IKKI XIL kalit hisoblab, ikkalasi ham
    /// qulfni "olib" ishni parallel bajarardi — ya'ni qulf umuman
    /// ishlamasdi va buni faqat prod'da sezish mumkin bo'lardi.
    ///
    /// Prefiks (<see cref="KeyNamespace"/>) — advisory lock makoni butun
    /// BAZA uchun umumiy: kelajakda boshqa modul ham advisory lock
    /// ishlatsa, kalitlar tasodifan to'qnashmasin.
    /// </summary>
    private static long KeyOf(string jobName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(KeyNamespace + jobName));
        return BinaryPrimitives.ReadInt64LittleEndian(hash);
    }

    /// <summary>
    /// Olingan qulf: o'z ulanishini OXIRIGACHA ushlab turadi.
    /// </summary>
    private sealed class Handle(
        string jobName, long key, NpgsqlConnection connection, ILogger logger) : IJobLockHandle
    {
        /// <inheritdoc />
        public string JobName => jobName;

        /// <inheritdoc />
        public async Task<bool> IsHeldAsync(CancellationToken ct = default)
        {
            if (connection.State != ConnectionState.Open) return false;

            try
            {
                // Eng arzon "ulanish tirikmi" so'rovi. Ulanish uzilgan
                // bo'lsa Npgsql istisno tashlaydi — qulf ham bizda emas,
                // chunki sessiya tugagan.
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (NpgsqlException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                // Ulanish shu orada yopilgan (masalan `Dispose` bilan).
                return false;
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            try
            {
                // ★ OSHKOR BO'SHATISH — MAJBURIY EMAS, lekin FOYDALI:
                // ulanishning o'zi yopilishi bilan qulf baribir bo'shaydi.
                // Oshkor `unlock` esa qulfni ulanish yopilishini kutmasdan,
                // AYNI ONDA bo'shatadi — ikkinchi instance keyingi
                // aylanishda uni darhol egallay oladi.
                if (connection.State == ConnectionState.Open)
                {
                    await using var command = new NpgsqlCommand(
                        "SELECT pg_advisory_unlock(@key)", connection);

                    command.Parameters.Add(new NpgsqlParameter<long>("key", key));
                    await command.ExecuteScalarAsync().ConfigureAwait(false);
                }
            }
            catch (NpgsqlException ex)
            {
                // Bo'shatolmadik — ulanish yopilishi buni baribir hal
                // qiladi. Xato YUTILADI: qulfni bo'shatishdagi nosozlik
                // vazifaning muvaffaqiyatli natijasini bekor qilmasligi
                // kerak.
                JobLockLog.UnlockFailed(logger, ex, jobName);
            }
            catch (InvalidOperationException ex)
            {
                JobLockLog.UnlockFailed(logger, ex, jobName);
            }
            finally
            {
                // Ulanish HAR HOLDA yopiladi: `Pooling=false` bo'lgani
                // uchun bu fizik uzilish, ya'ni sessiya bilan birga qulf
                // ham yo'qoladi.
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

/// <summary>Manba-generatsiyali log metodlari (CA1848).</summary>
internal static partial class JobLockLog
{
    [LoggerMessage(
        EventId = 6440,
        Level = LogLevel.Warning,
        Message = "Vazifa qulfini oshkor bo'shatib bo'lmadi: {JobName}. "
                  + "Ulanish yopilishi bilan qulf baribir bo'shaydi.")]
    internal static partial void UnlockFailed(ILogger logger, Exception exception, string jobName);
}
