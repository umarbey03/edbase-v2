using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Recordings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Api;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TUNGI NAVBAT — QATORNI EGALLASH (SPEC-RECORDING-V2 §4.4)
/// ════════════════════════════════════════════════════════════════════════
///
/// Bu to'plamdagi barcha testlar HAQIQIY Postgres bilan ishlaydi va
/// buning sababi bitta: tekshirilayotgan narsa bizning kodimiz emas,
/// <c>FOR UPDATE SKIP LOCKED</c> ning O'ZI. Soxta baza bilan yashil
/// natija hech nimani isbotlamasdi.
///
/// ── QO'RIQLANADIGAN UCHTA XATO ──────────────────────────────────────────
///
/// 🔴 1) IKKI KODLOVCHI BITTA QATORDA. Ikkalasi AYNI yakuniy kalitga
///       yozardi va natija buzuq mp4 bo'lardi — buni hech kim sezmasdi,
///       chunki ikkala jarayon ham "muvaffaqiyat" deb yozardi.
///
/// 🔴 2) ENG ESKISI ORQADA QOLISHI. Tungi oynaga sig'magan ish keyingi
///       kechada BIRINCHI bo'lib olinishi kerak — bu loyiha egasining
///       oshkor talabi. Aks holda band kunlarda eng eski yozuv abadiy
///       navbatning oxirida qolardi.
///
/// 🔴 3) YARIM QOLGAN ISHNI "DAVOM ETTIRISH". Ijarasi eskirgan qator —
///       qulagan ishchidan qolgan ish va u BOSHIDAN boshlanishi kerak,
///       urinish esa SANALISHI kerak: aks holda har kecha aynan o'sha
///       joyda qulaydigan ish abadiy aylanardi.
/// </summary>
public sealed class CompositionQueueTests(CompositionFactory factory)
    : IClassFixture<CompositionFactory>
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    // ═══════════════════════════════════════════════════ 1) mutlaq mustasnolik

    /// <summary>
    /// 🔴 IKKI ISHCHI BIR VAQTDA URINADI — G'OLIB AYNAN BITTA.
    ///
    /// Har ikkalasi O'Z scope'ida, ya'ni O'Z <c>DbContext</c> va O'Z
    /// ulanishida ishlaydi: bu ikki KONTEYNER holatining eng yaqin
    /// taqlidi.
    /// </summary>
    [Fact]
    public async Task Claim_TwoWorkersRacing_ProduceExactlyOneWinner()
    {
        var sessionId = await NewSessionAsync();

        await CompositionWorld.AddRecordingAsync(factory, sessionId);

        var first = factory.ClaimAsync(Lease);
        var second = factory.ClaimAsync(Lease);

        var claims = await Task.WhenAll(first, second);

        claims.Count(c => c is not null).Should().Be(
            1, "bitta qatorni ikki kodlovchi olsa ikkalasi bitta kalitga yozardi");
    }

    /// <summary>
    /// Navbat bo'sh bo'lsa egallash <c>null</c> qaytaradi — bu NORMAL
    /// holat va u kunning katta qismida sodir bo'ladi.
    /// </summary>
    [Fact]
    public async Task Claim_OnAnEmptyQueue_ReturnsNothing()
    {
        await NewSessionAsync();

        (await factory.ClaimAsync(Lease)).Should().BeNull();
    }

    // ═══════════════════════════════════════════════════ 2) eng eskisi birinchi

    /// <summary>
    /// 🔴 ENG ESKISI BIRINCHI — TUNGI OYNAGA SIG'MAGAN ISH KEYINGI
    /// KECHADA BIRINCHI BO'LIB OLINADI.
    ///
    /// Uchta qator teskari tartibda yaratiladi (eng yangisi birinchi),
    /// egallash esa ularni <c>CreatedAt</c> bo'yicha qaytarishi kerak.
    /// </summary>
    [Fact]
    public async Task Claim_TakesTheOldestFirst()
    {
        await NewSessionAsync();

        var now = DateTimeOffset.UtcNow;

        // ⚠️ HAR YOZUV O'Z DARSIDA: `UX_SessionRecordings_SessionId_Pipeline_Active`
        //    bitta darsga bitta TIRIK urinishdan ortig'iga ruxsat bermaydi
        //    (§2.5). Bu indeks bu yerda TO'SIQ emas — u aynan shu
        //    invariantni himoya qilyapti va uni chetlab o'tish mumkin emas.
        var newest = await CompositionWorld.AddRecordingAsync(
            factory, await AnotherSessionAsync(), createdAt: now.AddHours(-1));

        var oldest = await CompositionWorld.AddRecordingAsync(
            factory, await AnotherSessionAsync(), createdAt: now.AddDays(-3));

        var middle = await CompositionWorld.AddRecordingAsync(
            factory, await AnotherSessionAsync(), createdAt: now.AddDays(-1));

        var order = new List<long?>
        {
            (await factory.ClaimAsync(Lease))?.RecordingId,
            (await factory.ClaimAsync(Lease))?.RecordingId,
            (await factory.ClaimAsync(Lease))?.RecordingId,
        };

        order.Should().Equal(oldest, middle, newest);
    }

    // ═══════════════════════════════════════════════════ 3) ijara

    /// <summary>
    /// Tirik ijarali qator EGALLANMAYDI — uni boshqa ishchi hozir
    /// kodlamoqda.
    /// </summary>
    [Fact]
    public async Task Claim_SkipsARowWithALiveLease()
    {
        var sessionId = await NewSessionAsync();

        await CompositionWorld.AddRecordingAsync(
            factory,
            sessionId,
            composition: RecordingCompositionStatus.Running,
            leaseUntil: factory.Clock.GetUtcNow().AddMinutes(4));

        (await factory.ClaimAsync(Lease)).Should().BeNull();
    }

    /// <summary>
    /// 🔴 IJARASI ESKIRGAN QATOR — QULAGAN ISHCHIDAN QOLGAN ISH.
    ///
    /// U egallanadi, URINISH SANALADI va xodimga ko'rinadigan sabab
    /// yoziladi. Urinish sanalmasa, har kecha aynan o'sha joyda
    /// qulaydigan ish abadiy qaytaverardi va buni hech kim sezmasdi.
    /// </summary>
    [Fact]
    public async Task Claim_TakesOverAnExpiredLease_AndCountsARealAttempt()
    {
        var sessionId = await NewSessionAsync();

        var recordingId = await CompositionWorld.AddRecordingAsync(
            factory,
            sessionId,
            composition: RecordingCompositionStatus.Running,
            leaseUntil: factory.Clock.GetUtcNow().AddMinutes(-1),
            attempts: 1);

        var claim = await factory.ClaimAsync(Lease);

        claim.Should().NotBeNull();
        claim!.RecordingId.Should().Be(recordingId);
        claim.TookOverExpiredLease.Should().BeTrue();

        var row = await CompositionWorld.ReloadAsync(factory, recordingId);

        row.CompositionAttempts.Should().Be(2, "uzilib qolgan urinish HAQIQIY nosozlik");
        row.CompositionStatus.Should().Be(RecordingCompositionStatus.Running);
        row.CompositionLeaseUntil.Should().BeAfter(factory.Clock.GetUtcNow());
        row.CompositionError.Should().Be(
            "Oldingi yig'ish urinishi uzilib qoldi — boshidan boshlanmoqda.");
    }

    /// <summary>
    /// Navbatdagi qatorni egallash urinish SARFLAMAYDI — u nosozlikdan
    /// keyingi qayta urinish emas, oddiy ish.
    /// </summary>
    [Fact]
    public async Task Claim_OfAQueuedRow_DoesNotCountAnAttempt()
    {
        var sessionId = await NewSessionAsync();

        var recordingId = await CompositionWorld.AddRecordingAsync(factory, sessionId);

        var claim = await factory.ClaimAsync(Lease);

        claim!.TookOverExpiredLease.Should().BeFalse();

        (await CompositionWorld.ReloadAsync(factory, recordingId))
            .CompositionAttempts.Should().Be(0);
    }

    /// <summary>
    /// 🔴 SQL VA DOMAIN HOLAT MASHINASI BIR XIL NATIJA BERISHI SHART.
    ///
    /// Egallash bitta SQL bayonoti bo'lishi SHART (§4.4), ya'ni o'tish
    /// qoidasi <c>SessionRecording.TryClaimComposition</c> da ham,
    /// bayonotda ham yozilgan. Ikki nusxa bir kun ajralib ketishi mumkin
    /// — bu test aynan o'shani ushlaydi.
    /// </summary>
    [Fact]
    public async Task Claim_HasTheSameEffectAsTheDomainStateMachine()
    {
        var sessionId = await NewSessionAsync();

        var recordingId = await CompositionWorld.AddRecordingAsync(
            factory,
            sessionId,
            composition: RecordingCompositionStatus.Running,
            leaseUntil: factory.Clock.GetUtcNow().AddMinutes(-1),
            attempts: 1);

        await factory.ClaimAsync(Lease);

        var actual = await CompositionWorld.ReloadAsync(factory, recordingId);

        // AYNI kirish holatidagi obyekt, AYNI o'tish — domain metodi bilan.
        var expected = new SessionRecording
        {
            SessionId = sessionId,
            ObjectKey = actual.ObjectKey,
            Pipeline = RecordingPipeline.TrackComposition,
            CompositionStatus = RecordingCompositionStatus.Running,
            CompositionLeaseUntil = factory.Clock.GetUtcNow().AddMinutes(-1),
            CompositionAttempts = 1,
        };

        expected.TryClaimComposition(factory.Clock.GetUtcNow(), Lease).Should().BeTrue();

        actual.CompositionStatus.Should().Be(expected.CompositionStatus);
        actual.CompositionAttempts.Should().Be(expected.CompositionAttempts);
        actual.CompositionError.Should().Be(expected.CompositionError);
    }

    // ═══════════════════════════════════════════════════ 4) begona qatorlar

    /// <summary>
    /// 🔴 ESKI QUVURNING QATORIGA TEGILMAYDI. Unda yig'ish bosqichi
    /// umuman yo'q va uni "navbatdagi" deb olish LiveKit yozib bergan
    /// tayyor faylni ustidan yozardi.
    /// </summary>
    [Fact]
    public async Task Claim_IgnoresTheOldPipeline()
    {
        var sessionId = await NewSessionAsync();

        await factory.WithDbAsync(async db =>
        {
            db.SessionRecordings.Add(new SessionRecording
            {
                SessionId = sessionId,
                Status = RecordingStatus.Active,
                Pipeline = RecordingPipeline.RoomComposite,
                ObjectKey = $"recordings/test/{Guid.NewGuid():N}.mp4",
            });

            return await db.SaveChangesAsync();
        });

        (await factory.ClaimAsync(Lease)).Should().BeNull();
    }

    /// <summary>Yakunlangan va yiqilgan qatorlar navbatga qaytmaydi.</summary>
    [Theory]
    [InlineData(RecordingCompositionStatus.Completed)]
    [InlineData(RecordingCompositionStatus.Failed)]
    [InlineData(RecordingCompositionStatus.Collecting)]
    public async Task Claim_IgnoresNonQueuedStates(RecordingCompositionStatus status)
    {
        var sessionId = await NewSessionAsync();

        await CompositionWorld.AddRecordingAsync(factory, sessionId, composition: status);

        (await factory.ClaimAsync(Lease)).Should().BeNull();
    }

    // ═══════════════════════════════════════════════════ 5) ijarani uzaytirish

    /// <summary>Ishlayotgan ishchi ijarani uzaytiradi.</summary>
    [Fact]
    public async Task Renew_ExtendsTheLease()
    {
        var sessionId = await NewSessionAsync();
        var recordingId = await CompositionWorld.AddRecordingAsync(factory, sessionId);

        var claim = await factory.ClaimAsync(Lease);

        var before = (await CompositionWorld.ReloadAsync(factory, recordingId))
            .CompositionLeaseUntil;

        factory.Clock.Set(factory.Clock.GetUtcNow().AddMinutes(1));

        (await RenewAsync(claim!, TimeSpan.FromMinutes(5))).Should().BeTrue();

        (await CompositionWorld.ReloadAsync(factory, recordingId))
            .CompositionLeaseUntil.Should().BeAfter(before!.Value);
    }

    /// <summary>
    /// 🔴 QATORNI BOSHQA ISHCHI OLGAN BO'LSA UZAYTIRISH RAD ETILADI.
    ///
    /// Busiz uzoq qotib qolgan birinchi ishchi uyg'onib, qatorni O'ZIGA
    /// QAYTARIB OLARDI — ikkalasi ham AYNI kalitga yozar edi. Egalik
    /// chiptasi — egallash paytida yozilgan <c>CompositionStartedAt</c>.
    /// </summary>
    [Fact]
    public async Task Renew_AfterAnotherWorkerTookOver_IsRefused()
    {
        var sessionId = await NewSessionAsync();
        var recordingId = await CompositionWorld.AddRecordingAsync(factory, sessionId);

        var stalled = await factory.ClaimAsync(Lease);

        // Birinchi ishchi qotib qoldi, ijara eskirdi va qatorni ikkinchisi oldi.
        factory.Clock.Set(factory.Clock.GetUtcNow().AddMinutes(10));

        var takeover = await factory.ClaimAsync(Lease);

        takeover.Should().NotBeNull();
        takeover!.RecordingId.Should().Be(recordingId);

        (await RenewAsync(stalled!, Lease)).Should().BeFalse(
            "qator endi bizniki emas");

        (await RenewAsync(takeover, Lease)).Should().BeTrue(
            "yangi egasi uzaytira olishi kerak");
    }

    /// <summary>Yakunlangan qatorning ijarasi uzaytirilmaydi.</summary>
    [Fact]
    public async Task Renew_OfAFinishedRow_IsRefused()
    {
        var sessionId = await NewSessionAsync();
        var recordingId = await CompositionWorld.AddRecordingAsync(factory, sessionId);

        var claim = await factory.ClaimAsync(Lease);

        await factory.WithDbAsync(async db =>
        {
            var row = await db.SessionRecordings.FirstAsync(r => r.Id == recordingId);
            row.CompositionStatus = RecordingCompositionStatus.Completed;

            return await db.SaveChangesAsync();
        });

        (await RenewAsync(claim!, Lease)).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════ yordamchilar

    private async Task<bool> RenewAsync(CompositionClaim claim, TimeSpan lease)
    {
        using var scope = factory.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IRecordingCompositionStore>()
            .RenewAsync(claim.RecordingId, claim.ClaimedAt, lease);
    }

    /// <summary>
    /// Guruh + dars, VA NAVBATNI TOZALASH.
    ///
    /// 🔴 TOZALASH SHART: egallash butun BAZA bo'yicha ishlaydi (u
    /// "eng eski navbatdagi qator" ni izlaydi), test sinfi esa bitta
    /// bazani baham ko'radi. Tozalanmasa qo'shni testning qoldig'i
    /// natijani o'zgartirardi va ular xUnit ning tartibiga bog'liq —
    /// ya'ni "flaky", eng chalg'ituvchi turdagi nosozlik.
    ///
    /// ⚠️ SOAT HAM QAYTARILADI: ba'zi testlar uni oldinga suradi va
    ///    surilgan soat oldingi testlarning "tirik" ijaralarini
    ///    eskirtirib yuborardi.
    /// </summary>
    private async Task<long> NewSessionAsync()
    {
        await factory.WithDbAsync(db => db.SessionRecordings.ExecuteDeleteAsync());

        factory.Clock.Set(DateTimeOffset.UtcNow);

        return await AnotherSessionAsync();
    }

    /// <summary>Yana bitta dars — navbatni TOZALAMASDAN.</summary>
    private async Task<long> AnotherSessionAsync()
    {
        _world ??= await WorldBuilder.CreateAsync(factory, "cq");

        return await RecordingWorld.AddSessionAsync(
            factory, _world.GroupId, SessionStatus.Ended, _world.Teacher.Id);
    }

    /// <summary>Guruh/foydalanuvchilar HAR TESTDA qayta yaratilmaydi — ular arzon emas.</summary>
    private StudentWorld? _world;
}
