using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Api;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// AVTOMATIK DARS YOZUVI (2026-08-13) — DARS BOSHLANISHI BILAN
/// ════════════════════════════════════════════════════════════════════════
///
/// Qaror va uning bekor qilingan o'tmishdoshi <c>IRecordingService</c>
/// izohida. Bu yerda AYNAN uchta narsa qulflanadi:
///
///  1) GURUH KALITI HAQIQATAN ISHLAYDI. <c>Group.RecordEnabled</c> —
///     yagona kalit va u ilgari HECH KIM O'QIMAYDIGAN ustun edi. Test
///     ikkala yo'nalishni ham tekshiradi: yoqilganda navbat qatori
///     paydo bo'ladi, o'chirilganda — YO'Q.
///
///  2) 🔴 YOZUV DARSNI TO'XTATA OLMAYDI. Eng muhim guruh: Egress
///     sozlanmagan bo'lsa ham, xato qaytarsa ham dars ODATDAGIDEK
///     boshlanadi. Bu bekor qilinmagan eski qoida va u avtomatik
///     rejimning butun arxitekturasini belgilagan.
///
///  3) 🔴 ROZILIK INDIKATORI O'QUVCHIGA KO'RINADI. Avtomatik yozuv shu
///     shart bilan qabul qilingan: o'quvchi <c>recording-status</c> dan
///     `200` oladi va ketayotgan yozuvni KO'RADI. ⚠️ Eski
///     <c>GET .../recordings</c> ro'yxati bu vazifani bajara olmaydi —
///     u o'quvchiga faqat `Completed` qatorlarni beradi, ya'ni indikator
///     hech qachon yonmasdi. Shu farq ham test bilan qulflangan.
///
/// ── NIMA UCHUN DARS API ORQALI BOSHLANADI ──────────────────────────────
///
/// ★ <c>POST /live-sessions/{id}/start</c> — trigger AYNAN shu yo'lda
/// (<c>LiveSessionService.StartAsync</c>). Servisni to'g'ridan-to'g'ri
/// chaqirish DI ulanishini (yangi <c>IAutoRecordingScheduler</c>
/// bog'liqligi) va tranzaksiya chegarasini CHETLAB o'tardi — ya'ni
/// "konteynerda ro'yxatdan o'tmagan" xatosi testda ko'rinmasdi.
/// </summary>
public sealed class AutoRecordingTests(RecordingFactory factory)
    : IClassFixture<RecordingFactory>
{
    // ================================================================= guruh kaliti

    /// <summary>
    /// 🔴 ASOSIY TEST: yozuvi YOQILGAN guruhning darsi boshlanishi bilan
    /// navbat qatori paydo bo'ladi.
    ///
    /// ★ <c>RequestedBy</c> ning <c>null</c> ligi ALOHIDA tasdiqlanadi:
    /// aynan shu qiymat "tizim boshladi" degani va aynan shu tufayli
    /// migratsiya kerak bo'lmadi. Qo'lda boshlashda u xodimning Id'si
    /// bo'ladi (pastdagi test).
    /// </summary>
    [Fact]
    public async Task StartingLesson_WithRecordEnabledGroup_QueuesRecordingAutomatically()
    {
        var world = await WorldBuilder.CreateAsync(factory, "autorec");
        await SetRecordEnabledAsync(world.GroupId, enabled: true);

        var sessionId = await ScheduledSessionAsync(world.GroupId);

        await StartLessonAsync(world, sessionId);

        var rows = await RecordingsOfAsync(sessionId);

        rows.Should().ContainSingle("dars boshlanishi bilan AYNI bitta navbat qatori yoziladi");

        var recording = rows[0];

        recording.Status.Should().Be(
            RecordingStatus.Requested,
            "Egress dars boshlash yo'lida CHAQIRILMAYDI — u watchdog'ning ishi");

        recording.RequestedBy.Should().BeNull("yozuvni TIZIM boshladi, xodim emas");
        recording.ObjectKey.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Yozuvi O'CHIQ guruhda hech narsa yozilmaydi.
    ///
    /// ★ BU TESTSIZ YUQORIDAGISI HECH NIMANI ISBOTLAMASDI: "har doim
    /// yozib qo'yish" bilan ham u yashil bo'lardi va guruh kaliti
    /// amalda yana hech kim o'qimaydigan ustun bo'lib qolardi.
    /// </summary>
    [Fact]
    public async Task StartingLesson_WithRecordDisabledGroup_QueuesNothing()
    {
        var world = await WorldBuilder.CreateAsync(factory, "autooff");
        await SetRecordEnabledAsync(world.GroupId, enabled: false);

        var sessionId = await ScheduledSessionAsync(world.GroupId);

        await StartLessonAsync(world, sessionId);

        (await RecordingsOfAsync(sessionId)).Should().BeEmpty(
            "guruhda yozuv o'chiq — bitta ham qator bo'lmasligi kerak");
    }

    // ================================================================= idempotentlik

    /// <summary>
    /// 🔴 IKKI MARTA "BOSHLASH" IKKINCHI YOZUV YARATMAYDI.
    ///
    /// ⚠️ Bu HAQIQIY xavf: <c>LiveSession.Start()</c> darsni
    /// <c>Live</c> dan <c>Live</c> ga o'tkazishni RAD ETMAYDI (u faqat
    /// `Ended`/`Cancelled` ni to'sadi). Ya'ni tugma ikkinchi qurilmadan
    /// bosilsa yoki so'rov takrorlansa, himoyasiz kod ikkinchi navbat
    /// qatori yasab, watchdog IKKITA egress ochardi — bir darsning ikki
    /// nusxasi, ikki barobar tarmoq va ombor.
    /// </summary>
    [Fact]
    public async Task StartingLessonTwice_QueuesOnlyOneRecording()
    {
        var world = await WorldBuilder.CreateAsync(factory, "autotwice");
        await SetRecordEnabledAsync(world.GroupId, enabled: true);

        var sessionId = await ScheduledSessionAsync(world.GroupId);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var first = await teacher.PostAsync($"/api/v1/live-sessions/{sessionId}/start", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await Body(first));

        var second = await teacher.PostAsync($"/api/v1/live-sessions/{sessionId}/start", null);
        second.StatusCode.Should().Be(HttpStatusCode.OK, await Body(second));

        (await RecordingsOfAsync(sessionId)).Should().ContainSingle(
            "takroriy 'boshlash' ikkinchi egress ochmasligi kerak");
    }

    /// <summary>
    /// 🔴 QO'LDA BOSHLASH/TO'XTATISH YO'LLARI UMUMAN YO'Q (2026-09-01).
    ///
    /// Ilgari bu yerda "avtomatik navbat ustiga bosilgan qo'lda tugma AYNI
    /// qatorni qaytaradi" degan test turardi. Loyiha egasining qarori bilan
    /// qo'lda boshqaruv butunlay olib tashlandi: yozuv FAQAT guruh
    /// darajasida (<c>Group.RecordEnabled</c>) boshqariladi.
    ///
    /// ★ TEST SAQLANDI, MA'NOSI ALMASHDI: endi u yo'llarning QAYTIB
    /// KELMASLIGINI qo'riqlaydi. Kimdir endpointni "qulaylik uchun"
    /// tiklab qo'ysa, bu test yiqiladi va qaror qayta muhokama qilinadi —
    /// aks holda tiklash jimgina o'tib ketardi.
    /// </summary>
    [Theory]
    [InlineData("recordings/start")]
    [InlineData("recordings/stop")]
    public async Task ManualRecordingEndpoints_NoLongerExist(string path)
    {
        var world = await WorldBuilder.CreateAsync(factory, $"manual{path.GetHashCode(StringComparison.Ordinal):x}");
        await SetRecordEnabledAsync(world.GroupId, enabled: true);

        var sessionId = await ScheduledSessionAsync(world.GroupId);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var started = await teacher.PostAsync($"/api/v1/live-sessions/{sessionId}/start", null);
        started.StatusCode.Should().Be(HttpStatusCode.OK, await Body(started));

        var manual = await teacher.PostAsync($"/api/v1/live-sessions/{sessionId}/{path}", null);

        manual.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "qo'lda yozuv boshqaruvi ATAYLAB olib tashlangan");

        // Avtomatik navbat esa O'Z ISHINI QILGAN bo'lishi kerak — ya'ni
        // endpoint yo'qolgani yozuvni umuman o'chirib qo'ymagan.
        (await RecordingsOfAsync(sessionId)).Should().ContainSingle(
            "guruh kaliti yoqilgan, ya'ni dars boshlanishida qator qo'yiladi");
    }

    // ================================================================= watchdog merosi

    /// <summary>
    /// 🔴 NAVBAT QATORINI WATCHDOG BO'SHATADI — avtomatik yozuv AYNAN
    /// shu tufayli qayta urinish va muhlat mantiqini bepul oladi.
    ///
    /// ★ TEST WATCHDOG'NING "YANGI YOZUV BOSHLAMAYDI" QOIDASINI BUZMAYDI:
    /// vazifa guruhlarni skanerlamaydi, u ALLAQACHON MAVJUD
    /// <c>Requested</c> qatorini ko'radi. Qaror darsni boshlashda, ijro
    /// esa shu yerda — ikki alohida joy, bitta yo'l.
    /// </summary>
    [Fact]
    public async Task Watchdog_PicksUpTheAutomaticallyQueuedRecording_AndCallsEgress()
    {
        factory.Egress.FailWith = null;
        factory.Egress.Started.Clear();

        var world = await WorldBuilder.CreateAsync(factory, "autowd");
        await SetRecordEnabledAsync(world.GroupId, enabled: true);

        var sessionId = await ScheduledSessionAsync(world.GroupId);
        var roomName = await RoomNameAsync(sessionId);

        await StartLessonAsync(world, sessionId);

        await factory.RunRecordingWatchdogAsync();

        factory.Egress.Started.Should().Contain(
            request => request.RoomName == roomName,
            "watchdog navbatdagi qatorni AYNAN shu darsning xonasi uchun boshlashi kerak");

        var recording = (await RecordingsOfAsync(sessionId)).Single();

        recording.Status.Should().Be(RecordingStatus.Starting, "Egress so'rovni qabul qildi");
        recording.EgressId.Should().NotBeNullOrWhiteSpace();
        recording.Attempts.Should().Be(1);

        // Javobgarlik qatorda QOLADI: watchdog uni "o'ziniki" qilib
        // qo'ymaydi — yozuvni baribir guruh sozlamasi boshlagan.
        recording.RequestedBy.Should().BeNull();
    }

    /// <summary>
    /// ══════════════════════════════════════════════════════════════════
    /// 🔴 BO'SH XONADA YOZUV BOSHLANMAYDI
    /// ══════════════════════════════════════════════════════════════════
    ///
    /// ★ NIMA UCHUN BU QOIDA BOR (2026-08-24). Yozuv qatori dars `Live`
    ///   ga o'tgan ZAHOTI yaratiladi, ustozning brauzeri esa xonaga
    ///   KEYIN kiradi (kamera ruxsati birinchi safar o'nlab soniya
    ///   olishi mumkin). Egress bo'sh xonani kutmaydi: Chrome kiradi,
    ///   hech kim e'lon qilmasa ~18 soniyada uziladi, watchdog esa
    ///   faylni topa olmay yozuvni `Failed` deb belgilaydi.
    ///
    /// 🔴 `Failed` — YAKUNIY holat. Ya'ni sekin kirgan ustoz darsining
    ///    yozuvi BUTUNLAY yo'qolardi va qayta urinilmasdi. Alomati esa
    ///    yo'q: dars a'lo o'tadi, nosozlik faqat "yozuv qani?" savoli
    ///    bilan ochiladi.
    ///
    /// ★ MUHIM TAFSILOT: kutish URINISH SANALMAYDI (`Attempts` = 0).
    ///   Aks holda ustoz kirgunicha `MaxAttempts` sarflanib bo'lardi va
    ///   qoida o'zi qutqarmoqchi bo'lgan nosozlikni O'ZI yasagan bo'lardi.
    /// </summary>
    [Fact]
    public async Task Watchdog_WhenRoomIsEmpty_DoesNotStartRecording()
    {
        factory.Egress.FailWith = null;
        factory.Egress.Started.Clear();

        var world = await WorldBuilder.CreateAsync(factory, "autoempty");
        await SetRecordEnabledAsync(world.GroupId, enabled: true);

        var sessionId = await ScheduledSessionAsync(world.GroupId);
        var roomName = await RoomNameAsync(sessionId);

        // ⚠️ ATAYLAB `StartLessonAsync` EMAS: u ustozni xonaga ham
        //    kiritadi. Bu yerda esa aynan "boshladi, lekin hali
        //    kirmadi" holati kerak.
        using (var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher))
        {
            var response = await teacher.PostAsync(
                $"/api/v1/live-sessions/{sessionId}/start", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK, await Body(response));
        }

        // 🔴 Redis testlar orasida TOZALANMAYDI, baza esa har yurishda
        //    yangi — ya'ni `sessionId` qayta ishlatiladi va oldingi
        //    yurishdan qolgan presence "meros" bo'lib qolishi mumkin.
        //    Sabab batafsil: `RecordingWorld.ClearRoomAsync`.
        await factory.ClearRoomAsync(sessionId);

        await factory.RunRecordingWatchdogAsync();

        factory.Egress.Started.Should().NotContain(
            request => request.RoomName == roomName,
            "xona bo'sh ekan — Egress'ni bezovta qilish faqat yozuvni yo'qotardi");

        var recording = (await RecordingsOfAsync(sessionId)).Single();

        recording.Status.Should().Be(
            RecordingStatus.Requested, "qator navbatda QOLADI, yo'qolmaydi");

        recording.Attempts.Should().Be(
            0, "kutish — urinish EMAS, aks holda `MaxAttempts` bekorga sarflanardi");

        // ── Ustoz kirdi: keyingi yurishda yozuv boshlanishi kerak ──
        await factory.EnterRoomAsync(sessionId, world.Teacher.Id);
        await factory.RunRecordingWatchdogAsync();

        factory.Egress.Started.Should().Contain(
            request => request.RoomName == roomName,
            "xona to'lgach yozuv O'ZIDAN boshlanishi kerak — qo'lda aralashuv shart emas");

        (await RecordingsOfAsync(sessionId)).Single().Status
            .Should().Be(RecordingStatus.Starting);
    }

    // ================================================================= dars to'xtamaydi

    /// <summary>
    /// 🔴 ENG MUHIM XAVFSIZLIK TESTI: YOZUV XIZMATI SOZLANMAGAN BO'LSA
    /// HAM DARS ODATDAGIDEK BOSHLANADI.
    ///
    /// Bu bekor QILINMAGAN eski qoida ("yozuv nosozligi darsni
    /// to'xtatmasligi shart") va u avtomatik rejimda YANADA muhim: endi
    /// yozuv yo'li dars boshlash yo'liga ulangan.
    ///
    /// ★ QATOR HAM YOZILMAYDI. Aks holda u navbatda abadiy yotardi
    /// (watchdog sozlanmagan holatda hech nima qilmaydi), dars tugagach
    /// esa "Dars yakunlandi, yozuv esa boshlanmadi" degan YOLG'ON xato
    /// qatoriga aylanardi — har dars uchun bittadan.
    /// </summary>
    [Fact]
    public async Task StartingLesson_WhenEgressIsNotConfigured_StillStartsAndQueuesNothing()
    {
        var world = await WorldBuilder.CreateAsync(factory, "autonocfg");
        await SetRecordEnabledAsync(world.GroupId, enabled: true);

        var sessionId = await ScheduledSessionAsync(world.GroupId);

        factory.Egress.IsConfigured = false;

        try
        {
            using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

            var response = await teacher.PostAsync(
                $"/api/v1/live-sessions/{sessionId}/start", null);

            response.StatusCode.Should().Be(
                HttpStatusCode.OK,
                "yozuv xizmatining sozlanmagani darsni BOSHLASHGA to'sqinlik qila olmaydi");

            (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Live);

            (await RecordingsOfAsync(sessionId)).Should().BeEmpty(
                "sozlanmagan xizmatda va'da berilmaydi — bo'sh navbat qatori yozilmaydi");
        }
        finally
        {
            factory.Egress.IsConfigured = true;
        }
    }

    /// <summary>
    /// Egress XATO qaytarsa ham dars boshlanadi va qator <c>Requested</c>
    /// holida qoladi (watchdog qayta uradi).
    ///
    /// ★ Bu yerda xato watchdog yurishida sodir bo'ladi, dars boshlash
    /// so'rovida EMAS — aynan shuning uchun dars so'rovi umuman ta'sir
    /// ko'rmaydi.
    /// </summary>
    [Fact]
    public async Task StartingLesson_WhenEgressRejects_LeavesRecordingRetryable()
    {
        factory.Egress.FailWith = "Egress band.";

        try
        {
            var world = await WorldBuilder.CreateAsync(factory, "autofail");
            await SetRecordEnabledAsync(world.GroupId, enabled: true);

            var sessionId = await ScheduledSessionAsync(world.GroupId);

            await StartLessonAsync(world, sessionId);
            await factory.RunRecordingWatchdogAsync();

            var recording = (await RecordingsOfAsync(sessionId)).Single();

            recording.Status.Should().Be(
                RecordingStatus.Requested,
                "urinishning yiqilishi YAKUNIY xato emas — watchdog qayta uradi");

            recording.Error.Should().Contain("Egress band.");
            recording.Attempts.Should().Be(1);
        }
        finally
        {
            factory.Egress.FailWith = null;
        }
    }

    // ================================================================= 🔴 rozilik indikatori

    /// <summary>
    /// ══════════════════════════════════════════════════════════════════
    /// 🔴 O'QUVCHI YOZIB OLINAYOTGANINI KO'RADI — AVTOMATIK REJIMNING
    ///    SHARTI. Bu test buzilsa avtomatik yozuv ham JORIY ETILMASLIGI
    ///    kerak: eski tizimning eng jiddiy kamchiligi aynan shu edi —
    ///    xonadagi hech kim yozib olinayotganini bilmasdi, ishtirokchilar
    ///    esa "ko'pincha bolalar".
    /// ══════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public async Task RecordingStatus_IsVisibleToStudent_WhileRecordingIsQueued()
    {
        var world = await WorldBuilder.CreateAsync(factory, "autoind");
        await SetRecordEnabledAsync(world.GroupId, enabled: true);

        var sessionId = await ScheduledSessionAsync(world.GroupId);

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        // ── dars boshlanmagan: indikator O'CHIQ ────────────────────────
        var before = await LiveStatusAsync(student, sessionId);

        before.IsRecording.Should().BeFalse();
        before.StartedAt.Should().BeNull();

        await StartLessonAsync(world, sessionId);

        // ── dars boshlandi: indikator YONADI ──────────────────────────
        //
        // ⚠️ Egress hali CHAQIRILMAGAN (watchdog yurmadi), ya'ni qator
        //    `Requested` holatida. Indikator SHUNDA HAM yonadi — bu
        //    ONGLI asimmetriya: "yozilmayapti" deb yolg'on aytish
        //    roziligni buzardi, ortiqcha ogohlantirish esa zararsiz.
        var during = await LiveStatusAsync(student, sessionId);

        during.IsRecording.Should().BeTrue(
            "o'quvchi yozib olinayotganini KO'RISHI shart — bu qarorning sharti");
    }

    /// <summary>
    /// 🔴 NIMA UCHUN ESKI RO'YXAT ENDPOINTI INDIKATOR UCHUN YARAMAYDI.
    ///
    /// AYNI dars, AYNI o'quvchi, AYNI lahza: <c>recording-status</c>
    /// "yozilmoqda" deydi, <c>GET .../recordings</c> esa BO'SH ro'yxat
    /// qaytaradi (o'quvchiga faqat `Completed` qatorlar ko'rsatiladi).
    /// Indikatorni o'sha ro'yxatga ulash JIM YOLG'ON bo'lardi — shuning
    /// uchun alohida endpoint qo'shilgan va bu farq shu yerda qulflanadi.
    /// </summary>
    [Fact]
    public async Task RecordingList_HidesInProgressRecordingFromStudent_UnlikeStatusEndpoint()
    {
        var world = await WorldBuilder.CreateAsync(factory, "autolist");
        await SetRecordEnabledAsync(world.GroupId, enabled: true);

        var sessionId = await ScheduledSessionAsync(world.GroupId);

        await StartLessonAsync(world, sessionId);

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        (await LiveStatusAsync(student, sessionId)).IsRecording.Should().BeTrue();

        var list = await student.GetAsync($"/api/v1/live-sessions/{sessionId}/recordings");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await Body(list));

        var rows = await list.Content.ReadFromJsonAsync<List<RecordingResponse>>();

        rows.Should().BeEmpty(
            "ro'yxat o'quvchiga faqat TAYYOR yozuvlarni beradi — shuning uchun u "
            + "indikator uchun yaramaydi");
    }

    /// <summary>
    /// Ruxsat ROLGA emas, DARSGA bog'liq: guruhda bo'lmagan o'quvchi
    /// baribir rad etiladi.
    ///
    /// ★ Endpoint'da <c>[Authorize(Roles = …)]</c> ATAYLAB yo'q (o'quvchi
    /// ko'rishi kerak), ya'ni yagona himoya — servisdagi darsga kirish
    /// tekshiruvi. Bu test aynan o'sha himoya joyidaligini isbotlaydi.
    /// </summary>
    [Fact]
    public async Task RecordingStatus_IsRejectedForNonMember()
    {
        var world = await WorldBuilder.CreateAsync(factory, "autoperm");
        var sessionId = await ScheduledSessionAsync(world.GroupId);

        // ★ GURUHGA QO'SHILMAYDI (`AddStudentAsync` a'zo qilib qo'yardi):
        //   tekshiriladigan narsa aynan A'ZO BO'LMAGAN odamning rad
        //   etilishi.
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var stranger = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, "autoalien");

        using var client = await WorldBuilder.ClientAsync(factory, stranger);

        var response = await client.GetAsync(
            $"/api/v1/live-sessions/{sessionId}/recording-status");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await Body(response));
    }

    // ================================================================= yordamchilar

    /// <summary>
    /// Guruhning yozuv kalitini qo'yadi (o'quv bo'limi guruh formasida
    /// bosadigan checkbox — `GroupEditDrawer.vue`).
    /// </summary>
    /// <returns>Yozilgan qatorlar soni (CA1859 — turni yashirish bekorga qoplama).</returns>
    private Task<int> SetRecordEnabledAsync(long groupId, bool enabled) =>
        factory.WithDbAsync(async db =>
        {
            var group = await db.Groups.FirstAsync(g => g.Id == groupId);
            group.RecordEnabled = enabled;

            return await db.SaveChangesAsync();
        });

    /// <summary>
    /// Hozir boshlanadigan dars.
    ///
    /// ★ Vaqt ATAYLAB "hozir": <c>LiveSession.Start</c> darsni
    /// boshlanishidan 5 daqiqadan oldin boshlashni rad etadi
    /// (<c>StartLeadMinutes</c>).
    /// </summary>
    private Task<long> ScheduledSessionAsync(long groupId) =>
        WorldBuilder.AddScheduledSessionAsync(factory, groupId, DateTimeOffset.UtcNow);

    /// <summary>
    /// Darsni boshlaydi VA ustozni xonaga kiritadi.
    ///
    /// ★ IKKINCHI QADAM 2026-08-24 DA QO'SHILDI va u HAQIQIY oqimni
    ///   aks ettiradi: ustoz "Darsni boshlash" ni bosgach brauzer uni
    ///   darhol xonaga olib kiradi. Watchdog esa BO'SH xonada yozuvni
    ///   boshlamaydi (sabab: `RecordingWatchdogJob`), ya'ni presence
    ///   yozilmasa bu yerdagi testlar HAQIQATDAN uzoq holatni
    ///   tekshirgan bo'lardi.
    ///
    /// ⚠️ Bo'sh xona qoidasining O'ZI alohida testda tekshiriladi
    ///   (`Watchdog_WhenRoomIsEmpty_DoesNotStartRecording`) — shuning
    ///   uchun bu yordamchini "har ehtimolga qarshi" ishlatish
    ///   qoidani ko'r qilib qo'ymaydi.
    /// </summary>
    private async Task StartLessonAsync(StudentWorld world, long sessionId)
    {
        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.PostAsync($"/api/v1/live-sessions/{sessionId}/start", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await Body(response));

        await factory.EnterRoomAsync(sessionId, world.Teacher.Id);
    }

    private static async Task<LiveStatusResponse> LiveStatusAsync(HttpClient client, long sessionId)
    {
        var response = await client.GetAsync(
            $"/api/v1/live-sessions/{sessionId}/recording-status");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await Body(response));

        return (await response.Content.ReadFromJsonAsync<LiveStatusResponse>())!;
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

    /// <summary>
    /// ★ DTO'lar TESTDA QAYTA E'LON QILINADI (loyihaning umumiy uslubi):
    /// server shaklini o'zgartirsa test KOMPILYATSIYADA emas, TASDIQDA
    /// yiqilishi kerak — ya'ni shartnoma buzilgani ko'rinsin.
    /// </summary>
    private sealed record RecordingResponse(long Id, string Status);

    private sealed record LiveStatusResponse(bool IsRecording, DateTimeOffset? StartedAt);
}
