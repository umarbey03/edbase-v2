using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Recordings.Services;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Api;
using Xunit.Abstractions;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// HAQIQIY ffmpeg BILAN TO'LIQ AYLANMA (SPEC-RECORDING-V2 §4.5–4.6)
/// ════════════════════════════════════════════════════════════════════════
///
/// Bu yerda hech narsa soxta emas: haqiqiy xom fayllar (ffmpeg yasaydi),
/// haqiqiy ombor (MinIO), haqiqiy <c>ffmpeg</c>/<c>ffprobe</c> va haqiqiy
/// baza. Tekshirilayotgan narsa — REJA VA IJRO BIR-BIRIGA MOS KELISHI:
/// planner yozgan <c>-filter_complex</c> satrini ffmpeg qabul qiladimi va
/// natijada AYNAN kutilgan uzunlikdagi, ikkala oqimli fayl chiqadimi.
///
/// 🔴 NIMA UCHUN BU TEST BOR: planner testlari satrni tekshiradi, lekin
/// SATR TO'G'RI BO'LIB, ffmpeg uni RAD ETISHI mumkin. Aynan shunday
/// bo'lgan ham: SPEC §4.6 da ovoz bo'g'ini <c>first_pta=0</c> deb
/// yozilgan — ffmpeg'da bunday parametr YO'Q va u butun grafni rad
/// etardi. Bunday xatoni faqat haqiqiy protsess ushlaydi.
///
/// ⚠️ ffmpeg KERAK. Ishlab chiqarishda u <c>compositor</c> konteynerida
/// (<c>runtime-media</c> bosqichi), testda esa test konteynerida bo'lishi
/// kerak. Yo'q bo'lsa test JIMGINA o'tkazib yuboriladi va sababi
/// chiqishga yoziladi — aks holda ffmpeg'siz mashinada butun to'plam
/// sababsiz qizarardi.
/// </summary>
public sealed class FfmpegCompositionTests(CompositionFactory factory, ITestOutputHelper output)
    : IClassFixture<CompositionFactory>
{
    /// <summary>Vaqt o'qining boshi — LiveKit vaqtlari shunga nisbatan beriladi.</summary>
    private static readonly DateTimeOffset Start =
        new(2026, 9, 5, 5, 0, 0, TimeSpan.Zero);

    /// <summary>Natija uzunligi vaqt o'qidan shuncha soniyadan ko'p farq qilmasligi kerak (§4.5-6).</summary>
    private const double Tolerance = 2;

    // ═══════════════════════════════════════════════════ to'liq aylanma

    /// <summary>
    /// 🔴 PRODUKSIYA SHAKLI: bitta uzluksiz ovoz + kamera + dars o'rtasida
    /// yoqilgan EKRAN ULASHISH.
    ///
    /// Butun yo'l tekshiriladi: navbatdan egallash -> reja -> yuklab
    /// olish -> <c>ffprobe</c> -> kodlash -> tekshiruv -> YUKLASH ->
    /// yozuvni yakunlash -> xom fayllarni tozalash.
    /// </summary>
    [Fact]
    public async Task FullRoundTrip_ProducesAPlayableFileAtTheExistingKey()
    {
        if (!await FfmpegAvailableAsync()) return;

        var lesson = await NewLessonAsync();

        // 0–12 s ovoz, 0–6 s kamera, 3–9 s ekran ulashish.
        await AddMediaAsync(lesson, RecordingTrackKind.RoomAudio, seconds: 12, at: 0);
        await AddMediaAsync(lesson, RecordingTrackKind.CameraVideo, seconds: 6, at: 0);
        await AddMediaAsync(lesson, RecordingTrackKind.ScreenVideo, seconds: 6, at: 3);

        var result = await factory.RunCompositionAsync();

        result.Outcome.Should().Be(CompositionCycleOutcome.Completed);

        var row = await CompositionWorld.ReloadAsync(factory, lesson.RecordingId);

        row.Status.Should().Be(RecordingStatus.Completed);
        row.SizeBytes.Should().BeGreaterThan(0);
        row.DurationSeconds.Should().BeInRange(10, 14);
        row.RawPurgedAt.Should().NotBeNull("xom fayllar yig'ishdan keyin o'chiriladi");

        // ── Natija HAQIQATAN o'ynaydigan fayl ekanini o'lchaymiz ──────
        var media = await ProbeOutputAsync(row.ObjectKey);

        media.Video.Should().Be(1, "brauzerning <video> elementi ikkala oqimni kutadi");
        media.Audio.Should().Be(1);
        media.Duration.Should().BeApproximately(12, Tolerance);

        // Xom fayllar HAQIQATAN ombordan ketgan.
        foreach (var track in row.Tracks)
            (await HeadAsync(track.ObjectKey)).Should().BeNull(track.TrackSid);
    }

    /// <summary>
    /// 🔴 DARSNING UZUNLIGI AHAMIYATSIZ.
    ///
    /// 2026-09-04 dagi nosozlik AYNAN uzunlikka bog'liq edi: 10 daqiqadan
    /// uzun har qanday dars jimgina yo'qolardi, qisqalari esa ishlardi —
    /// shuning uchun u oylab sezilmadi. Bu yerda qisqa va UZUNROQ dars
    /// AYNI yo'ldan o'tkaziladi va ikkalasi ham to'g'ri uzunlik beradi.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(45)]
    public async Task LessonLength_DoesNotChangeTheOutcome(int seconds)
    {
        if (!await FfmpegAvailableAsync()) return;

        var lesson = await NewLessonAsync();

        await AddMediaAsync(lesson, RecordingTrackKind.RoomAudio, seconds, at: 0);
        await AddMediaAsync(lesson, RecordingTrackKind.CameraVideo, seconds, at: 0);

        (await factory.RunCompositionAsync()).Outcome.Should().Be(CompositionCycleOutcome.Completed);

        var row = await CompositionWorld.ReloadAsync(factory, lesson.RecordingId);
        var media = await ProbeOutputAsync(row.ObjectKey);

        media.Video.Should().Be(1);
        media.Audio.Should().Be(1);
        media.Duration.Should().BeApproximately(seconds, Tolerance);
    }

    /// <summary>
    /// Ustoz kamerani UMUMAN yoqmagan dars: qora fon + haqiqiy ovoz.
    /// Bu MUVAFFAQIYAT (§4.1-6) va natija baribir ikkala oqimli bo'ladi.
    /// </summary>
    [Fact]
    public async Task AudioOnlyLesson_StillProducesAPlayableFile()
    {
        if (!await FfmpegAvailableAsync()) return;

        var lesson = await NewLessonAsync();

        await AddMediaAsync(lesson, RecordingTrackKind.RoomAudio, seconds: 8, at: 0);

        (await factory.RunCompositionAsync()).Outcome.Should().Be(CompositionCycleOutcome.Completed);

        var row = await CompositionWorld.ReloadAsync(factory, lesson.RecordingId);
        var media = await ProbeOutputAsync(row.ObjectKey);

        media.Video.Should().Be(1, "qora fon ham VIDEO oqimi");
        media.Audio.Should().Be(1);
        media.Duration.Should().BeApproximately(8, Tolerance);
    }

    /// <summary>
    /// Mikser yiqilgan dars: tasvir bor, ovoz yo'q. Natijada JIMLIK oqimi
    /// bo'ladi — ovozsiz mp4 ni pleyerlar yomon ochadi (§4.6).
    /// </summary>
    [Fact]
    public async Task VideoOnlyLesson_GetsASilentAudioStream()
    {
        if (!await FfmpegAvailableAsync()) return;

        var lesson = await NewLessonAsync();

        await AddMediaAsync(lesson, RecordingTrackKind.CameraVideo, seconds: 6, at: 0);

        (await factory.RunCompositionAsync()).Outcome.Should().Be(CompositionCycleOutcome.Completed);

        var row = await CompositionWorld.ReloadAsync(factory, lesson.RecordingId);

        row.CompositionError.Should().Be("Dars ovozi yozib olinmadi.");

        var media = await ProbeOutputAsync(row.ObjectKey);

        media.Video.Should().Be(1);
        media.Audio.Should().Be(1, "jimlik ham OVOZ oqimi");
    }

    // ═══════════════════════════════════════════════════ ishchi papka

    /// <summary>
    /// Ishchi papka HAR HOLDA o'chiriladi. Bitta ish ~6 GB egallaydi;
    /// qoldiqlar bir necha kechada diskni to'ldirib, butun tungi navbatni
    /// to'xtatardi.
    /// </summary>
    [Fact]
    public async Task Scratch_IsAlwaysRemoved()
    {
        if (!await FfmpegAvailableAsync()) return;

        var lesson = await NewLessonAsync();

        await AddMediaAsync(lesson, RecordingTrackKind.RoomAudio, seconds: 4, at: 0);
        await AddMediaAsync(lesson, RecordingTrackKind.CameraVideo, seconds: 4, at: 0);

        await factory.RunCompositionAsync();

        Directory.Exists(Path.Combine(
                factory.ScratchPath,
                lesson.RecordingId.ToString(CultureInfo.InvariantCulture)))
            .Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════ yordamchilar

    private sealed record Lesson(long RecordingId, long SessionId, string ObjectKey);

    private sealed record Media(int Video, int Audio, double Duration);

    private async Task<Lesson> NewLessonAsync()
    {
        await factory.WithDbAsync(db => db.SessionRecordings.ExecuteDeleteAsync());

        factory.Clock.Set(DateTimeOffset.UtcNow);

        _world ??= await WorldBuilder.CreateAsync(factory, "ff");

        var sessionId = await RecordingWorld.AddSessionAsync(
            factory, _world.GroupId, SessionStatus.Ended, _world.Teacher.Id);

        var objectKey = $"recordings/itest/{Guid.NewGuid():N}.mp4";

        var recordingId = await CompositionWorld.AddRecordingAsync(
            factory, sessionId, objectKey: objectKey);

        return new Lesson(recordingId, sessionId, objectKey);
    }

    /// <summary>
    /// HAQIQIY xom fayl yasaydi, uni omborga qo'yadi va qatorini yozadi.
    ///
    /// ★ Video VP8/WebM — ishlab chiqarishda AYNAN shu chiqadi:
    ///   frontend <c>videoCodec</c> ni bermaydi, ya'ni livekit-client VP8
    ///   e'lon qiladi va <c>TrackEgress</c> uni QAYTA KODLAMASDAN yozadi.
    ///   Ovoz Opus/OGG — mikser so'rovida fayl turi OSHKORA <c>OGG</c>
    ///   deb belgilanadi (§3.4b).
    /// </summary>
    private async Task AddMediaAsync(
        Lesson lesson, RecordingTrackKind kind, int seconds, int at)
    {
        var video = kind is RecordingTrackKind.CameraVideo or RecordingTrackKind.ScreenVideo;
        var sid = kind == RecordingTrackKind.RoomAudio ? "ROOM" : $"TR_{Guid.NewGuid():N}"[..12];
        var key = $"raw/itest/{lesson.RecordingId}/{Guid.NewGuid():N}.{(video ? "webm" : "ogg")}";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.{(video ? "webm" : "ogg")}");

        var duration = seconds.ToString(CultureInfo.InvariantCulture);

        var arguments = video
            ? new[]
            {
                "-v", "error", "-y",
                "-f", "lavfi",
                "-i", $"testsrc=size={(kind == RecordingTrackKind.ScreenVideo ? "1280x720" : "640x480")}"
                      + $":rate=15:duration={duration}",
                "-c:v", "libvpx", "-b:v", "150k", path,
            }
            : [
                "-v", "error", "-y",
                "-f", "lavfi",
                "-i", $"sine=frequency=440:duration={duration}",
                "-c:a", "libopus", "-ar", "48000", "-ac", "2", path,
            ];

        (await RunAsync("ffmpeg", arguments)).Code.Should().Be(0, "xom fayl yasalishi kerak");

        try
        {
            await using var content = File.OpenRead(path);

            await factory.Services.GetRequiredService<IRecordingStorage>()
                .PutAsync(key, content, content.Length, video ? "video/webm" : "audio/ogg");
        }
        finally
        {
            File.Delete(path);
        }

        await CompositionWorld.AddTrackAsync(
            factory,
            lesson.RecordingId,
            kind,
            startedAt: Start.AddSeconds(at),
            endedAt: Start.AddSeconds(at + seconds),
            trackSid: sid,
            objectKey: key);
    }

    /// <summary>Tayyor faylni ombordan yuklab olib <c>ffprobe</c> bilan o'lchaydi.</summary>
    private async Task<Media> ProbeOutputAsync(string objectKey)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");

        var stored = await factory.Services.GetRequiredService<IRecordingStorage>()
            .OpenReadAsync(objectKey);

        stored.Should().NotBeNull("tayyor fayl AYNAN mavjud kalitga qo'yilishi kerak");

        try
        {
            await using (stored)
            await using (var file = File.Create(path))
            {
                await stored!.Content.CopyToAsync(file);
            }

            var probe = await RunAsync("ffprobe",
            [
                "-v", "error",
                "-show_entries", "format=duration:stream=codec_type",
                "-of", "json",
                path,
            ]);

            probe.Code.Should().Be(0, probe.Error);

            using var json = JsonDocument.Parse(probe.Output);
            var root = json.RootElement;

            var types = root.GetProperty("streams").EnumerateArray()
                .Select(s => s.GetProperty("codec_type").GetString())
                .ToList();

            return new Media(
                types.Count(t => t == "video"),
                types.Count(t => t == "audio"),
                double.Parse(
                    root.GetProperty("format").GetProperty("duration").GetString()!,
                    CultureInfo.InvariantCulture));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private Task<StoredObjectInfo?> HeadAsync(string objectKey) =>
        factory.Services.GetRequiredService<IRecordingStorage>().HeadAsync(objectKey);

    /// <summary>
    /// ffmpeg mavjudmi.
    ///
    /// 🔴 YO'Q BO'LSA TEST O'TKAZIB YUBORILADI, YIQILMAYDI — VA BU ONGLI
    /// MUROSA. Loyihada hujjatlashtirilgan test buyrug'i oddiy .NET SDK
    /// obrazini ishlatadi, unda esa ffmpeg yo'q; test qizarsa har bir
    /// dasturchi uni "o'zimniki emas" deb o'tib ketishga o'rganardi va
    /// o'shanda HAQIQIY qizil ham ko'rinmasdi.
    ///
    /// ⚠️ Sabab HAR SAFAR chiqishga yoziladi: "jimgina o'tdi" degan holat
    /// bo'lmasin.
    /// </summary>
    private async Task<bool> FfmpegAvailableAsync()
    {
        try
        {
            if ((await RunAsync("ffmpeg", ["-version"])).Code == 0) return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // pastda xabar beriladi
        }

        output.WriteLine(
            "ffmpeg topilmadi — bu test o'tkazib yuborildi. "
            + "Uni yurgizish uchun test konteynerida ffmpeg bo'lishi kerak "
            + "(docs/ASSUMPTIONS.md dagi buyruqqa qarang).");

        return false;
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(
        string fileName, string[] arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info)!;

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return (process.ExitCode, await stdout, await stderr);
    }

    private StudentWorld? _world;
}
