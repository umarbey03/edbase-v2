using System.Net;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Settings;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Persistence;
using Zinnur.IntegrationTests.Api;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// QAYSI QUVUR TANLANADI (SPEC-RECORDING-V2 §2.7 / §5.9-2)
/// ════════════════════════════════════════════════════════════════════════
///
/// 🔴 BU — YOYISHNING KALITI. M1–M6 modullari PRODUKSIYADA HECH NARSA
/// QILMAYDI: hech kim <c>TrackComposition</c> qatorini yaratmaydi, ya'ni
/// ular xatti-harakatga umuman tegmasdan qo'shildi. Tanlash mantig'i
/// (<c>AutoRecordingScheduler</c>) — yangi quvurni HAQIQATAN yoqadigan
/// yagona joy. Bu yerdagi testlar ikkita teskari xatoni ushlaydi:
///
///   • JUDA KENG — hamma guruh jimgina yangi yo'lga o'tib ketadi va
///     darslarning yozuvi ertalabgacha paydo bo'lmaydi;
///   • JUDA TOR — hech bir guruh o'tmaydi va yoyish "ishlamayapti"
///     bo'lib ko'rinadi, sababi esa hech qayerda ko'rinmaydi.
///
/// ── HAR TESTDA SOZLAMALAR OShKORA QO'YILADI ─────────────────────────────
///
/// ⚠️ Sinfdagi testlar BITTA baza va BITTA <c>AppSettings</c> jadvalini
/// baham ko'radi. Sozlamani "oldingi test qanday qoldirgan bo'lsa" deb
/// qoldirish testlarni TARTIBGA bog'lab qo'yardi — ya'ni ular yakka
/// yurganda yashil, birga yurganda qizil bo'lardi (bu loyihada bir marta
/// bo'lgan: `RecordingWorld.ClearRoomAsync` izohi).
///
/// ── NIMA UCHUN DARS API ORQALI BOSHLANADI ──────────────────────────────
///
/// <c>AutoRecordingTests</c> dagi AYNI sabab: trigger
/// <c>LiveSessionService.StartAsync</c> ichida va servisni to'g'ridan
/// chaqirish DI ulanishini hamda tranzaksiya chegarasini chetlab o'tardi.
/// </summary>
public sealed class AutoRecordingPipelineTests(RecordingFactory factory)
    : IClassFixture<RecordingFactory>
{
    private const string EnabledKey = SettingsRegistry.Keys.RecordingsTrackPipelineEnabled;

    private const string ShadowKey = SettingsRegistry.Keys.RecordingsTrackPipelineShadowGroups;

    // ================================================================= eski xulq

    /// <summary>
    /// 🔴 STANDART HOLAT — HECH NARSA O'ZGARMAGAN.
    ///
    /// Sozlama qatorlari UMUMAN yo'q (ya'ni migratsiya endi chiqqan
    /// server), guruh ustuni tegilmagan. Natija bugungi bilan AYNAN bir
    /// xil bo'lishi kerak: bitta <c>RoomComposite</c> qatori, montaj
    /// holati <c>null</c>, watchdog esa eski egress'ni boshlaydi.
    ///
    /// ★ <c>CompositionStatus</c> ning <c>null</c> ligi ALOHIDA
    ///   tasdiqlanadi: eski qatorda bo'sh bo'lmagan qiymat — XATO va u
    ///   tungi montajni o'zi bilan hech qanday aloqasi yo'q darsga olib
    ///   borardi.
    /// </summary>
    [Fact]
    public async Task GroupWithNoSetting_KeepsTheOldRoomCompositePipeline()
    {
        await ResetPipelineSettingsAsync();

        factory.Egress.FailWith = null;
        factory.Egress.Started.Clear();
        factory.Egress.StartedTracks.Clear();

        var world = await WorldBuilder.CreateAsync(factory, "pipedef");
        await SetGroupAsync(world.GroupId, RecordingPipeline.RoomComposite);

        var sessionId = await ScheduledSessionAsync(world.GroupId);
        var roomName = await RoomNameAsync(sessionId);

        await StartLessonAsync(world, sessionId);

        var recording = (await RecordingsOfAsync(sessionId)).Should().ContainSingle().Subject;

        recording.Pipeline.Should().Be(RecordingPipeline.RoomComposite);
        recording.CompositionStatus.Should().BeNull("eski yo'lda montaj bosqichi YO'Q");
        recording.Status.Should().Be(RecordingStatus.Requested);

        await factory.RunRecordingWatchdogAsync();

        factory.Egress.Started.Should().Contain(
            request => request.RoomName == roomName,
            "eski yo'l — bu ORQAGA QAYTISH yo'li va u ishlashda davom etishi shart");
    }

    /// <summary>
    /// 🔴 FAVQULODDA TORMOZ GURUH USTUNIDAN USTUN.
    ///
    /// Guruh <c>TrackComposition</c> ga qo'yilgan, lekin global kalit
    /// o'chiq. Bu — deploysiz orqaga qaytish yo'lining AYNAN o'zi: prod'da
    /// nimadir noto'g'ri ketsa, administrator bitta kalitni o'chiradi va
    /// keyingi darslar eski yo'lga qaytadi. Guruh ustunlarini birma-bir
    /// tozalash SHART EMAS.
    /// </summary>
    [Fact]
    public async Task KillSwitchOff_ForcesRoomComposite_EvenWhenTheGroupAsksForTracks()
    {
        await SetPipelineSettingsAsync(enabled: false, shadowGroups: null);

        var world = await WorldBuilder.CreateAsync(factory, "pipeoff");
        await SetGroupAsync(world.GroupId, RecordingPipeline.TrackComposition);

        var sessionId = await ScheduledSessionAsync(world.GroupId);

        await StartLessonAsync(world, sessionId);

        var recording = (await RecordingsOfAsync(sessionId)).Should().ContainSingle().Subject;

        recording.Pipeline.Should().Be(
            RecordingPipeline.RoomComposite,
            "umumiy kalit o'chiq ekan, guruh ustuni O'QILMAYDI");

        recording.CompositionStatus.Should().BeNull();
    }

    // ================================================================= yangi quvur

    /// <summary>
    /// ★★★ ASOSIY TEST: guruh yangi quvurga qo'yilgan va kalit yoqilgan.
    ///
    /// Ikki narsa BIRGA tasdiqlanadi va ikkalasi ham majburiy:
    ///
    ///   1) qator <c>TrackComposition</c> bo'lib, <c>Collecting</c>
    ///      holatida tug'iladi (dars ketmoqda, xom bo'laklar yig'ilmoqda);
    ///   2) 🔴 ESKI, CHROME'LI EGRESS BOSHLANMAYDI. Bu — butun ishning
    ///      MA'NOSI: yangi quvur arzon bo'lishi kerak. Filtr buzilsa
    ///      qator baribir to'g'ri ko'rinardi, lekin server bir darsga
    ///      ikki barobar protsessor sarflardi va buni faqat `docker stats`
    ///      ko'rsatardi.
    /// </summary>
    [Fact]
    public async Task TrackPipelineGroup_QueuesTrackRow_AndNeverStartsTheOldEgress()
    {
        await SetPipelineSettingsAsync(enabled: true, shadowGroups: null);

        factory.Egress.FailWith = null;
        factory.Egress.Started.Clear();

        var world = await WorldBuilder.CreateAsync(factory, "pipetrack");
        await SetGroupAsync(world.GroupId, RecordingPipeline.TrackComposition);

        var sessionId = await ScheduledSessionAsync(world.GroupId);
        var roomName = await RoomNameAsync(sessionId);

        await StartLessonAsync(world, sessionId);

        var recording = (await RecordingsOfAsync(sessionId)).Should().ContainSingle().Subject;

        recording.Pipeline.Should().Be(RecordingPipeline.TrackComposition);

        recording.CompositionStatus.Should().Be(
            RecordingCompositionStatus.Collecting,
            "qator YARATILGANDA yig'ish bosqichi ochiladi — bo'sh oraliq bo'lmasin");

        recording.EgressId.Should().BeNull("dars boshlash yo'lida LiveKit'ga BORILMAYDI");
        recording.ObjectKey.Should().NotBeNullOrWhiteSpace("yakuniy kalit hozircha band qilinadi");
        recording.RequestedBy.Should().BeNull("yozuvni TIZIM boshladi");

        // 🔴 Watchdog yangi qatorga TEGMAYDI (`Pipeline` filtri).
        await factory.RunRecordingWatchdogAsync();

        factory.Egress.Started.Should().NotContain(
            request => request.RoomName == roomName,
            "yangi quvurda xona kompoziti UMUMAN boshlanmasligi kerak");

        (await RecordingsOfAsync(sessionId)).Single().Status.Should().Be(
            RecordingStatus.Requested,
            "eski watchdog yangi qatorni na boshlaydi, na `Failed` qiladi");
    }

    // ================================================================= solishtiruv (A/B)

    /// <summary>
    /// ══════════════════════════════════════════════════════════════════
    /// 🔴 YOYISHNING 3-BOSQICHI: BITTA GURUH, IKKALA QUVUR BARAVAR
    /// ══════════════════════════════════════════════════════════════════
    ///
    /// Guruh ustuni <c>RoomComposite</c> da QOLADI — ro'yxat undan USTUN
    /// va bu ikki qator olishning YAGONA yo'li. Rejadagi haqiqiy qiymat:
    /// <c>recordings.track_pipeline_shadow_groups = "7"</c> (ATF-97).
    ///
    /// ★ RO'YXAT ATAYLAB IFLOS BERILADI (" 7 , yomon"): qiymat admin
    ///   panelidan qo'lda kiritiladi va u yerda ortiqcha probel yoki
    ///   tasodifiy so'z bo'lishi mutlaqo real. Yaroqsiz bo'lak dars
    ///   boshlashni yiqitmasligi kerak — u shunchaki tashlab yuboriladi.
    ///
    /// ★ IKKI KALIT HAR XIL: solishtiruvning butun ma'nosi ikki faylni
    ///   yonma-yon ochish. Bitta kalitga ikkalasi yozsa, ikkinchisi
    ///   birinchisini o'chirib yuborardi.
    /// </summary>
    [Fact]
    public async Task ShadowGroup_QueuesBothPipelines_WithDistinctObjectKeys()
    {
        factory.Egress.FailWith = null;
        factory.Egress.Started.Clear();

        var world = await WorldBuilder.CreateAsync(factory, "pipeshadow");

        // Guruh ustuni ATAYLAB tegilmaydi — ro'yxat o'zi yetarli bo'lishi kerak.
        await SetGroupAsync(world.GroupId, RecordingPipeline.RoomComposite);

        await SetPipelineSettingsAsync(
            enabled: true, shadowGroups: $" {world.GroupId} , yomon");

        var sessionId = await ScheduledSessionAsync(world.GroupId);
        var roomName = await RoomNameAsync(sessionId);

        await StartLessonAsync(world, sessionId);

        var rows = await RecordingsOfAsync(sessionId);

        rows.Should().HaveCount(2, "solishtiruv uchun ikkala fayl ham kerak");

        rows.Select(r => r.Pipeline).Should().BeEquivalentTo(
            [RecordingPipeline.RoomComposite, RecordingPipeline.TrackComposition]);

        rows.Select(r => r.ObjectKey).Distinct(StringComparer.Ordinal).Should().HaveCount(
            2, "kalitning 8 tasodifiy bayti ikki faylni bir-birining ustiga yozdirmaydi");

        var oldRow = rows.Single(r => r.Pipeline == RecordingPipeline.RoomComposite);
        var newRow = rows.Single(r => r.Pipeline == RecordingPipeline.TrackComposition);

        oldRow.CompositionStatus.Should().BeNull();
        newRow.CompositionStatus.Should().Be(RecordingCompositionStatus.Collecting);

        // Watchdog FAQAT eski qatorni ko'radi — ya'ni solishtiruv rejimida
        // ham Chrome'li egress AYNAN BITTA marta boshlanadi.
        await factory.RunRecordingWatchdogAsync();

        factory.Egress.Started.Count(request => request.RoomName == roomName).Should().Be(
            1, "solishtiruv ikkinchi xona kompozitini yaratmaydi");
    }

    /// <summary>
    /// Ro'yxatdagi BEGONA guruh Id'si shu guruhga ta'sir qilmaydi.
    ///
    /// ★ Mavjud bo'lmagan Id ham AYNI yo'l bilan e'tiborsiz qoladi: u
    ///   hech bir guruhga mos kelmaydi va guruh mavjudligini tekshirish
    ///   uchun dars boshlash yo'liga so'rov qo'shilmagan.
    /// </summary>
    [Fact]
    public async Task ShadowList_WithOtherGroupIds_DoesNotAffectThisGroup()
    {
        var world = await WorldBuilder.CreateAsync(factory, "pipealien");
        await SetGroupAsync(world.GroupId, RecordingPipeline.RoomComposite);

        await SetPipelineSettingsAsync(
            enabled: true, shadowGroups: $"{world.GroupId + 100000},{world.GroupId + 100001}");

        var sessionId = await ScheduledSessionAsync(world.GroupId);

        await StartLessonAsync(world, sessionId);

        var recording = (await RecordingsOfAsync(sessionId)).Should().ContainSingle().Subject;

        recording.Pipeline.Should().Be(RecordingPipeline.RoomComposite);
    }

    // ================================================================= idempotentlik

    /// <summary>
    /// 🔴 IDEMPOTENTLIK ENDI QUVUR BO'YICHA (§5.9-2).
    ///
    /// Solishtiruv rejimida ikkinchi "boshlash" so'rovi UCHINCHI va
    /// TO'RTINCHI qator yaratmasligi kerak. Eski shart ("dars uchun
    /// yakunlanmagan qator bormi") bu holatda ikkinchi qatorni UMUMAN
    /// yaratmasdi; yangi shart esa juda bo'shashib, har bosishda yangi
    /// juftlik yasashi mumkin edi. Bu test ikkala xatoni ham ushlaydi.
    /// </summary>
    [Fact]
    public async Task StartingLessonTwice_InShadowMode_KeepsExactlyTwoRows()
    {
        var world = await WorldBuilder.CreateAsync(factory, "pipetwice");
        await SetGroupAsync(world.GroupId, RecordingPipeline.RoomComposite);
        await SetPipelineSettingsAsync(enabled: true, shadowGroups: world.GroupId.ToString(Culture));

        var sessionId = await ScheduledSessionAsync(world.GroupId);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var first = await teacher.PostAsync($"/api/v1/live-sessions/{sessionId}/start", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await Body(first));

        var second = await teacher.PostAsync($"/api/v1/live-sessions/{sessionId}/start", null);
        second.StatusCode.Should().Be(HttpStatusCode.OK, await Body(second));

        (await RecordingsOfAsync(sessionId)).Should().HaveCount(
            2, "takroriy 'boshlash' har quvurda AYNI bitta qatorni qoldiradi");
    }

    /// <summary>
    /// ══════════════════════════════════════════════════════════════════
    /// 🔴 BIR VAQTDA KELGAN "BOSHLASH" SO'ROVLARI 500 BERMAYDI
    /// ══════════════════════════════════════════════════════════════════
    ///
    /// ★ NIMA O'ZGARDI: M2 <c>UX_SessionRecordings_SessionId_Pipeline_Active</c>
    ///   unikal indeksini qo'shdi. U ilgari JIMGINA ikkinchi qator yasagan
    ///   poygani endi <c>DbUpdateException</c> ga aylantiradi —
    ///   tekshiruv (<c>SELECT</c>) va yozuv (<c>INSERT</c>) alohida
    ///   tranzaksiyalarda, ya'ni ikki so'rov ikkalasi ham "qator yo'q" deb
    ///   xulosa qilishi mumkin.
    ///
    /// ★ TALAB: BAZA HOLATI TO'G'RI QOLSIN va foydalanuvchi 500 KO'RMASIN.
    ///   Dars baribir boshlanadi (yutgan so'rov) — ya'ni yutqazgan
    ///   so'rovning javobi ham ma'noli bo'lishi kerak, "Ichki server
    ///   xatosi" emas.
    ///
    /// ⚠️ TEST HAQIQIY POYGANI YARATADI, ya'ni u ba'zan poyga umuman
    ///    yuz bermagan holatni ham o'lchaydi (ikkala so'rov ketma-ket
    ///    bajarilsa). Bu ATAYLAB: sun'iy poyga (qo'lda `DbContext`
    ///    boshqarish) HTTP yo'lini va uning istisno xaritasini chetlab
    ///    o'tardi — ya'ni aynan tekshirilishi kerak bo'lgan narsani.
    /// </summary>
    [Fact]
    public async Task ConcurrentStarts_ResolveCleanly_WithoutServerError()
    {
        await ResetPipelineSettingsAsync();

        var world = await WorldBuilder.CreateAsync(factory, "piperace");
        await SetGroupAsync(world.GroupId, RecordingPipeline.RoomComposite);

        var sessionId = await ScheduledSessionAsync(world.GroupId);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var url = $"/api/v1/live-sessions/{sessionId}/start";

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => teacher.PostAsync(url, null)));

        try
        {
            foreach (var response in responses)
            {
                ((int)response.StatusCode).Should().BeLessThan(
                    500,
                    "unikal indeksga urilish foydalanuvchiga 'Ichki server xatosi' "
                    + "bo'lib ko'rinmasligi kerak");
            }

            responses.Should().Contain(
                response => response.StatusCode == HttpStatusCode.OK,
                "kamida bitta so'rov darsni HAQIQATAN boshlashi kerak");
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }

        (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Live);

        (await RecordingsOfAsync(sessionId)).Should().ContainSingle(
            "bir darsga bir quvurda AYNI bitta yozuv — bu endi BAZA qoidasi");
    }

    // ================================================================= yordamchilar

    private static readonly System.Globalization.CultureInfo Culture =
        System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>
    /// Guruhning yozuv kaliti va MEXANIZMI (o'quv bo'limi guruh
    /// tahririda tanlaydigan ikki maydon).
    /// </summary>
    private Task<int> SetGroupAsync(long groupId, RecordingPipeline pipeline) =>
        factory.WithDbAsync(async db =>
        {
            var group = await db.Groups.FirstAsync(g => g.Id == groupId);

            group.RecordEnabled = true;
            group.RecordingPipeline = pipeline;

            return await db.SaveChangesAsync();
        });

    /// <summary>Ikkala kalitni ham qo'yadi (yoki <c>null</c> bo'lsa o'chiradi).</summary>
    private async Task SetPipelineSettingsAsync(bool enabled, string? shadowGroups)
    {
        await SetSettingAsync(EnabledKey, enabled ? "true" : "false");
        await SetSettingAsync(ShadowKey, shadowGroups);
    }

    /// <summary>
    /// Sozlama qatorlarini butunlay olib tashlaydi — ya'ni "migratsiya
    /// endi chiqqan, hech kim hech narsa yoqmagan" holati.
    /// </summary>
    private async Task ResetPipelineSettingsAsync()
    {
        await SetSettingAsync(EnabledKey, null);
        await SetSettingAsync(ShadowKey, null);
    }

    /// <summary>
    /// <c>AppSettings</c> qatorini yozadi/o'chiradi.
    ///
    /// ★ TO'G'RIDAN-TO'G'RI BAZAGA, panel endpointi orqali EMAS: bu yerda
    ///   tekshirilayotgan narsa panel emas, TANLASH mantig'i. Panel yo'li
    ///   `SettingsRuntimeTests` da alohida qulflangan.
    /// </summary>
    private Task<int> SetSettingAsync(string key, string? value) =>
        factory.WithDbAsync(async db =>
        {
            var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);

            if (value is null)
            {
                if (row is not null)
                    db.AppSettings.Remove(row);
            }
            else if (row is null)
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = value,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                row.Value = value;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }

            return await db.SaveChangesAsync();
        });

    /// <summary>
    /// Hozir boshlanadigan dars (<c>LiveSession.Start</c> darsni
    /// boshlanishidan 5 daqiqadan oldin boshlashni rad etadi).
    /// </summary>
    private Task<long> ScheduledSessionAsync(long groupId) =>
        WorldBuilder.AddScheduledSessionAsync(factory, groupId, DateTimeOffset.UtcNow);

    /// <summary>
    /// Darsni boshlaydi VA ustozni xonaga kiritadi — watchdog BO'SH xonada
    /// yozuvni boshlamaydi (sabab: <c>RecordingWatchdogJob</c>).
    /// </summary>
    private async Task StartLessonAsync(StudentWorld world, long sessionId)
    {
        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.PostAsync($"/api/v1/live-sessions/{sessionId}/start", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await Body(response));

        await factory.EnterRoomAsync(sessionId, world.Teacher.Id);
    }

    private Task<List<SessionRecording>> RecordingsOfAsync(long sessionId) =>
        factory.WithDbAsync(db => db.SessionRecordings
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId)
            .OrderBy(r => r.Id)
            .ToListAsync());

    private Task<SessionStatus> StatusOfAsync(long sessionId) =>
        factory.WithDbAsync(db => db.LiveSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.Status)
            .FirstAsync());

    private Task<string> RoomNameAsync(long sessionId) =>
        factory.WithDbAsync(db => db.LiveSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.RoomName)
            .FirstAsync());

    private static Task<string> Body(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync();
}
