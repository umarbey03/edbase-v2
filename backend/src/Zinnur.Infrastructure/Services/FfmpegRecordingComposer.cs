using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Recordings.Services;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TUNGI YIG'UVCHI — ffmpeg ADAPTERI
/// ════════════════════════════════════════════════════════════════════════
///
/// <see cref="IRecordingComposer"/> ning amalga oshirilishi: XOM fayllarni
/// diskka tushiradi, o'lchaydi, REJADAGI filtr grafi bilan kodlaydi,
/// natijani tekshiradi va omborga qo'yadi.
///
/// 🔴 BU SINF FILTR GRAFINI QURMAYDI. Graf ham, kirishlar tartibi ham,
/// vaqt o'qi ham <c>RecordingCompositionPlanner</c> da — SOF funksiyada —
/// hisoblanadi va bu yerga TAYYOR satr bo'lib keladi. Sabab: aynan o'sha
/// hisob eng xatoga moyil qism va u protsesssiz tekshirilishi SHART.
/// Bu yerga bitta <c>if</c> qo'shilsa, o'sha shart buzilardi.
///
/// ── NIMA UCHUN FAYLLAR DISKKA TUSHIRILADI ───────────────────────────────
///
/// ffmpeg'ga imzolangan havola BERILMAYDI (§4.5-2):
///
///   1) havolaning umri 15 daqiqa, kodlash esa SOATLAB davom etadi —
///      havola ish o'rtasida tugab, ffmpeg buni faylning oxiri deb qabul
///      qilardi va YARIM videoni muvaffaqiyat sifatida qaytarardi;
///   2) ffmpeg HTTP ustida orqaga-oldinga izlaydi, R2 esa bunday
///      so'rovlarda sekin va uzilganda yomon qayta uriniladi;
///   3) R2 dan BIZNING serverimizga chiqish trafigi BEPUL, ya'ni oqim
///      uzatishning tejamkorlik dalili ham yo'q.
///
/// ── ISHCHI PAPKA HAR HOLDA O'CHIRILADI ──────────────────────────────────
///
/// Bitta ish diskda ~6 GB egallaydi (xom kirishlar + natija + faststart
/// uchun vaqtinchalik nusxa). <c>finally</c> dagi o'chirish bekor
/// qilinganda ham ishlaydi; konteyner o'ldirilganda esa ishlamaydi va
/// o'shani <see cref="CleanScratchAsync"/> tozalaydi.
/// </summary>
public sealed class FfmpegRecordingComposer(
    IRecordingStorage storage,
    FfmpegComposerSettings settings,
    ILogger<FfmpegRecordingComposer> logger) : IRecordingComposer
{
    /// <summary>
    /// Natijaning uzunligi vaqt o'qidan shuncha soniyadan ko'p farq
    /// qilsa — yig'ish NOSOZ deb hisoblanadi (§4.5-6).
    ///
    /// ★ NIMA UCHUN TEKSHIRUV UMUMAN BOR: ffmpeg nol kod bilan chiqib,
    /// lekin kirishning yarmini o'qib qo'yishi mumkin (buzuq xom fayl,
    /// diskda joy tugashi). Bunda "muvaffaqiyat" deb yozilgan yozuv
    /// ochilganda 10 daqiqada tugab qolardi va buni faqat o'quvchi
    /// aytardi.
    /// </summary>
    private const double DurationToleranceSeconds = 2;

    private const string OutputFileName = "out.mp4";

    /// <inheritdoc />
    public async Task<CompositionResult> ComposeAsync(
        CompositionPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var scratch = ScratchDirectoryOf(plan.RecordingId);

        // ⚠️ BOSHIDA HAM O'CHIRILADI, faqat oxirida emas: qulagan
        //    ishchidan qolgan yarim fayllar ustiga yozish — SPEC §4.4
        //    ATAYLAB taqiqlagan narsa (yarim mp4 da `moov` atomi yo'q).
        SafeDelete(scratch);
        Directory.CreateDirectory(scratch);

        try
        {
            return await ComposeInScratchAsync(plan, scratch, ct).ConfigureAwait(false);
        }
        finally
        {
            SafeDelete(scratch);
        }
    }

    /// <inheritdoc />
    public Task<int> CleanScratchAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var removed = 0;

        if (!Directory.Exists(settings.ScratchPath))
            return Task.FromResult(removed);

        var deadline = DateTime.UtcNow - maxAge;

        foreach (var directory in Directory.EnumerateDirectories(settings.ScratchPath))
        {
            ct.ThrowIfCancellationRequested();

            if (Directory.GetLastWriteTimeUtc(directory) > deadline) continue;

            SafeDelete(directory);
            FfmpegLog.ScratchCleaned(logger, directory);

            removed++;
        }

        return Task.FromResult(removed);
    }

    // ═════════════════════════════════════════════════════════ asosiy oqim

    private async Task<CompositionResult> ComposeInScratchAsync(
        CompositionPlan plan, string scratch, CancellationToken ct)
    {
        // ── 1) XOM FAYLLARNI DISKKA TUSHIRAMIZ ────────────────────────
        var missing = new List<long>();

        foreach (var input in plan.Inputs)
        {
            var path = Path.Combine(scratch, input.FileName);

            if (!await DownloadAsync(input.ObjectKey, path, ct).ConfigureAwait(false))
            {
                FfmpegLog.RawMissing(logger, plan.RecordingId, input.TrackId, input.ObjectKey);
                missing.Add(input.TrackId);
            }
        }

        if (missing.Count > 0)
        {
            // Bo'lak YO'QOLGAN — butun darsni yiqitmaymiz. Chaqiruvchi bu
            // qatorlarni yopadi va keyingi urinish reja ULARSIZ quriladi
            // (`CompositionResult.MissingTrackIds`).
            return CompositionResult.Fail(
                "Ba'zi xom bo'laklar omborda topilmadi.", missingTrackIds: missing);
        }

        // ── 2) HAR FAYLNING HAQIQIY UZUNLIGI (§9.1-1) ─────────────────
        var probes = new List<ProbedTrackDuration>(plan.Inputs.Count);

        foreach (var input in plan.Inputs)
        {
            var path = Path.Combine(scratch, input.FileName);
            var duration = await ProbeDurationAsync(path, ct).ConfigureAwait(false);

            if (duration is null) continue;

            probes.Add(new ProbedTrackDuration(input.TrackId, (int)Math.Round(duration.Value * 1000)));
        }

        // ── 3) KODLASH ────────────────────────────────────────────────
        var output = Path.Combine(scratch, OutputFileName);

        var encode = await RunAsync(
            settings.FfmpegPath, BuildArguments(plan, scratch, output), ct).ConfigureAwait(false);

        if (encode.ExitCode != 0)
        {
            FfmpegLog.EncodeFailed(logger, plan.RecordingId, encode.ExitCode, encode.Error);

            return CompositionResult.Fail(
                $"Video yig'ilmadi (ffmpeg kodi {encode.ExitCode.ToString(CultureInfo.InvariantCulture)}).",
                probes);
        }

        // ── 4) NATIJANI TEKSHIRAMIZ ───────────────────────────────────
        var verified = await VerifyAsync(output, plan, ct).ConfigureAwait(false);

        if (verified.Error is { Length: > 0 } reason)
        {
            FfmpegLog.VerifyFailed(logger, plan.RecordingId, reason);

            return CompositionResult.Fail(reason, probes);
        }

        // ── 5) YUKLASH — OXIRGI QADAM ─────────────────────────────────
        //
        // ★ Bitta `PUT`: kalit YO YO'Q, YO TO'LIQ. O'quvchi hech qachon
        //   yarim faylni ko'rmaydi (`IRecordingStorage.PutAsync`).
        var info = new FileInfo(output);

        try
        {
            await using var content = File.OpenRead(output);

            await storage
                .PutAsync(plan.TargetObjectKey, content, info.Length, "video/mp4", ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            FfmpegLog.UploadFailed(logger, ex, plan.RecordingId, plan.TargetObjectKey);

            return CompositionResult.Fail("Tayyor yozuvni omborga yuklab bo'lmadi.", probes);
        }

        FfmpegLog.Composed(
            logger, plan.RecordingId, info.Length, verified.DurationSeconds, plan.TargetObjectKey);

        return CompositionResult.Ok(info.Length, (int)Math.Round(verified.DurationSeconds), probes);
    }

    /// <summary>
    /// ffmpeg chaqiruvi (§4.6).
    ///
    /// ⚠️ <c>-itsoffset</c> HAR KIRISHDAN OLDIN turishi SHART: u KIRISH
    /// parametri, ya'ni o'zidan keyingi <c>-i</c> ga tegishli. Chiqish
    /// tomonga o'tib qolsa ffmpeg uni jimgina e'tiborsiz qoldirardi va
    /// butun tasvir vaqt o'qining boshiga yig'ilib qolardi.
    ///
    /// ⚠️ OVOZ KIRISHIDA <c>-itsoffset</c> YO'Q — reja unga <c>0</c>
    /// beradi va uning o'rni filtr grafida (<c>adelay</c>). Sabab
    /// <c>CompositionInput.ItsOffsetSeconds</c> izohida.
    /// </summary>
    private static List<string> BuildArguments(CompositionPlan plan, string scratch, string output)
    {
        var args = new List<string>(48)
        {
            "-hide_banner",

            // Progress qatorlari `stderr` ga har soniyada yoziladi va 90
            // daqiqada megabaytlarga yetadi — tashxis uchun kerakli
            // xato qatorlari o'sha oqimda ko'rinmay ketardi.
            "-nostats",
            "-loglevel", "error",
            "-y",
        };

        foreach (var input in plan.Inputs)
        {
            if (input.ItsOffsetSeconds > 0)
            {
                args.Add("-itsoffset");
                args.Add(input.ItsOffsetSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            }

            args.Add("-i");
            args.Add(Path.Combine(scratch, input.FileName));
        }

        args.Add("-filter_complex");
        args.Add(plan.FilterGraph);

        args.Add("-map");
        args.Add(CompositionPlan.VideoLabel);
        args.Add("-map");
        args.Add(CompositionPlan.AudioLabel);

        args.AddRange([
            "-c:v", "libx264",
            "-preset", plan.Preset,
            "-crf", plan.Crf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p",
            "-profile:v", "high",
            "-level", "4.1",
            "-g", "60",
            "-threads", "0",
            "-c:a", "aac",
            "-b:a", "128k",
            "-ac", "2",
            "-ar", "48000",
            "-max_muxing_queue_size", "1024",

            // `moov` atomi faylning BOSHIGA ko'chiriladi — brauzer butun
            // faylni yuklab bo'lmasdan o'ynay boshlaydi. Buning uchun
            // ikkinchi LOKAL yurish kerak, ya'ni kodlash tarmoqqa emas,
            // diskka bo'lishi SHART.
            "-movflags", "+faststart",
        ]);

        args.Add(output);

        return args;
    }

    // ═════════════════════════════════════════════════════════ ombor

    /// <summary><c>false</c> — obyekt omborda yo'q.</summary>
    private async Task<bool> DownloadAsync(string objectKey, string path, CancellationToken ct)
    {
        await using var stored = await storage.OpenReadAsync(objectKey, ct).ConfigureAwait(false);

        if (stored is null) return false;

        await using var file = File.Create(path);

        await stored.Content.CopyToAsync(file, ct).ConfigureAwait(false);

        return true;
    }

    // ═════════════════════════════════════════════════════════ ffprobe

    /// <summary>Faylning uzunligi (sekund) yoki <c>null</c> — o'qib bo'lmadi.</summary>
    private async Task<double?> ProbeDurationAsync(string path, CancellationToken ct)
    {
        var probe = await RunAsync(
            settings.FfprobePath,
            ["-v", "error", "-show_entries", "format=duration", "-of", "json", path],
            ct).ConfigureAwait(false);

        if (probe.ExitCode != 0)
        {
            FfmpegLog.ProbeFailed(logger, path, probe.Error);

            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(probe.Output);

            return json.RootElement.TryGetProperty("format", out var format)
                && format.TryGetProperty("duration", out var duration)
                && double.TryParse(
                    duration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }
        catch (JsonException ex)
        {
            FfmpegLog.ProbeFailed(logger, path, ex.Message);

            return null;
        }
    }

    /// <summary>
    /// Natijani tekshiradi: AYNAN bitta video + bitta ovoz oqimi va
    /// uzunlik vaqt o'qiga mos (§4.5-6).
    ///
    /// ★ IKKALA OQIM HAM SHART: ovozsiz yoki tasvirsiz mp4 ni brauzerning
    /// <c>&lt;video&gt;</c> elementi va ba'zi pleyerlar yomon ochadi.
    /// Reja buni allaqachon kafolatlaydi (jimlik oqimi / qora fon), bu
    /// yerdagi tekshiruv esa o'sha kafolat BUZILGANINI aniqlaydi.
    /// </summary>
    private async Task<(string? Error, double DurationSeconds)> VerifyAsync(
        string output, CompositionPlan plan, CancellationToken ct)
    {
        var probe = await RunAsync(
            settings.FfprobePath,
            [
                "-v", "error",
                "-show_entries", "format=duration:stream=codec_type",
                "-of", "json",
                output,
            ],
            ct).ConfigureAwait(false);

        if (probe.ExitCode != 0)
            return ("Tayyor faylni o'qib bo'lmadi.", 0);

        int video;
        int audio;
        double duration;

        try
        {
            using var json = JsonDocument.Parse(probe.Output);
            var root = json.RootElement;

            var streams = root.TryGetProperty("streams", out var list)
                ? list.EnumerateArray()
                    .Select(s => s.TryGetProperty("codec_type", out var type) ? type.GetString() : null)
                    .ToList()
                : [];

            video = streams.Count(s => string.Equals(s, "video", StringComparison.Ordinal));
            audio = streams.Count(s => string.Equals(s, "audio", StringComparison.Ordinal));

            duration =
                root.TryGetProperty("format", out var format)
                && format.TryGetProperty("duration", out var value)
                && double.TryParse(
                    value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 0;
        }
        catch (JsonException)
        {
            return ("Tayyor faylni o'qib bo'lmadi.", 0);
        }

        if (video != 1 || audio != 1)
            return ("Tayyor faylda video yoki ovoz oqimi yetishmadi.", duration);

        if (duration <= 0)
            return ("Tayyor faylning uzunligi noldan katta emas.", duration);

        var drift = Math.Abs(duration - plan.TimelineSeconds);

        if (drift > DurationToleranceSeconds)
        {
            FfmpegLog.DurationMismatch(logger, plan.RecordingId, plan.TimelineSeconds, duration);

            return ("Tayyor faylning uzunligi darsning uzunligiga mos kelmadi.", duration);
        }

        return (null, duration);
    }

    // ═════════════════════════════════════════════════════════ protsess

    /// <summary>
    /// Tashqi protsessni yurgizadi va ikkala oqimini CHEGARALANGAN holda
    /// yig'adi.
    ///
    /// ⚠️ CHEGARA MAJBURIY: 90 daqiqalik kodlashning <c>stderr</c> i
    /// cheklanmasa xotirada megabaytlab satr to'planardi — kompozitor
    /// konteynerining butun budjeti 2 GB.
    ///
    /// ⚠️ IKKALA OQIM HAM O'QILISHI SHART: faqat bittasi o'qilsa,
    /// ikkinchisining quvuri to'lganda protsess YOZISHDA muzlab qolardi
    /// va bu klassik boshi berk ko'chadir.
    /// </summary>
    private async Task<ProcessOutcome> RunAsync(
        string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };

        var output = new BoundedText();
        var error = new BoundedText();

        process.OutputDataReceived += (_, e) => output.Append(e.Data);
        process.ErrorDataReceived += (_, e) => error.Append(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TerminateAsync(process).ConfigureAwait(false);

            throw;
        }

        return new ProcessOutcome(process.ExitCode, output.ToString(), error.ToString());
    }

    /// <summary>
    /// Protsessni to'xtatadi: avval MULOYIM (<c>SIGTERM</c>), muhlat
    /// o'tgach MAJBURAN (<c>SIGKILL</c>) — butun daraxti bilan.
    ///
    /// 🔴 YETIM ffmpeg QOLDIRILMAYDI. Tungi oyna tugaganda yoki konteyner
    /// to'xtatilganda qolgan ffmpeg protsessi 3.5 yadroni yeb, ertalabki
    /// jonli darsni sekinlashtirardi — va uni hech kim qidirmasdi,
    /// chunki uni yurgizgan konteyner allaqachon yo'q.
    ///
    /// ⚠️ .NET DA <c>SIGTERM</c> YUBORISHNING TO'G'RIDAN-TO'G'RI YO'LI
    /// YO'Q: <c>Process.Kill</c> Unix'da DOIM <c>SIGKILL</c>.
    /// <c>[LibraryImport]</c> orqali <c>libc</c> ga murojaat qilish esa
    /// butun loyihada <c>AllowUnsafeBlocks</c> ni yoqishni talab qilardi
    /// — bitta signal uchun juda qimmat narx. Shuning uchun tizimning
    /// <c>kill</c> buyrug'i ishlatiladi; u yo'q bo'lsa (yoki Windows
    /// bo'lsa) darhol majburiy to'xtatishga o'tiladi.
    /// </summary>
    private async Task TerminateAsync(Process process)
    {
        try
        {
            if (process.HasExited) return;

            if (SendSigterm(process.Id))
            {
                using var grace = new CancellationTokenSource(settings.StopGrace);

                await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Muhlat tugadi — pastda majburan to'xtatiladi.
        }
        catch (InvalidOperationException)
        {
            return;         // protsess allaqachon tugagan
        }

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            FfmpegLog.KillFailed(logger, ex);
        }
    }

    /// <summary><c>false</c> — signal yuborilmadi, majburiy yo'lga o'tiladi.</summary>
    private bool SendSigterm(int pid)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return false;

        try
        {
            using var kill = Process.Start(new ProcessStartInfo
            {
                FileName = "kill",
                ArgumentList = { "-TERM", pid.ToString(CultureInfo.InvariantCulture) },
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            return kill is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            FfmpegLog.KillFailed(logger, ex);

            return false;
        }
    }

    // ═════════════════════════════════════════════════════════ yordamchilar

    private string ScratchDirectoryOf(long recordingId) =>
        Path.Combine(settings.ScratchPath, recordingId.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Papkani o'chiradi va HECH QACHON istisno tashlamaydi.
    ///
    /// ★ NIMA UCHUN: bu <c>finally</c> dan chaqiriladi. U yerdagi istisno
    /// asl xatoni (ffmpeg nega yiqilgani) BUTUNLAY yashirib yuborardi.
    /// </summary>
    private void SafeDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FfmpegLog.ScratchNotRemoved(logger, ex, directory);
        }
    }

    private sealed record ProcessOutcome(int ExitCode, string Output, string Error);

    /// <summary>
    /// Chegaralangan matn yig'gich: birinchi <see cref="Limit"/> belgidan
    /// keyin qolgani TASHLANADI.
    ///
    /// ★ NIMA UCHUN BOSHI SAQLANADI, OXIRI EMAS: ffmpeg va ffprobe
    /// nosozlik sababini BIRINCHI qatorlarda yozadi; keyingi qatorlar
    /// odatda o'sha sababning oqibatlari.
    /// </summary>
    private sealed class BoundedText
    {
        private const int Limit = 8_000;

        private readonly StringBuilder _text = new();

        public void Append(string? line)
        {
            if (line is null || _text.Length >= Limit) return;

            lock (_text)
            {
                if (_text.Length >= Limit) return;

                _text.AppendLine(line);
            }
        }

        public override string ToString()
        {
            lock (_text) return _text.ToString();
        }
    }
}

/// <summary>
/// Yig'uvchining muhitga oid sozlamalari.
///
/// ★ NIMA UCHUN ALOHIDA YOZUV VA <c>IOptions</c> EMAS —
/// <c>RecordingWatchdogSettings</c> dagi AYNI naqsh: qiymatlar DI
/// ro'yxatidan o'tkazishda uzatiladi, ya'ni sinf konfiguratsiya tizimini
/// bilmaydi va testda istalgan yo'l bilan yurgiziladi.
/// </summary>
/// <param name="ScratchPath">
/// Ishchi papkalarning ildizi (<c>Composition:ScratchPath</c>). Bitta ish
/// ~6 GB egallaydi, shuning uchun bu ALOHIDA volume bo'lishi kerak.
/// </param>
/// <param name="FfmpegPath">ffmpeg ning yo'li yoki nomi.</param>
/// <param name="FfprobePath">ffprobe ning yo'li yoki nomi.</param>
/// <param name="StopGrace">
/// <c>SIGTERM</c> dan keyin <c>SIGKILL</c> gacha kutish muddati.
///
/// ⚠️ Konteynerning <c>stop_grace_period</c> i bundan KATTA bo'lishi
/// SHART, aks holda Docker bizni ffmpeg'dan oldin o'ldiradi va yetim
/// protsess qoladi.
/// </param>
public sealed record FfmpegComposerSettings(
    string ScratchPath,
    string FfmpegPath,
    string FfprobePath,
    TimeSpan StopGrace)
{
    /// <summary>Konteynerdagi standart qiymatlar (§4.2).</summary>
    public static FfmpegComposerSettings Default { get; } = new(
        ScratchPath: "/var/lib/zinnur/compose",
        FfmpegPath: "ffmpeg",
        FfprobePath: "ffprobe",
        StopGrace: TimeSpan.FromSeconds(10));
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848). EventId makoni:
/// <c>6632–6649</c>.
/// </summary>
internal static partial class FfmpegLog
{
    [LoggerMessage(
        EventId = 6632,
        Level = LogLevel.Information,
        Message = "Tungi yig'ish tayyor: yozuv={RecordingId} hajm={SizeBytes} "
                  + "uzunlik={DurationSeconds}s kalit={ObjectKey}")]
    internal static partial void Composed(
        ILogger logger, long recordingId, long sizeBytes, double durationSeconds, string objectKey);

    [LoggerMessage(
        EventId = 6633,
        Level = LogLevel.Error,
        Message = "ffmpeg yiqildi: yozuv={RecordingId} kod={ExitCode}\n{Error}")]
    internal static partial void EncodeFailed(
        ILogger logger, long recordingId, int exitCode, string error);

    [LoggerMessage(
        EventId = 6634,
        Level = LogLevel.Error,
        Message = "Tayyor fayl tekshiruvdan o'tmadi: yozuv={RecordingId} sabab={Reason}")]
    internal static partial void VerifyFailed(ILogger logger, long recordingId, string reason);

    /// <summary>
    /// ⚠️ Bu qator YIG'ISH YIQILGANIDA yoziladi va sabab odatda A/V
    /// siljish emas, uzilib qolgan kirish faylidir.
    /// </summary>
    [LoggerMessage(
        EventId = 6635,
        Level = LogLevel.Warning,
        Message = "Tayyor faylning uzunligi mos kelmadi: yozuv={RecordingId} "
                  + "kutilgan={ExpectedSeconds}s o'lchangan={ActualSeconds}s")]
    internal static partial void DurationMismatch(
        ILogger logger, long recordingId, double expectedSeconds, double actualSeconds);

    [LoggerMessage(
        EventId = 6636,
        Level = LogLevel.Warning,
        Message = "ffprobe faylni o'qiy olmadi: fayl={Path}\n{Error}")]
    internal static partial void ProbeFailed(ILogger logger, string path, string error);

    /// <summary>
    /// 🔴 Qator <c>Completed</c> bo'lgan, ya'ni fayl bir paytlar BOR edi.
    /// Bu xabar ombordagi yo'qotishni ko'rsatadi va uni tekshirish kerak.
    /// </summary>
    [LoggerMessage(
        EventId = 6637,
        Level = LogLevel.Error,
        Message = "Xom bo'lak omborda topilmadi: yozuv={RecordingId} bo'lak={TrackId} "
                  + "kalit={ObjectKey}")]
    internal static partial void RawMissing(
        ILogger logger, long recordingId, long trackId, string objectKey);

    [LoggerMessage(
        EventId = 6638,
        Level = LogLevel.Error,
        Message = "Tayyor yozuvni omborga yuklab bo'lmadi: yozuv={RecordingId} kalit={ObjectKey}")]
    internal static partial void UploadFailed(
        ILogger logger, Exception exception, long recordingId, string objectKey);

    [LoggerMessage(
        EventId = 6639,
        Level = LogLevel.Warning,
        Message = "Ishchi papkani o'chirib bo'lmadi: {Directory}")]
    internal static partial void ScratchNotRemoved(
        ILogger logger, Exception exception, string directory);

    [LoggerMessage(
        EventId = 6640,
        Level = LogLevel.Information,
        Message = "Eski ishchi papka tozalandi: {Directory}")]
    internal static partial void ScratchCleaned(ILogger logger, string directory);

    [LoggerMessage(
        EventId = 6641,
        Level = LogLevel.Warning,
        Message = "ffmpeg protsessini majburan to'xtatib bo'lmadi.")]
    internal static partial void KillFailed(ILogger logger, Exception exception);
}
