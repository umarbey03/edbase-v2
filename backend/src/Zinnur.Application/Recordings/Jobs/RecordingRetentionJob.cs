using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Jobs;
using Zinnur.Application.Recordings.Services;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Recordings.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// DARS YOZUVLARINI SAQLASH MUDDATI — N KUNDAN ESKISINI O'CHIRISH
/// (2026-10-31, loyiha egasi qarori: "30 kun saqlansin, keyin o'chirilsin")
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN KERAK: tayyor mp4 R2'da CHEKSIZ turardi (xom fayllar
/// montajdan keyin o'chiriladi, yakuniy fayl esa hech qachon). 30 guruhda
/// bu oyiga ~650 GB va har oy o'sib boradigan hisob. Bundan tashqari
/// yozuvda o'quvchilarning yuzi va ovozi bor — ya'ni bu SHAXSIY MA'LUMOT
/// va uni keraksiz saqlash o'zi xavf.
///
/// ── NIMA O'CHIRILADI ───────────────────────────────────────────────────
///
///   1) ombordagi yakuniy fayl (<c>SessionRecording.ObjectKey</c>);
///   2) ombordagi xom bo'laklar (<c>RecordingTrack.ObjectKey</c>) — odatda
///      ular allaqachon yo'q, lekin montaji yiqilgan yozuvda qolib ketgan
///      bo'lishi mumkin;
///   3) bazadagi qator (bo'laklar kaskad bilan ketadi).
///
/// ★ NIMA UCHUN QATOR HAM O'CHIRILADI (belgilanmaydi): fayl yo'q qator
/// ro'yxatda "o'lik" yozuv bo'lib ko'rinardi — o'quvchi "Ko'rish" ni bosib
/// 404 olardi. Belgi (`ExpiredAt`) qo'yilsa, HAR o'qish yo'liga filtr
/// qo'shish kerak bo'lardi (ro'yxat, havola, xodim paneli, hisobotlar) va
/// bittasida unutilardi — `ChatRetentionJob` dagi AYNI mulohaza. Migratsiya
/// ham kerak emas.
///
/// ── 🔴 XAVFSIZLIK QOIDALARI (testlar bilan qulflangan) ──────────────────
///
///   • KESIMDAN YANGI YOZUVGA HECH QACHON TEGILMAYDI. Muddat
///     <c>EndedAt ?? CreatedAt</c> dan sanaladi — ikkalasidan KECHROQ
///     (ya'ni ehtiyotkorroq) bo'lgani.
///   • JARAYONDAGI YOZUVGA TEGILMAYDI: faqat yakuniy holat
///     (<c>Completed</c>/<c>Failed</c>) va montaj ham yakunlangan
///     (yoki umuman bo'lmagan) qatorlar olinadi. Montaj navbatida 30 kundan
///     ko'p turgan yozuv (amalda yo'q holat) R2 lifecycle qoidasiga qoladi.
///   • OMBOR AVVAL, BAZA KEYIN. Fayl o'chmasa (ombor javob bermadi) qator
///     QOLADI va keyingi yurishda qayta uriniladi. Teskarisi — qator
///     o'chib, fayl R2'da MANGU qolardi: kalitni boshqa hech kim bilmaydi
///     (PUL va shaxsiy ma'lumot). <c>ChatRetentionJob</c> bu yerda boshqa
///     yo'l tanlagan, chunki u yerda egasining talabi aynan XABARLARNING
///     yo'qolishi edi; bu yerda esa talab — FAYLNING yo'qolishi.
///   • OMBOR SOZLANMAGAN BO'LSA HECH NIMA O'CHIRILMAYDI: fayllar boshqa
///     deployment'da bo'lishi mumkin va qatorni o'chirish ularni yetim
///     qoldirardi.
///   • MUDDAT VAZIFANING O'ZIDA HAM CHEKLANADI (<see cref="MinDays"/>).
///
/// ── IDEMPOTENTLIK VA PAKETLAR ──────────────────────────────────────────
///
/// Holat saqlanmaydi: har yurish kesimni qaytadan hisoblaydi. Ombordagi
/// <c>DELETE</c> obyekt yo'q bo'lsa ham xato bermaydi (port kelishuvi),
/// ya'ni yarmida qulagan yurishni keyingisi xavfsiz davom ettiradi.
/// Paket kichik (<see cref="RecordingRetentionSettings.BatchSize"/>): har
/// qator bir nechta tarmoq so'rovi, va birinchi yoqilganda orqada oylik
/// arxiv turgan bo'lishi mumkin — chegara qulfni soatlab ushlab turmaslik
/// uchun.
/// </summary>
public sealed class RecordingRetentionJob(
    IApplicationDbContext db,
    IRecordingStorage storage,
    ISettingsResolver settings,
    TimeProvider clock,
    RecordingRetentionSettings options,
    ILogger<RecordingRetentionJob> logger) : IScheduledJob
{
    /// <summary>
    /// 🔴 ENG QISQA RUXSAT ETILGAN MUDDAT — registrdagi <c>Minimum</c> bilan
    /// AYNI, nusxa ataylab (sabab: <c>ChatRetentionJob.MinMonths</c>).
    /// </summary>
    public const int MinDays = 7;

    /// <summary>Eng uzun muddat — registrdagi <c>Maximum</c> bilan bir xil.</summary>
    public const int MaxDays = 3650;

    /// <summary>Registrda kalit topilmasa (dasturchi xatosi emas, eski baza) — egasining qarori.</summary>
    public const int DefaultDays = 30;

    /// <inheritdoc />
    public string Name => "recording-retention";

    /// <inheritdoc />
    public TimeSpan Interval => options.Interval;

    /// <inheritdoc />
    public async Task<JobRunResult> RunAsync(CancellationToken ct = default)
    {
        var days = await ResolveDaysAsync(settings, ct).ConfigureAwait(false);

        if (!storage.IsConfigured)
        {
            // `Debug`: dev'da ombor sozlanmagan bo'lishi NORMAL. Prod'da esa
            // ombor sozlanmagan bo'lsa yozuvning O'ZI ham ishlamaydi va bu
            // boshqa joyda baland ko'rinadi.
            RetentionLog.StorageNotConfigured(logger);
            return JobRunResult.Nothing;
        }

        var now = clock.GetUtcNow();
        var cutoff = now.AddDays(-days);

        var deleted = 0;
        var failed = 0;
        var batches = 0;

        // Bir yurishda yiqilgan qatorlarni QAYTA olmaslik uchun — aks holda
        // ombor yotgan paytda bitta qator paketni abadiy band qilib turardi.
        var skip = new HashSet<long>();

        while (batches < options.MaxBatchesPerRun)
        {
            ct.ThrowIfCancellationRequested();

            // Massiv — EF uni `<> ALL (@p)` parametriga aylantiradi.
            var skipped = skip.ToArray();

            var batch = await db.SessionRecordings
                .AsTracking()
                .Include(r => r.Tracks)
                .Where(r => (r.EndedAt ?? r.CreatedAt) < cutoff)
                .Where(r => r.Status == RecordingStatus.Completed || r.Status == RecordingStatus.Failed)
                .Where(r => r.CompositionStatus == null
                         || r.CompositionStatus == RecordingCompositionStatus.Completed
                         || r.CompositionStatus == RecordingCompositionStatus.Failed)
                .Where(r => !skipped.Contains(r.Id))
                .OrderBy(r => r.Id)
                .Take(options.BatchSize)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (batch.Count == 0)
                break;

            batches++;

            foreach (var recording in batch)
            {
                if (await DeleteObjectsAsync(recording, ct).ConfigureAwait(false))
                {
                    db.SessionRecordings.Remove(recording);
                    deleted++;
                }
                else
                {
                    skip.Add(recording.Id);
                    failed++;
                }
            }

            // Har paket o'z tranzaksiyasida: yarmida qulasa ham o'chirilgan
            // fayllarning qatorlari allaqachon commit bo'lgan.
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (batch.Count < options.BatchSize)
                break;
        }

        if (deleted == 0 && failed == 0)
            return JobRunResult.Nothing;

        // 🔴 HAR YURISH LOGDA: "nega bu darsning yozuvi yo'q?" degan savolga
        // javob AYNAN shu qatordan topiladi.
        RetentionLog.Purged(logger, deleted, failed, days, cutoff);

        return new JobRunResult(
            Processed: deleted,
            Skipped: failed,
            Note: string.Create(CultureInfo.InvariantCulture, $"{days} kun, kesim {cutoff:yyyy-MM-dd}"));
    }

    /// <summary>
    /// Yozuvning ombordagi HAMMA obyektini o'chiradi. <c>false</c> — kamida
    /// bittasi o'chmadi, qator qoladi va keyingi yurishda qayta uriniladi.
    /// </summary>
    private async Task<bool> DeleteObjectsAsync(SessionRecording recording, CancellationToken ct)
    {
        var keys = recording.Tracks
            .Select(t => t.ObjectKey)
            .Append(recording.ObjectKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            try
            {
                await storage.DeleteAsync(key, ct).ConfigureAwait(false);
            }
            catch (ServiceUnavailableException ex)
            {
                RetentionLog.DeleteFailed(logger, ex, recording.Id, key);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Saqlash muddati (kun) — panel qiymati, QAT'IY cheklangan.
    ///
    /// ★ <c>public static</c>: ro'yxat ekranlari ham ("yozuv N kungacha
    /// saqlanadi") AYNAN shu qiymatni ko'rsatishi kerak — ikki joyda ikki
    /// xil o'qish bo'lsa, ekran vazifadan boshqa raqamni aytardi.
    /// </summary>
    public static async Task<int> ResolveDaysAsync(ISettingsResolver settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!SettingsRegistry.TryGet(SettingsRegistry.Keys.RecordingsRetentionDays, out var definition))
            return DefaultDays;

        var resolved = await settings.ResolveAsync(definition, ct).ConfigureAwait(false);

        var days = SettingValueParser.TryReadDecimal(definition, resolved.Value, out var value)
            ? (int)value
            : DefaultDays;

        return Math.Clamp(days, MinDays, MaxDays);
    }
}

/// <summary>
/// Tozalashning TEXNIK chegaralari (<see cref="ChatRetentionSettings"/> bilan
/// AYNI naqsh). Siyosat (necha kun) — paneldan, bu yerda faqat "qanchalik
/// tez-tez va qanday bo'laklarda".
/// </summary>
public sealed record RecordingRetentionSettings(
    TimeSpan Interval,
    int BatchSize,
    int MaxBatchesPerRun)
{
    /// <summary>
    /// Soatiga bir marta, 25 tadan, bir yurishda ko'pi bilan 20 paket
    /// (500 yozuv). Kuniga ~25–30 dars yoziladi, ya'ni odatiy yurish
    /// 0–2 qatorni oladi; chegara faqat birinchi yoqilishdagi arxiv uchun.
    /// </summary>
    public static RecordingRetentionSettings Default { get; } =
        new(TimeSpan.FromHours(1), BatchSize: 25, MaxBatchesPerRun: 20);
}

/// <summary>Manba-generatsiyali log metodlari (CA1848). EventId makoni: <c>6680–6689</c>.</summary>
internal static partial class RetentionLog
{
    [LoggerMessage(
        EventId = 6680,
        Level = LogLevel.Warning,
        Message = "Yozuvlar muddati: {Deleted} ta yozuv o'chirildi, {Failed} tasi keyingi yurishga qoldi "
                  + "(muddat {Days} kun, kesim {Cutoff:O}).")]
    internal static partial void Purged(
        ILogger logger, int deleted, int failed, int days, DateTimeOffset cutoff);

    [LoggerMessage(
        EventId = 6681,
        Level = LogLevel.Warning,
        Message = "Yozuvlar muddati: obyektni o'chirib bo'lmadi, qator qoldirildi. yozuv={RecordingId} kalit={ObjectKey}")]
    internal static partial void DeleteFailed(
        ILogger logger, Exception exception, long recordingId, string objectKey);

    [LoggerMessage(
        EventId = 6682,
        Level = LogLevel.Debug,
        Message = "Yozuvlar muddati: ombor sozlanmagan — hech nima o'chirilmadi.")]
    internal static partial void StorageNotConfigured(ILogger logger);
}
