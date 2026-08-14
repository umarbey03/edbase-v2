using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Persistence;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// Dars yakunlanganda xona REAL VAQTDA xabardor qilinadimi.
///
/// NIMA UCHUN ALOHIDA SINF: bu xatti-harakat ilgari UMUMAN yo'q edi —
/// frontend `SessionEnded` hodisasini tinglardi, backend esa uni hech qachon
/// yubormasdi (`IHubContext` butun `backend/src` da uchramasdi). Ustoz darsni
/// yakunlaganda o'quvchilar ekranida hech narsa o'zgarmasdi: video ulanishi
/// ochiq qolardi va ular sahifani qo'lda yangilamaguncha bilmasdi.
///
/// Testlar SignalR transportini emas, PORTNI tekshiradi
/// (<see cref="ILiveSessionNotifier"/>): use-case xabarni yuborishga
/// buyurdimi, qaysi holatda buyurmadi va — eng muhimi — buni ma'lumot
/// SAQLANGANDAN KEYIN qildimi.
/// </summary>
public sealed class LiveSessionEndBroadcastTests(NotifierSpyFactory factory)
    : IClassFixture<NotifierSpyFactory>
{
    [Fact]
    public async Task End_AsHost_NotifiesRoom()
    {
        var sessionId = await CreateScheduledSessionAsync();
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var response = await client.PostAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/end", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Spy.Ended.Should().Contain(sessionId,
            "dars yakunlangani xonadagi o'quvchilarga darhol yetishi kerak");
    }

    /// <summary>
    /// ★ COMMIT-THEN-SEND. Xabar yuborilayotgan payt bazadagi holat ALLAQACHON
    /// <c>Ended</c> bo'lishi shart.
    ///
    /// Teskarisi (avval xabar, keyin saqlash) jimgina buziladi: saqlash
    /// yiqilsa o'quvchilarda "dars tugadi" ekrani chiqib, baza esa darsni jonli
    /// deb turaverardi. Eski tizimning xatosi aynan shu edi, shuning uchun bu
    /// tartib TEST bilan qulflanadi — kelajakda kimdir qatorlarni almashtirsa
    /// test yiqiladi.
    /// </summary>
    [Fact]
    public async Task End_NotifiesOnlyAfterStatusIsSaved()
    {
        var sessionId = await CreateScheduledSessionAsync();
        var tokens = await factory.LoginAsAdminAsync();
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        await client.PostAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/end", UriKind.Relative),
            content: null);

        factory.Spy.StatusWhenNotified.Should().ContainKey(sessionId);
        factory.Spy.StatusWhenNotified[sessionId].Should().Be(SessionStatus.Ended,
            "xabar yuborilganda o'zgarish bazada saqlangan bo'lishi kerak");
    }

    /// <summary>
    /// Ruxsat yo'q bo'lsa xabar HAM ketmasligi kerak: aks holda begona odam
    /// so'rov yuborib, jonli darsdagi hammani ekrandan chiqarib yuborardi
    /// (dars esa aslida davom etardi).
    /// </summary>
    [Fact]
    public async Task End_WhenForbidden_DoesNotNotify()
    {
        var sessionId = await CreateScheduledSessionAsync();
        var (email, password) = await CreateOutsiderStudentAsync();
        var tokens = await factory.LoginAsync(email);
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);

        var response = await client.PostAsync(
            new Uri($"/api/v1/live-sessions/{sessionId}/end", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Spy.Ended.Should().NotContain(sessionId);
    }

    // ------------------------------------------------------------------ yordamchi

    /// <summary>
    /// HAR TEST O'Z darsini yaratadi: sinf fixture'i umumiy bo'lgani uchun
    /// bitta darsni bo'lishsa, testlar tartibiga bog'liq bo'lib qolardi
    /// ("allaqachon yakunlangan" holati).
    /// </summary>
    private async Task<long> CreateScheduledSessionAsync() =>
        await factory.WithDbAsync(async db =>
        {
            var groupId = await db.Groups.Select(g => g.Id).FirstAsync();
            var now = DateTimeOffset.UtcNow;

            var session = new LiveSession
            {
                GroupId = groupId,
                Title = "Broadcast testi",
                Type = SessionType.Teacher,
                Status = SessionStatus.Scheduled,
                ScheduledStart = now,
                ScheduledEnd = now.AddMinutes(80),
                RoomName = LiveSession.GenerateRoomName(),
            };

            db.LiveSessions.Add(session);
            await db.SaveChangesAsync();
            return session.Id;
        });

    private async Task<(string Email, string Password)> CreateOutsiderStudentAsync()
    {
        const string password = "Student!2345";
        var email = $"broadcast-{Guid.NewGuid():N}"[..22] + "@zinnur.uz";

        await factory.WithDbAsync(async db =>
        {
            using var scope = factory.Services.CreateScope();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            db.Users.Add(new User
            {
                FullName = "Begona O'quvchi",
                Email = email,
                PasswordHash = await hasher.HashAsync(password),
                Role = UserRole.Student,
                IsActive = true,
            });
            return await db.SaveChangesAsync();
        });

        return (email, password);
    }
}

/// <summary>
/// Haqiqiy SignalR yuboruvchisi o'rniga JOSUS (spy) qo'yilgan API.
///
/// SignalR transportining o'zi bu yerda tekshirilmaydi — u framework'ning ishi.
/// Tekshiriladigan narsa: use-case xabar yuborishni SO'RADIMI va qanday
/// holatda so'radi. Shuning uchun port almashtiriladi.
/// </summary>
public sealed class NotifierSpyFactory : ZinnurApiFactory
{
    public LiveSessionNotifierSpy Spy { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            // Haqiqiy (SignalR) implementatsiyani OLIB TASHLAYMIZ — aks holda
            // qaysi biri ishlashini ro'yxat tartibi hal qilardi.
            services.RemoveAll<ILiveSessionNotifier>();

            // Josus baza holatini O'ZI o'qiy olishi uchun unga scope fabrikasi
            // beriladi (ro'yxatga olish paytida emas — konteyner qurilgach).
            services.AddSingleton(sp =>
            {
                Spy.UseScopes(sp.GetRequiredService<IServiceScopeFactory>());
                return Spy;
            });
            services.AddScoped<ILiveSessionNotifier>(sp =>
                sp.GetRequiredService<LiveSessionNotifierSpy>());
        });
    }
}

/// <summary>Chaqiruvlarni va chaqirilgan PAYTDAGI baza holatini yozib boradi.</summary>
public sealed class LiveSessionNotifierSpy : ILiveSessionNotifier
{
    private IServiceScopeFactory? _scopeFactory;

    /// <summary>Baza holatini o'qish uchun scope fabrikasini ulaydi.</summary>
    internal void UseScopes(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <summary>Yakunlangani xabar qilingan darslar.</summary>
    public ConcurrentBag<long> Ended { get; } = [];

    /// <summary>Xabar yuborilgan paytda bazadagi holat (commit-then-send dalili).</summary>
    public ConcurrentDictionary<long, SessionStatus> StatusWhenNotified { get; } = new();

    public async Task SessionEndedAsync(long sessionId, CancellationToken ct = default)
    {
        Ended.Add(sessionId);

        if (_scopeFactory is null) return;

        // YANGI scope — so'rovning o'z DbContext'i emas: shu sababli o'qilgan
        // qiymat haqiqatan bazaga YOZILGAN holat bo'ladi, kesh emas.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var status = await db.LiveSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.Status)
            .FirstOrDefaultAsync(ct);

        StatusWhenNotified[sessionId] = status;
    }
}
