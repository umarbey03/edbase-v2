using System.IO.Pipelines;
using System.Security.Claims;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Zinnur.IntegrationTests.Notifications;

/// <summary>
/// ========================================================================
/// 🔴 `Clients.User(...)` KIMGA YETADI — ZANJIRNING QULFI
/// ========================================================================
///
/// Bu repozitoriydagi BIRINCHI foydalanuvchi darajasidagi kanal
/// (<c>NotificationHub</c>). Mavjud ikki hub <c>Clients.Group</c> ishlatadi
/// va ularning xona nomini BIZ yasaymiz — ya'ni xato darhol ko'rinadi.
/// Bu yerda esa yo'nalish UCH mustaqil bo'g'inga tayanadi:
///
///   1) tokenda foydalanuvchi Id'si <c>sub</c> claim'ida
///      (<c>JwtTokenService</c>);
///   2) <c>JwtBearer</c> ning kirish xaritasi <c>sub</c> ni
///      <c>ClaimTypes.NameIdentifier</c> ga o'giradi (standart holat,
///      <c>Program.cs</c> da ATAYLAB o'zgartirilmagan);
///   3) SignalR ning standart <c>DefaultUserIdProvider</c> i ulanish
///      egasini AYNAN <c>ClaimTypes.NameIdentifier</c> dan oladi.
///
/// Bo'g'inlardan birortasi uzilsa nosozlik JIMGINA bo'ladi: hub ulanadi,
/// xato chiqmaydi, log toza — faqat bildirishnoma hech kimga bormaydi.
///
/// ── QAYSI BO'G'IN QAYERDA SINALADI ─────────────────────────────────────
///
///  • (1) va (2) ALLAQACHON qoplangan: har bir avtorizatsiyalangan
///    endpoint testi <c>ClaimTypes.NameIdentifier</c> ni o'qiydigan
///    controller orqali o'tadi (<c>NotificationsController.CurrentUserId</c>
///    ham shunday), va hub testlari (<c>GroupChatRealtimeTests</c>,
///    <c>LiveChatBroadcastTests</c>) uni HUB ulanishi ichida o'qiydi.
///    Zanjir uzilsa o'sha testlar qizaradi.
///
///  • (3) — SHU YERDA, va bu yagona joy. U framework xulqi, ya'ni bizning
///    kodimizda emas: ASP.NET Core versiyasi almashganda yoki kimdir
///    maxsus <c>IUserIdProvider</c> qo'shganda o'zgarishi mumkin.
///
/// ★ BU TEST BAZAGA TEGMAYDI — fixture ham, migratsiya ham kerak emas.
///   Ataylab: u eng arzon va eng tez qulf, ya'ni sxema tayyor bo'lmagan
///   holatda ham ishlaydi.
/// </summary>
public class NotificationUserIdTests
{
    /// <summary>
    /// 🔴 ASOSIY TASDIQ: SignalR ulanish egasini <c>NameIdentifier</c>
    /// claim'idan oladi va u AYNAN bizning foydalanuvchi Id'mizga teng.
    ///
    /// Ya'ni <c>NotificationNotifier</c> dagi
    /// <c>Clients.User(userId.ToString(InvariantCulture))</c> to'g'ri
    /// ulanishga tushadi.
    /// </summary>
    [Fact]
    public void DefaultUserIdProvider_ResolvesNameIdentifierClaim()
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, "4271"));

        UserIdOf(principal).Should().Be("4271",
            "`Clients.User(...)` AYNAN shu claim bo'yicha ulanish topadi");
    }

    /// <summary>
    /// ★ QISQA `sub` NOMI O'ZI YETARLI EMAS — xaritalash SHART.
    ///
    /// Bu test (2)-bo'g'inning NEGA kerakligini ko'rsatadi: agar kimdir
    /// <c>Program.cs</c> da <c>MapInboundClaims = false</c> qilsa,
    /// principal'da faqat xom <c>sub</c> qolardi va bu yerdagi qidiruv
    /// HECH NIMA topmasdi. Nosozlik jimgina bo'lardi — shuning uchun
    /// kutilayotgan natija ATAYLAB `null`: kim bu testni "tuzatmoqchi"
    /// bo'lsa, avval izohni o'qishi kerak.
    /// </summary>
    [Fact]
    public void DefaultUserIdProvider_WithRawSubClaimOnly_ResolvesNothing()
    {
        var principal = PrincipalWith(new Claim("sub", "4271"));

        UserIdOf(principal).Should().BeNull(
            "`sub` xaritalanmasa SignalR ulanish egasini topa olmaydi — "
            + "`Program.cs` dagi standart kirish xaritasiga TEGILMASIN");
    }

    /// <summary>Autentifikatsiyasiz ulanishda ega yo'q (hub `[Authorize]` bilan qo'riqlanadi).</summary>
    [Fact]
    public void DefaultUserIdProvider_WithoutClaims_ResolvesNothing() =>
        UserIdOf(new ClaimsPrincipal(new ClaimsIdentity())).Should().BeNull();

    // ================================================================== yordamchi

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));

    /// <summary>
    /// HAQIQIY <see cref="DefaultUserIdProvider"/> ni HAQIQIY
    /// <see cref="HubConnectionContext"/> ustida chaqiradi — soxta emas.
    /// Soxta bo'lsa test faqat o'z nusxamizni tekshirardi va framework
    /// o'zgarganda baribir yashil qolardi.
    /// </summary>
    private static string? UserIdOf(ClaimsPrincipal principal)
    {
        var connection = new StubConnectionContext();
        connection.Features.Set<IConnectionUserFeature>(new StubUserFeature { User = principal });

        var context = new HubConnectionContext(
            connection, new HubConnectionContextOptions(), NullLoggerFactory.Instance);

        return new DefaultUserIdProvider().GetUserId(context);
    }

    /// <summary>Eng kichik <see cref="ConnectionContext"/> — faqat `Features` muhim.</summary>
    private sealed class StubConnectionContext : ConnectionContext
    {
        private readonly Pipe _inbound = new();
        private readonly Pipe _outbound = new();

        public override string ConnectionId { get; set; } = "test-connection";

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override IDictionary<object, object?> Items { get; set; } =
            new Dictionary<object, object?>();

        public override IDuplexPipe Transport { get; set; } = null!;

        public StubConnectionContext() =>
            Transport = new StubDuplexPipe(_inbound.Reader, _outbound.Writer);

        private sealed class StubDuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
        {
            public PipeReader Input { get; } = input;

            public PipeWriter Output { get; } = output;
        }
    }

    private sealed class StubUserFeature : IConnectionUserFeature
    {
        public ClaimsPrincipal? User { get; set; }
    }
}
