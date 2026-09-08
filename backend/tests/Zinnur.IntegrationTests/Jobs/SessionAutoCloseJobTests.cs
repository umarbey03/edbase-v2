using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.IntegrationTests.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// MUDDATI O'TGAN DARSLARNI AVTO-YAKUNLASH (FAZA 5.5)
/// ════════════════════════════════════════════════════════════════════════
///
/// Tekshiriladigan ikki xavf:
///
///  • YOPILMASLIK: ustoz "Yakunlash" ni bosmasa dars abadiy <c>Live</c>
///    qolardi -> davomat yakunlanmaydi, hisobotlar esa faqat <c>Ended</c>
///    darslarni sanaydi, ya'ni o'tkazilgan dars statistikaga tushmaydi;
///
///  • 🔴 ERTA YOPILISH: hali davom etayotgan darsni yopish o'quvchilar
///    ekranidan videoni o'chiradi. Shuning uchun mo'hlat (grace) va uning
///    HURMAT QILINISHI alohida test bilan qulflanadi.
///
///  • KECH YOPILISH (2026-09-08): ustozlar "Yakunlash"ni bosmaydi va dars
///    bir soatcha "Hozir efirda" bo'lib turardi. Endi XONA BO'SH bo'lsa
///    dars qisqa mo'hlatdan keyin yopiladi — pastdagi "BO'SH XONA"
///    bo'limi ikkala yo'nalishni ham qulflaydi: bo'sh xona TEZ yopiladi,
///    odam turgan xona esa to'liq mo'hlatni kutadi.
/// </summary>
public sealed class SessionAutoCloseJobTests(JobFactory factory) : IClassFixture<JobFactory>
{
    /// <summary>Standart dars uzunligi (Domain va seed bilan bir xil).</summary>
    private static readonly TimeSpan Lesson = TimeSpan.FromMinutes(80);

    // ================================================================= 1) JONLI DARS

    /// <summary>
    /// Muddati ANIQ o'tgan jonli dars yakunlanadi va xona xabardor qilinadi.
    ///
    /// ★ XABAR MAJBURIY: fon vazifasi bazaga o'zi yozib qo'ysa, o'quvchilar
    /// ekranida dars tugagani KO'RINMASDI (ogohlantirish
    /// <c>ILiveSessionNotifier</c> izohida ochiq yozilgan). Shuning uchun
    /// vazifa use-case'ni chaqiradi va bu test aynan shuni qulflaydi.
    /// </summary>
    [Fact]
    public async Task OverdueLiveSession_IsEndedAndRoomIsNotified()
    {
        // Boshlanganiga 5 soat: EndsAt ~3.5 soat oldin o'tgan, ya'ni
        // standart 60 daqiqalik mo'hlatdan ancha uzoq.
        var sessionId = await CreateSessionAsync(
            SessionStatus.Live, ago: TimeSpan.FromHours(5));

        var result = await factory.RunSessionJobAsync();

        result.Processed.Should().BeGreaterThan(0);
        (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Ended);

        (await ActualEndOfAsync(sessionId)).Should().NotBeNull(
            "yakunlangan darsda haqiqiy tugash vaqti bo'lishi kerak");

        factory.Notifier.Ended.Should().Contain(sessionId,
            "avto-yakunlash ham xonaga xabar yuborishi kerak");
    }

    /// <summary>
    /// 🔴 ENG MUHIM XAVFSIZLIK TESTI: XONADA ODAM BO'LSA, muddati o'tgan
    /// bo'lsa ham MO'HLAT tugamaguncha darsga TEGILMAYDI.
    ///
    /// Dars 100 daqiqa oldin boshlangan (rejada 80), ya'ni <c>EndsAt</c>
    /// 20 daqiqa oldin o'tgan. Standart mo'hlat 60 daqiqa — demak dars hali
    /// YOPILMASLIGI kerak. Bu qoida buzilsa, hali o'qitayotgan ustozning
    /// darsi uzilib qolardi.
    ///
    /// ⚠️ XONAGA ATAYLAB ODAM QO'YILADI (2026-09-08): vaqt bo'yicha bu
    /// dars endi qisqa mo'hlatning ham nomzodi, ya'ni uni faqat XONADAGI
    /// ODAM qutqarib qoladi. Presence qo'yilmasa test tekshirmoqchi
    /// bo'lgan himoyaga umuman yetib bormasdi.
    /// </summary>
    [Fact]
    public async Task LiveSession_PastEndButRoomIsBusy_IsLeftAloneUntilFullGrace()
    {
        var sessionId = await CreateSessionAsync(
            SessionStatus.Live, ago: TimeSpan.FromMinutes(100));

        await EnterRoomAsync(sessionId);

        await factory.RunSessionJobAsync();

        (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Live,
            "xonada odam bor ekan, to'liq mo'hlat tugamaguncha darsga tegilmaydi");

        factory.Notifier.Ended.Should().NotContain(sessionId);
    }

    // ================================================================= 1b) BO'SH XONA

    /// <summary>
    /// ★ AYNAN SHIKOYAT QILINGAN HOLAT (2026-09-08): ustoz darsni o'tib
    /// bo'ldi va oynani yopdi — "Yakunlash"ni bosmadi. Xona BO'SH, dars
    /// esa rejadagi tugash paytidan o'tgan. Ilgari bunday dars TO'LIQ
    /// mo'hlat (60 daqiqa) davomida "Hozir efirda" bo'lib turardi.
    ///
    /// Dars 100 daqiqa oldin boshlangan (rejada 80) -> <c>EndsAt</c> 20
    /// daqiqa oldin o'tgan: bo'sh xona mo'hlatidan (5 daqiqa) ko'p, to'liq
    /// mo'hlatdan (60 daqiqa) esa ANCHA kam. Ya'ni test faqat YANGI qoida
    /// tufayli o'tadi.
    /// </summary>
    [Fact]
    public async Task LiveSession_PastEndAndRoomIsEmpty_IsClosedWithoutWaitingFullGrace()
    {
        var sessionId = await CreateSessionAsync(
            SessionStatus.Live, ago: TimeSpan.FromMinutes(100));

        await LeaveRoomEmptyAsync(sessionId);

        await factory.RunSessionJobAsync();

        (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Ended,
            "xonada hech kim yo'q ekan, uzib qo'yiladigan odamning o'zi yo'q");

        factory.Notifier.Ended.Should().Contain(sessionId,
            "bosh sahifadagi 'Hozir efirda' ham darhol yangilanishi kerak");
    }

    /// <summary>
    /// BO'SH XONA HAM DARHOL YOPILMAYDI: <c>EndsAt</c> dan keyin qisqa
    /// mo'hlat baribir kutiladi.
    ///
    /// Dars 81 daqiqa oldin boshlangan (rejada 80), ya'ni <c>EndsAt</c>
    /// bor-yo'g'i 1 daqiqa oldin o'tgan — bo'sh xona mo'hlati (5 daqiqa)
    /// hali tugamagan. Sabab: ustoz o'sha daqiqada xonaga qaytib
    /// kirayotgan bo'lishi mumkin va presence hali yangilanmagan.
    /// </summary>
    [Fact]
    public async Task LiveSession_JustPastEndAndEmpty_IsLeftAlone_WithinEmptyRoomGrace()
    {
        var sessionId = await CreateSessionAsync(
            SessionStatus.Live, ago: TimeSpan.FromMinutes(81));

        await LeaveRoomEmptyAsync(sessionId);

        await factory.RunSessionJobAsync();

        (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Live,
            "bo'sh xona mo'hlati tugamaguncha dars jonli qoladi");
    }

    /// <summary>
    /// 🔴 TO'LIQ MO'HLAT SHARTSIZ: xonada odam bo'lsa ham, <c>Grace</c>
    /// o'tgach dars YOPILADI. Bu — eski xatti-harakat va u SAQLANADI:
    /// aks holda xonada unutilgan bitta ulanish darsni abadiy "jonli"
    /// holda ushlab turardi (davomat yakunlanmaydi, hisobotga tushmaydi).
    /// </summary>
    [Fact]
    public async Task LiveSession_PastFullGrace_IsClosedEvenWhenRoomIsBusy()
    {
        var sessionId = await CreateSessionAsync(
            SessionStatus.Live, ago: TimeSpan.FromHours(5));

        await EnterRoomAsync(sessionId);

        await factory.RunSessionJobAsync();

        (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Ended,
            "to'liq mo'hlatdan keyin xonadagi odam ham darsni ushlab tura olmaydi");
    }

    /// <summary>Rejada davom etayotgan dars (hali tugamagan) ham tegilmaydi.</summary>
    [Fact]
    public async Task LiveSession_StillRunning_IsLeftAlone()
    {
        var sessionId = await CreateSessionAsync(
            SessionStatus.Live, ago: TimeSpan.FromMinutes(10));

        await factory.RunSessionJobAsync();

        (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Live);
    }

    // ================================================================= 2) BEKOR QILINGAN

    /// <summary>
    /// 🔴 BEKOR QILINGAN DARS HECH QACHON YAKUNLANMAYDI.
    ///
    /// Sabab (topilgan va Domain testi bilan ham qulflangan bug):
    /// <c>Cancelled -> Ended</c> o'tishi bekor qilish faktini O'CHIRIB
    /// tashlardi va umuman bo'lmagan dars uchun davomat yozardi.
    ///
    /// Dars ATAYLAB "boshlangan" holda yaratiladi: vaqti bo'yicha u aniq
    /// nomzod, ya'ni uni faqat HOLAT qutqarib qoladi. Aks holda test
    /// tekshirmoqchi bo'lgan himoyaga umuman yetib bormasdi.
    /// </summary>
    [Fact]
    public async Task CancelledSession_IsNeverEnded()
    {
        var sessionId = await CreateSessionAsync(
            SessionStatus.Cancelled, ago: TimeSpan.FromHours(5), started: true);

        await factory.RunSessionJobAsync();

        (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Cancelled);
        factory.Notifier.Ended.Should().NotContain(sessionId);
    }

    // ================================================================= 3) BOSHLANMAGAN

    /// <summary>
    /// 🔴 UMUMAN BOSHLANMAGAN DARSGA TEGILMAYDI — qancha eski bo'lsa ham.
    ///
    /// Bu ATAYLAB qilingan tanlov, e'tibordan chetda qolgan holat emas.
    /// Hisobotlar <c>Ended</c> darsni "O'TKAZILGAN dars" deb sanaydi
    /// (<c>AttendanceSummaryService</c>: har <c>Ended</c> dars maxrajga
    /// qo'shiladi, davomat yozuvi yo'q bo'lsa "kelmagan" deb hisoblanadi).
    /// Ya'ni o'tkazilmagan darsni yopish HAR o'quvchining davomat foizini
    /// jimgina pasaytirardi — bo'lmagan darsga "kelmadi" deb yozilardi.
    ///
    /// Bunday darslar uchun alohida holat ("o'tkazilmadi") kerak; u Domain
    /// o'zgarishi va biznes qarori. Shu test qoidani qulflaydi: kimdir
    /// "muddati o'tgan hamma darsni yopaylik" deb kengaytirsa, test yiqiladi.
    /// </summary>
    [Fact]
    public async Task NeverStartedSession_IsLeftAlone_HoweverOldItIs()
    {
        var sessionId = await CreateSessionAsync(
            SessionStatus.Scheduled, ago: TimeSpan.FromDays(30), started: false);

        await factory.RunSessionJobAsync();

        (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Scheduled,
            "o'tkazilmagan darsni 'Ended' qilish hisobotlarni buzardi");
    }

    // ================================================================= 4) DAVOMAT

    /// <summary>
    /// Davomat YAKUNLANADI: o'lchangan vaqt bo'yicha baho qo'yiladi.
    /// Aynan shu bosqich yopilmagan darsda BAJARILMASDAN qolardi.
    /// </summary>
    [Fact]
    public async Task Attendance_IsFinalized_WhenSessionIsAutoClosed()
    {
        var sessionId = await CreateSessionAsync(
            SessionStatus.Live, ago: TimeSpan.FromHours(5));

        var attendanceId = await AddAttendanceAsync(
            sessionId, durationSeconds: 20 * 60, isManual: false);

        await factory.RunSessionJobAsync();

        var attendance = await LoadAttendanceAsync(attendanceId);

        attendance.Status.Should().Be(AttendanceStatus.Present,
            "20 daqiqa (15 daqiqalik chegaradan ko'p) — to'liq qatnashgan");
    }

    /// <summary>
    /// 🔴 QO'LDA QO'YILGAN BAHO QAYTA HISOBLANMAYDI.
    ///
    /// Ustoz o'quvchini qo'lda "Absent" deb belgilagan bo'lsa (masalan
    /// "xonada bor edi, lekin qatnashmadi"), avto-yakunlash uni o'lchangan
    /// vaqtga qarab "Present" ga O'ZGARTIRMASLIGI kerak. Qoida Domain'da
    /// (<c>Attendance.Finalize</c> ichida <c>if (IsManual) return;</c>) —
    /// bu test uni fon vazifasi yo'lida ham qulflaydi.
    /// </summary>
    [Fact]
    public async Task ManualAttendance_IsNotRecalculated_ByAutoClose()
    {
        var sessionId = await CreateSessionAsync(
            SessionStatus.Live, ago: TimeSpan.FromHours(5));

        // O'lchov bo'yicha "Present" chiqishi kerak edi (1 soat), lekin
        // ustozning QARORI — "Absent".
        var attendanceId = await AddAttendanceAsync(
            sessionId, durationSeconds: 60 * 60, isManual: true,
            status: AttendanceStatus.Absent);

        await factory.RunSessionJobAsync();

        var attendance = await LoadAttendanceAsync(attendanceId);

        attendance.Status.Should().Be(AttendanceStatus.Absent,
            "qo'lda qo'yilgan baho avto-yakunlashdan keyin ham saqlanishi kerak");
        attendance.IsManual.Should().BeTrue();
    }

    // ================================================================= 5) IDEMPOTENTLIK

    /// <summary>
    /// Ikkinchi yurish yakunlangan darsga QAYTA TEGMAYDI: tugash vaqti
    /// o'zgarmaydi va xona ikkinchi marta xabar olmaydi.
    /// </summary>
    [Fact]
    public async Task SecondRun_DoesNotTouchAlreadyEndedSessions()
    {
        var sessionId = await CreateSessionAsync(
            SessionStatus.Live, ago: TimeSpan.FromHours(5));

        await factory.RunSessionJobAsync();
        var endedAt = await ActualEndOfAsync(sessionId);

        await factory.RunSessionJobAsync();

        (await ActualEndOfAsync(sessionId)).Should().Be(endedAt,
            "yakunlangan darsga qayta tegilmasligi kerak (idempotent)");

        factory.Notifier.Ended.Count(id => id == sessionId).Should().Be(1,
            "xona faqat BIR MARTA xabar olishi kerak");
    }

    /// <summary>
    /// ★ IKKI "INSTANCE" birdaniga yurgizilsa ham dars BIR MARTA yakunlanadi
    /// va xona BIR MARTA xabar oladi.
    ///
    /// Bu — qulfning HAQIQIY vazifadagi dalili: <c>JobLockTests</c> qulf
    /// mexanizmini soxta vazifa bilan tekshiradi, bu esa uning oxirgi
    /// natijaga ta'sirini.
    /// </summary>
    [Fact]
    public async Task TwoInstances_AutoClosingAtOnce_NotifyTheRoomOnlyOnce()
    {
        var sessionId = await CreateSessionAsync(
            SessionStatus.Live, ago: TimeSpan.FromHours(5));

        await Task.WhenAll(
            factory.RunSessionJobAsync(),
            factory.RunSessionJobAsync());

        (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Ended);

        factory.Notifier.Ended.Count(id => id == sessionId).Should().Be(1,
            "leader lock ikkinchi instance'ni to'sishi kerak");
    }

    // ------------------------------------------------------------------ yordamchi

    /// <summary>
    /// Test darsi: <paramref name="ago"/> vaqt oldin boshlangan (rejada ham,
    /// <paramref name="started"/> bo'lsa haqiqatda ham).
    /// </summary>
    /// <param name="status">Boshlang'ich holat.</param>
    /// <param name="ago">Dars rejadagi boshlanish payti qancha oldin bo'lgan.</param>
    /// <param name="started">
    /// <c>true</c> — <c>ActualStart</c> yoziladi, ya'ni dars HAQIQATAN
    /// boshlangan. Avto-yakunlash faqat shunday darslarga tegadi.
    /// </param>
    private async Task<long> CreateSessionAsync(
        SessionStatus status,
        TimeSpan ago,
        bool started = true)
    {
        var groupId = await factory.SeededGroupIdAsync();
        var start = DateTimeOffset.UtcNow - ago;

        return await factory.WithDbAsync(async db =>
        {
            var session = new LiveSession
            {
                GroupId = groupId,
                Title = "Avto-yakunlash testi",
                Type = SessionType.Teacher,
                Status = status,
                ScheduledStart = start,
                ScheduledEnd = start + Lesson,
                ActualStart = started ? start : null,
                RoomName = LiveSession.GenerateRoomName(),
            };

            db.LiveSessions.Add(session);
            await db.SaveChangesAsync();

            return session.Id;
        });
    }

    private async Task<long> AddAttendanceAsync(
        long sessionId,
        int durationSeconds,
        bool isManual,
        AttendanceStatus status = AttendanceStatus.Absent)
    {
        var studentId = await factory.SeededStudentIdAsync();

        return await factory.WithDbAsync(async db =>
        {
            var attendance = new Attendance
            {
                SessionId = sessionId,
                StudentId = studentId,
                Status = status,
                DurationSeconds = durationSeconds,
                IsManual = isManual,
            };

            db.Attendances.Add(attendance);
            await db.SaveChangesAsync();

            return attendance.Id;
        });
    }

    /// <summary>Presence — Redis'dagi "xonada kim bor" ro'yxati (singleton).</summary>
    private IPresenceService Presence => factory.Services.GetRequiredService<IPresenceService>();

    /// <summary>
    /// Xonaga bitta ishtirokchi qo'yadi — ya'ni dars HAQIQATAN davom
    /// etyapti va uni yopish odamlarni uzib qo'yardi.
    /// </summary>
    private async Task EnterRoomAsync(long sessionId)
    {
        var studentId = await factory.SeededStudentIdAsync();

        await Presence.AddAsync(sessionId, new PresenceEntry(
            studentId, "Test o'quvchi", "Student", HandRaised: false, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Xonani ATAYLAB bo'shatadi.
    ///
    /// ★ NIMA UCHUN BU KERAK, GARCHI DARS ENDIGINA YARATILGAN BO'LSA-DA:
    /// presence kaliti (`presence:{sessionId}`) Redis'da test bazasining
    /// prefiksisiz yashaydi, Redis esa dev stack'i va boshqa test
    /// sinflari bilan BAHAM KO'RILADI. Boshqa sinfdagi AYNI raqamli
    /// darsdan qolgan yozuv bu testni tasodifiy (flaky) qilardi — va
    /// aynan "bo'sh xona" testlari jimgina o'tib ketardi.
    /// </summary>
    private Task LeaveRoomEmptyAsync(long sessionId) => Presence.ClearAsync(sessionId);

    private Task<Attendance> LoadAttendanceAsync(long id) =>
        factory.WithDbAsync(db => db.Attendances.AsNoTracking().FirstAsync(a => a.Id == id));

    private Task<SessionStatus> StatusOfAsync(long sessionId) =>
        factory.WithDbAsync(db => db.LiveSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.Status)
            .FirstAsync());

    private Task<DateTimeOffset?> ActualEndOfAsync(long sessionId) =>
        factory.WithDbAsync(db => db.LiveSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.ActualEnd)
            .FirstAsync());
}


/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// BO'SH XONA IKKI MARTA O'LCHANADI (2026-09-08)
/// ════════════════════════════════════════════════════════════════════════
///
/// 🔴 HIMOYA QILINAYOTGAN XAVF: ustoz sahifani YANGILAGAN lahzada (F5,
/// tarmoq sakrashi, telefonda ilova fonga o'tishi) presence bir necha
/// soniyaga NOLGA tushadi. Bitta o'lchov bilan aynan o'sha soniyaga
/// tushib qolgan dars uzilib qolardi — va uni QAYTA OCHIB BO'LMAYDI
/// (<c>LiveSession.Start</c> yakunlangan darsni rad etadi), ya'ni ustoz
/// dars o'rtasida quvvatsiz qolardi.
///
/// ★ NIMA UCHUN ALOHIDA SINF: bu yerda presence HAQIQIY Redis emas,
/// scenariy (<see cref="ScriptedPresenceService"/>) — aks holda "birinchi
/// o'lchov bo'sh, ikkinchisi to'la" holatini vaqtga bog'liq (flaky)
/// hiylalarsiz yasab bo'lmasdi.
/// </summary>
public sealed class SessionAutoCloseEmptyRoomConfirmTests(ScriptedPresenceJobFactory factory)
    : IClassFixture<ScriptedPresenceJobFactory>
{
    private static readonly TimeSpan Lesson = TimeSpan.FromMinutes(80);

    /// <summary>
    /// Birinchi o'lchovda bo'sh, tasdiqlashda ODAM BOR -> dars TEGILMAYDI.
    /// </summary>
    [Fact]
    public async Task RoomThatRefillsBeforeTheSecondCheck_IsLeftAlone()
    {
        var sessionId = await CreateLiveSessionAsync(ago: TimeSpan.FromMinutes(100));

        // 1-o'lchov: bo'sh. 2-o'lchov: ustoz qaytib keldi.
        factory.Presence.DefaultCount = 1;
        factory.Presence.Script(0, 1);

        await factory.RunSessionJobAsync();

        (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Live,
            "sahifa yangilanishiga tushib qolgan dars uzilmasligi kerak");

        factory.Notifier.Ended.Should().NotContain(sessionId);
    }

    /// <summary>
    /// IKKALA o'lchovda ham bo'sh -> dars yopiladi. Bu — himoyaning
    /// darsni umuman yopmaydigan holga keltirmaganini isbotlaydi.
    /// </summary>
    [Fact]
    public async Task RoomEmptyOnBothChecks_IsClosed()
    {
        var sessionId = await CreateLiveSessionAsync(ago: TimeSpan.FromMinutes(100));

        factory.Presence.DefaultCount = 0;
        factory.Presence.Script(0, 0);

        await factory.RunSessionJobAsync();

        (await StatusOfAsync(sessionId)).Should().Be(SessionStatus.Ended);
        factory.Notifier.Ended.Should().Contain(sessionId);
    }

    // ------------------------------------------------------------------ yordamchi

    private async Task<long> CreateLiveSessionAsync(TimeSpan ago)
    {
        var groupId = await factory.SeededGroupIdAsync();
        var start = DateTimeOffset.UtcNow - ago;

        return await factory.WithDbAsync(async db =>
        {
            var session = new LiveSession
            {
                GroupId = groupId,
                Title = "Tasdiqlash testi",
                Type = SessionType.Teacher,
                Status = SessionStatus.Live,
                ScheduledStart = start,
                ScheduledEnd = start + Lesson,
                ActualStart = start,
                RoomName = LiveSession.GenerateRoomName(),
            };

            db.LiveSessions.Add(session);
            await db.SaveChangesAsync();

            return session.Id;
        });
    }

    private Task<SessionStatus> StatusOfAsync(long sessionId) =>
        factory.WithDbAsync(db => db.LiveSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.Status)
            .FirstAsync());
}
