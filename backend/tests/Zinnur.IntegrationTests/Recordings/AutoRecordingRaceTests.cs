using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zinnur.Application.Recordings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Persistence;
using Zinnur.IntegrationTests.Api;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 IKKI "BOSHLASH" SO'ROVI BIR VAQTDA — 500 EMAS, MA'NOLI JAVOB
/// ════════════════════════════════════════════════════════════════════════
///
/// ── NIMA UCHUN BU TEST BOR ──────────────────────────────────────────────
///
/// Navbat qatorining idempotentlik tekshiruvi (<c>SELECT</c>) va uning
/// yozilishi (<c>INSERT</c>) BOSHQA-BOSHQA lahzalarda bo'ladi: qatorni
/// <c>AutoRecordingScheduler</c> qo'shadi, saqlashni esa
/// <c>LiveSessionService.StartAsync</c> qiladi. Ikki so'rov bir vaqtda
/// kelsa ikkalasi ham "qator yo'q" deb xulosa qiladi.
///
/// M2 qo'shgan <c>UX_SessionRecordings_SessionId_Pipeline_Active</c>
/// unikal indeksi buni ENDI to'sadi — ya'ni ilgari JIMGINA ikkinchi qator
/// bo'lgan holat endi yiqilgan tranzaksiyaga aylandi. Himoyasiz kodda u
/// foydalanuvchiga "Ichki server xatosi" bo'lib ko'rinardi.
///
/// ── NIMA UCHUN POYGA ATAYLAB YASALADI ───────────────────────────────────
///
/// ⚠️ HAQIQIY parallel so'rovlar bilan bu holat KAFOLATLI takrorlanmaydi:
/// oyna bir necha millisekund va test mashinaning bandligiga qarab goh
/// tushadi, goh tushmaydi. Bunday test yashil bo'lib, hech nimani
/// tekshirmasligi mumkin — bu loyihada eng qimmat turdagi yolg'on.
///
/// Shuning uchun bu yerda navbat porti O'RALADI: haqiqiy qatordan keyin,
/// lekin <c>SaveChanges</c> dan OLDIN, BOSHQA tranzaksiyada "raqib" qator
/// yoziladi. Ya'ni oyna 100% ochiladi va HTTP yo'li — controller, servis,
/// istisno xaritasi — to'liq o'z holicha ishlaydi.
///
/// ── KUTILAYOTGAN NATIJA VA NIMA UCHUN AYNAN SHU ─────────────────────────
///
/// <c>409</c>, va yutqazgan so'rovning butun tranzaksiyasi ORQAGA
/// QAYTADI. Bu "yo'qotish" emas: dars raqib so'rov bilan allaqachon
/// boshlangan, yutqazgan so'rovning qolgan yozuvlari esa TAKROR bo'lardi —
/// o'quvchilarga ikkinchi "dars boshlandi" xabari va ikkinchi kechikish
/// jarimasi.
/// </summary>
public sealed class AutoRecordingRaceTests(RacingRecordingFactory factory)
    : IClassFixture<RacingRecordingFactory>
{
    [Fact]
    public async Task LosingRaceForTheRecordingRow_ReturnsConflict_NotServerError()
    {
        var world = await WorldBuilder.CreateAsync(factory, "racelose");

        await factory.WithDbAsync(async db =>
        {
            var group = await db.Groups.FirstAsync(g => g.Id == world.GroupId);
            group.RecordEnabled = true;

            return await db.SaveChangesAsync();
        });

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, DateTimeOffset.UtcNow);

        factory.ArmRace();

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.PostAsync($"/api/v1/live-sessions/{sessionId}/start", null);

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "unikal indeksga urilish foydalanuvchiga 'Ichki server xatosi' bo'lib "
            + "ko'rinmasligi kerak. Javob tanasi: " + body);

        // Baza holati TO'G'RI: raqib qo'ygan AYNI bitta qator qoladi.
        var rows = await factory.WithDbAsync(db => db.SessionRecordings
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId)
            .ToListAsync());

        rows.Should().ContainSingle("bir darsga bir quvurda AYNI bitta yozuv");

        rows[0].ObjectKey.Should().Be(
            RacingRecordingFactory.RivalObjectKey,
            "yutgan (raqib) qator qoladi, yutqazgani esa orqaga qaytariladi");

        // ★ ENG MUHIM YON TA'SIR: dars JONLI EMAS, chunki yutqazgan
        //   tranzaksiya butunlay orqaga qaytdi. Haqiqiy poygada uni raqib
        //   so'rov allaqachon jonli qilib bo'lgan bo'lardi; bu yerda
        //   raqib faqat yozuv qatorini qo'ygani uchun dars rejadagicha
        //   qoladi. Tekshiriladigan narsa ayni shu: YARIM holat YO'Q.
        var status = await factory.WithDbAsync(db => db.LiveSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.Status)
            .FirstAsync());

        status.Should().Be(
            SessionStatus.Scheduled,
            "yiqilgan saqlash YARIM holat qoldirmasligi kerak");
    }

    /// <summary>
    /// 🔴 TESKARI SHARTNOMA: poyga YO'Q bo'lsa hech narsa o'zgarmaydi.
    ///
    /// Busiz yuqoridagi test "har doim 409 qaytarish" bilan ham yashil
    /// bo'lardi — ya'ni dars boshlash butunlay ishdan chiqqan holatda
    /// ham.
    /// </summary>
    [Fact]
    public async Task WithoutRace_TheLessonStartsNormally()
    {
        var world = await WorldBuilder.CreateAsync(factory, "racewin");

        await factory.WithDbAsync(async db =>
        {
            var group = await db.Groups.FirstAsync(g => g.Id == world.GroupId);
            group.RecordEnabled = true;

            return await db.SaveChangesAsync();
        });

        var sessionId = await WorldBuilder.AddScheduledSessionAsync(
            factory, world.GroupId, DateTimeOffset.UtcNow);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.PostAsync($"/api/v1/live-sessions/{sessionId}/start", null);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var rows = await factory.WithDbAsync(db => db.SessionRecordings
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId)
            .ToListAsync());

        rows.Should().ContainSingle();
        rows[0].Pipeline.Should().Be(RecordingPipeline.RoomComposite);
    }
}

/// <summary>
/// Navbat portini O'RAB, dars boshlash tranzaksiyasining O'RTASIDA
/// "raqib" yozuv qatorini yozadigan fabrika.
///
/// ★ NIMA UCHUN AYNAN <see cref="IAutoRecordingScheduler"/> O'RALADI: u —
///   <c>StartAsync</c> ning ichidagi YAGONA port va u qatorni qo'shgandan
///   KEYIN, <c>SaveChanges</c> dan OLDIN chaqiriladi. Ya'ni poyga oynasi
///   aynan shu nuqtada ochiladi va boshqa hech narsani soxtalashtirish
///   kerak emas.
/// </summary>
public sealed class RacingRecordingFactory : RecordingFactory
{
    /// <summary>Raqib qatorning kaliti — testda uni ANIQ tanish uchun.</summary>
    public const string RivalObjectKey = "recordings/race/rival.mp4";

    private int _armed;

    /// <summary>Keyingi (va faqat keyingi) navbatga qo'yishda poyga yasaladi.</summary>
    public void ArmRace() => Interlocked.Exchange(ref _armed, 1);

    /// <summary>Poyga bir marta ishlaydi — qolgan testlar odatdagidek yuradi.</summary>
    internal bool TryConsumeRace() => Interlocked.Exchange(ref _armed, 0) == 1;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAutoRecordingScheduler>();

            services.AddScoped<IAutoRecordingScheduler>(sp => new RaceInjectingScheduler(
                ActivatorUtilities.CreateInstance<AutoRecordingScheduler>(sp),
                sp.GetRequiredService<IServiceScopeFactory>(),
                this));
        });
    }
}

/// <summary>
/// Haqiqiy navbatni chaqiradi, so'ng BOSHQA tranzaksiyada raqib qator
/// yozadi — ya'ni "boshqa so'rov ulgurdi" holatini kafolatli yasaydi.
/// </summary>
internal sealed class RaceInjectingScheduler(
    IAutoRecordingScheduler inner,
    IServiceScopeFactory scopeFactory,
    RacingRecordingFactory factory) : IAutoRecordingScheduler
{
    public async Task<bool> EnqueueAsync(LiveSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var queued = await inner.EnqueueAsync(session, ct);

        if (!queued || !factory.TryConsumeRace())
            return queued;

        // ⚠️ ALOHIDA SCOPE = ALOHIDA `DbContext` = ALOHIDA TRANZAKSIYA.
        //    Ayni `DbContext` ishlatilsa qator chaqiruvchining o'z
        //    saqlashiga qo'shilib ketardi va hech qanday poyga bo'lmasdi.
        using var scope = scopeFactory.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.SessionRecordings.Add(new SessionRecording
        {
            SessionId = session.Id,
            ObjectKey = RacingRecordingFactory.RivalObjectKey,
            Pipeline = RecordingPipeline.RoomComposite,
        });

        await db.SaveChangesAsync(ct);

        return queued;
    }
}
