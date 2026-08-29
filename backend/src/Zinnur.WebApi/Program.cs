using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Zinnur.Application;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.GroupChat.Services;
using Zinnur.Application.Jobs;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Recordings.Jobs;
using Zinnur.Application.Recordings.Services;
using Zinnur.Infrastructure;
using Zinnur.Infrastructure.Persistence;
using Zinnur.WebApi;
using Zinnur.WebApi.Controllers;
using Zinnur.WebApi.Hubs;
using Zinnur.WebApi.Middleware;
using Zinnur.WebApi.Observability;
using Zinnur.WebApi.Services;
using Zinnur.WebApi.Telegram;
using Zinnur.WebApi.Workers;

// ============================================================================
// ZIN-NUR API — kompozitsiya ildizi (composition root).
// Bu YAGONA joy barcha qatlamlarni bir-biriga ulaydi.
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- kuzatuv
// Sentry — xato kuzatuvi. IXTIYORIY: `Sentry:Dsn` bo'sh bo'lsa umuman
// ishga tushmaydi va ilova odatdagidek ishlayveradi (dev mashinasida DSN yo'q).
// Serilog'dan OLDIN turadi: sink SDK allaqachon tayyor bo'lishini kutadi.
builder.AddZinnurSentry();

// Serilog: strukturali log. Konteynerda stdout'ga chiqadi va Docker yig'adi.
// Prod'da JSON (CLEF), dev'da o'qiladigan matn — batafsil: SerilogSetup.cs.
builder.Host.UseSerilog(SerilogSetup.Configure);

// ---------------------------------------------------------------- qatlamlar
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Chat yozuvchisi: BITTA instance ham IChatMessageWriter, ham fon xizmati.
// Ikkalasi bir obyekt bo'lishi SHART — aks holda hub bir kanalga yozadi,
// fon xizmati esa boshqasini o'qib, xabarlar hech qachon saqlanmaydi.
builder.Services.AddSingleton<ChatMessageWriter>();
builder.Services.AddSingleton<IChatMessageWriter>(sp => sp.GetRequiredService<ChatMessageWriter>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ChatMessageWriter>());

// Jonli dars xabarnomasi: use-case (`Application`) SignalR ni bilmaydi, shuning
// uchun port shu yerda — WebApi tomonida — ulanadi. `IHubContext` singleton,
// lekin ro'yxat scoped: uni ishlatadigan `LiveSessionService` ham scoped.
builder.Services.AddScoped<ILiveSessionNotifier, LiveSessionNotifier>();

// Guruh chati xabarnomasi (FAZA 6) — o'sha naqsh: port `Application` da,
// SignalR amalga oshirilishi shu yerda. SCOPED, chunki uni chaqiradigan
// `GroupChatService` ham scoped (`DbContext` ga bog'langan).
builder.Services.AddScoped<IGroupChatNotifier, GroupChatNotifier>();

// Bildirishnoma kanali (R35/R36) — o'sha naqsh: port `Application` da,
// SignalR amalga oshirilishi shu yerda. SCOPED, chunki uni chaqiradigan
// `AssignmentService` ham scoped.
//
// 🔴 BU REPOZITORIYDAGI YAGONA `Clients.User(...)` YO'LI: mavjud ikki hub
//    xonaga (`Clients.Group`) yuboradi, bu esa ODAMGA. Batafsil sabab va
//    identifikator qanday aniqlanishi — `NotificationHub` izohida.
builder.Services.AddScoped<INotificationNotifier, NotificationNotifier>();

// Notifikatsiya navbati (FAZA 5.2): outbox yozuvchisi, tezlik chegarasi va
// fon worker'i. Xabar biznes tranzaksiyasi bilan BIRGA yoziladi, yuborish
// esa kommitdan KEYIN fon xizmatida bo'ladi — izoh: Workers/NotificationsSetup.cs.
builder.Services.AddZinnurNotifications(builder.Configuration);

// Telegram bot va Mini App (FAZA 5.1) — o'quvchilar uchun YAGONA kirish yo'li.
//
// ★ TARTIB MUHIM: `AddZinnurNotifications` DAN KEYIN turishi SHART.
//   `OutboxDispatcher` bir kanalga ikkita yuboruvchi bo'lsa OXIRGISINI
//   tanlaydi; notifikatsiya moduli esa vaqtinchalik log-yuboruvchini
//   ro'yxatdan o'tkazadi. Bu qatorni yuqoriga ko'chirsak, xabarlar
//   Telegram'ga emas, LOGGA ketardi va buni hech kim sezmasdi.
builder.Services.AddZinnurTelegram(builder.Configuration);

// Fon vazifalari (FAZA 5.5): muddati o'tgan darslarni avto-yakunlash va
// oylik to'lov yozuvlarini ochish. Rejalashtiruvchi HAR konteynerda
// ko'tariladi, lekin har vazifa Postgres advisory lock ostida yuradi —
// ya'ni ish AYNAN BIR MARTA bajariladi (izoh: Workers/JobsSetup.cs).
builder.Services.AddZinnurJobs(builder.Configuration);

// Dars yozuvi WATCHDOG'i (FAZA 5.3): boshlanmagan yozuvni qayta uradi,
// yo'qolgan webhook o'rniga ombordan tekshiradi, umidsizini `Failed` qiladi.
//
// ★ NIMA UCHUN `AddZinnurJobs` ICHIDA EMAS, SHU YERDA: rejalashtiruvchi
//   `IEnumerable<IScheduledJob>` ni o'qiydi, ya'ni vazifa qayerda
//   ro'yxatdan o'tgani AHAMIYATSIZ — u baribir AYNI qulf ostida, AYNI
//   siklda yuradi. YANGI REJALASHTIRUVCHI YOZILMADI.
//
// SCOPED: vazifa `DbContext` ga (port orqali) tayanadi va rejalashtiruvchi
// har aylanishda yangi scope ochadi (`JobSchedulerWorker`).
//
// ⚠️ CHEGARALAR HOZIRCHA KODDA (`RecordingWatchdogSettings.Default`), boshqa
//    vazifalardagidek `Jobs:*` konfiguratsiyasida EMAS. Sozlanadigan qilish
//    uchun `Jobs:RecordingWatchdog:*` bo'limini `JobsOptions` ga qo'shish
//    kifoya — vazifaning O'ZI sozlamalarni allaqachon konstruktordan oladi
//    (`SessionAutoCloseSettings` bilan AYNI naqsh).
builder.Services.AddScoped<IScheduledJob>(sp => new RecordingWatchdogJob(
    sp.GetRequiredService<IApplicationDbContext>(),
    sp.GetRequiredService<ILiveKitEgress>(),
    sp.GetRequiredService<IRecordingStorage>(),
    // Xona bo'sh bo'lsa yozuv boshlanmaydi — sabab vazifaning ichida.
    sp.GetRequiredService<IPresenceService>(),
    sp.GetRequiredService<TimeProvider>(),
    RecordingWatchdogSettings.Default,
    sp.GetRequiredService<ILogger<RecordingWatchdogJob>>()));

// ---------------------------------------------------------------- auth
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret sozlanmagan.");

if (jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret kamida 32 belgi bo'lishi kerak.");

// ════════════════════════════════════════════════════════════════════════
// 🔴 PROD'DA NAMUNA SIRLARI BILAN KO'TARILMAYMIZ (2026-08-22 auditi).
//
// Yuqoridagi ikki tekshiruv sirning MAVJUDLIGINI va UZUNLIGINI ko'radi,
// lekin `.env.example` dagi namuna qiymat aynan shu ikkalasidan ham
// muvaffaqiyatli o'tardi (u 32 belgidan uzun qilib yozilgan). Sabab va
// to'liq qoida — `ProductionSecretsGuard` izohida.
//
// ★ SHU YERDA, chunki bu — `builder` bosqichi: xato port ochilgunga va
//   migratsiyalar qo'llangunga QADAR chiqadi.
// ════════════════════════════════════════════════════════════════════════
ProductionSecretsGuard.Validate(builder.Configuration, builder.Environment);

// Tokendagi ism claim'ining QISQA nomi (JwtTokenService shu nom bilan yozadi).
const string JwtNameClaim = "name";

// Sessiya versiyasi claim'i — `JwtTokenService.TokenVersionClaim` bilan BIR XIL
// bo'lishi shart. Satr ikki joyda yozilgani uchun izoh: Infrastructure loyihasi
// WebApi'ga bog'lanmaydi, shuning uchun konstanta import qilinmaydi.
const string TokenVersionClaim = "ver";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromSeconds(30),   // default 5 daqiqa — juda ko'p

            // Claim turlarini ANIQ yozamiz — standart qiymatga tayanmaymiz.
            // Bular kirish "xaritalash" (inbound claim map) dan KEYINGI nomlar:
            // `sub` -> ClaimTypes.NameIdentifier, `role` -> ClaimTypes.Role.
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };

        options.Events = new JwtBearerEvents
        {
            // MUHIM: WebSocket qo'l berishi (handshake) Authorization header
            // yubora olmaydi. Shuning uchun SignalR tokeni query'da keladi.
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                    context.Token = token;

                return Task.CompletedTask;
            },

            // ---- BUG TUZATISHI: chatda har xabar "Noma'lum" bo'lib chiqardi ----
            //
            // Tokenda ism QISQA `name` claim'ida keladi (JwtTokenService).
            // ASP.NET ning standart kirish xaritasi (inbound claim map) esa
            // `name` ni ClaimTypes.Name ga O'GIRMAYDI — u faqat `unique_name`
            // ni o'giradi. Natijada LiveClassHub dagi
            // `FindFirstValue(ClaimTypes.Name)` hech qachon topmasdi.
            //
            // NEGA YECHIM `NameClaimType = "name"` EMAS:
            //   1) NameClaimType claim'ning SAQLANGAN turini o'zgartirmaydi —
            //      u faqat `Identity.Name` xossasi qaysi turdan o'qishini
            //      belgilaydi. `FindFirstValue(ClaimTypes.Name)` baribir topmasdi.
            //   2) Yonida `RoleClaimType = "role"` qo'yilsa esa BUZILARDI:
            //      `role` claim'i xaritalash bosqichida allaqachon
            //      ClaimTypes.Role ga aylangan, ya'ni "role" turidagi claim
            //      qolmaydi va [Authorize(Roles = ...)] hamma joyda 403 berardi.
            //
            // Shuning uchun YETISHMAYOTGAN yagona xaritalashni qo'shamiz,
            // ishlab turgan `sub`/`role` xaritalariga TEGMAYMIZ.
            OnTokenValidated = async context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity
                    && identity.FindFirst(ClaimTypes.Name) is null
                    && identity.FindFirst(JwtNameClaim) is { Value.Length: > 0 } shortName)
                {
                    identity.AddClaim(new Claim(ClaimTypes.Name, shortName.Value));
                }

                // ---- XAVFSIZLIK TUZATISHI: `ver` HAQIQATAN tekshiriladi ----
                //
                // `JwtTokenService` tokenga `ver` (TokenVersion) qo'yadi va uning
                // izohida "WebApi ham SHU nomni tekshiradi" deb yozilgan edi —
                // lekin tekshiruv YOZILMAGAN edi. Natijada imzosi to'g'ri kirish
                // tokeni 15 daqiqa davomida so'zsiz qabul qilinardi:
                //
                //   * `logout` qilingan foydalanuvchi ishlayveradi;
                //   * O'CHIRILGAN (haydalgan yoki to'lamagan) o'quvchi jonli
                //     darsga LiveKit tokeni olib, video/audio efirga chiqa olardi
                //     va chatga yozardi — jonli tekshiruvda isbotlangan.
                //
                // Kurs/vazifa/guruh servislari buni `IsActive` tekshiruvi bilan
                // qisman qoplaydi, lekin bu HAR endpointda takrorlanishi kerak
                // bo'lgan qoida edi va jonli dars servisida tushib qolgan.
                // Shuning uchun tekshiruv MARKAZIY joyga — token tasdiqlash
                // bosqichiga qo'yildi: bir marta yozilgan, hamma joyda amal
                // qiladi (SignalR ulanishi ham shu yerdan o'tadi).
                var principal = context.Principal;
                var userIdText = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var versionText = principal?.FindFirstValue(TokenVersionClaim);

                if (!long.TryParse(userIdText, CultureInfo.InvariantCulture, out var userId)
                    || !int.TryParse(versionText, CultureInfo.InvariantCulture, out var tokenVersion))
                {
                    context.Fail("Token tarkibi to'liq emas.");
                    return;
                }

                var authState = context.HttpContext.RequestServices
                    .GetRequiredService<IAuthStateCache>();

                var state = await authState
                    .GetAsync(userId, context.HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                if (state is null || !state.IsActive || state.TokenVersion != tokenVersion)
                {
                    // 401 qaytadi — klient `refresh` ga urinadi va u yerda
                    // aniq sabab bilan rad etiladi ("Sessiya bekor qilingan").
                    context.Fail("Sessiya endi yaroqli emas.");
                }
            },
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------- CORS
// Frontend alohida origin'da (Vite dev serveri yoki nginx) turadi.
const string CorsPolicy = "zinnur-web";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));       // SignalR uchun zarur

// ---------------------------------------------------------------- SignalR
var signalR = builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();

    // Klient tirikligini tekshirish. Standart 30s — 200 kishida bu ko'p
    // keraksiz trafik; 45s yetarli va uzilishni ham o'z vaqtida sezadi.
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(45);

    // Bitta xabar hajmi chegarasi (chat uchun 32 KB dan ortiq kerak emas)
    options.MaximumReceiveMessageSize = 32 * 1024;
});

// ★ ENUM'LAR HUB XABARLARIDA HAM SATR — REST bilan BIR XIL.
//
// `AddJsonOptions` (yuqorida, MVC uchun) SignalR'ga UMUMAN tegmaydi: hub
// o'z `JsonHubProtocolOptions` ini ishlatadi va u standart holatda enum'ni
// RAQAM qilib yuboradi.
//
// Bunsiz bitta va AYNI DTO ikki xil ko'rinishda ketardi: `GroupChatMessageDto`
// REST javobida `"channel": "Curator"`, hub hodisasida esa `"channel": 1`.
// Frontend bitta turdagi obyektni ikki xil tahlil qilishga majbur bo'lardi va
// enum tartibi o'zgargan kunda hub yo'li JIMGINA noto'g'ri kanalni ko'rsatardi.
//
// Mavjud `LiveClassHub` ga ta'sir qilmaydi: uning hodisalarida enum YO'Q
// (`LiveSessionDto.Status`/`Type` va `PresenceDelta.Role` — ataylab `string`).
signalR.AddJsonProtocol(options =>
    options.PayloadSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));

// Redis backplane: bir necha API instance bo'lganda xabar HAMMA instance'dagi
// klientlarga yetib borishi uchun. Bittada ham zarar qilmaydi.
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
    signalR.AddStackExchangeRedis(redisConnection, o => o.Configuration.ChannelPrefix =
        StackExchange.Redis.RedisChannel.Literal("zinnur-signalr"));

// ---------------------------------------------------------------- rate limiting
// Kirish endpointi parol topishga qarshi cheklanadi (eski tizimda bu
// jarayon xotirasida edi va har server qayta ishga tushganda nolga qaytardi).
//
// ★ SIYOSATNI E'LON QILISH YETARLI EMAS. Bu yerda u FAQAT ro'yxatdan
//   o'tadi; endpointga `[EnableRateLimiting(...)]` bilan biriktirilmasa
//   HECH NARSA qilmaydi. Ilgari aynan shunday edi — siyosat bor, atribut
//   yo'q, va bitta IP'dan 1500 ta kirish so'rovi to'siqsiz o'tgan.
//   Endi nomlar `AuthController` dagi const'lar (satr xatosi bo'lmaydi),
//   atributlar esa o'sha faylda — ikkalasi yonma-yon ko'rinadi.
//
// CHEGARA SOZLANADIGAN (`RateLimiting:Auth:*`): to'g'ri qiymat joylashuvga
// bog'liq. Bitta maktab bitta NAT IP orqasida turadi va "IP = bitta odam"
// farazi u yerda ishlamaydi. Noto'g'ri chegara yangi image yig'masdan,
// konfiguratsiya bilan tuzatilsin.
var authPermitLimit = PositiveSetting(
    builder.Configuration, "RateLimiting:Auth:PermitLimit", defaultValue: 20);

var authRefreshPermitLimit = PositiveSetting(
    builder.Configuration, "RateLimiting:Auth:RefreshPermitLimit", defaultValue: 60);

// ★ HOLAT SO'ROVI UCHUN ALOHIDA, KENG BUDJET (2026-08-28).
//
// Bot orqali kirish oqimida brauzer chipta holatini bir necha soniyada
// bir so'rab turadi (`GET /auth/telegram/status`) — bu SO'ROV EMAS,
// KUTISH usuli. Kirish siyosati (20/daqiqa) unga qo'llansa, foydalanuvchi
// botni ochib ulgurmasidan 429 olardi, bitta NAT ortidagi maktabda esa
// birinchi ikki o'quvchidan keyin oqim umuman ishlamasdi.
//
// 🔴 KENG BUDJET BU YERDA XAVFSIZ, CHUNKI ENDPOINT HECH NARSA OCHMAYDI:
//    u 128 bitlik chiptasiz "yo'q" dan boshqa javob bermaydi va bitta
//    Redis o'qishidan iborat. Ya'ni cheklovning vazifasi — faqat
//    toshqinni to'sish, sirni himoya qilish emas.
var authPollPermitLimit = PositiveSetting(
    builder.Configuration, "RateLimiting:Auth:PollPermitLimit", defaultValue: 240);

var authWindowSeconds = PositiveSetting(
    builder.Configuration, "RateLimiting:Auth:WindowSeconds", defaultValue: 60);

// ★ OCHIQ FORMA (landing'dagi «Ariza qoldirish») — 2026-08-28.
//
// 5/daqiqa. Bu ATAYLAB tor: odam bir kunda bitta ariza qoldiradi, ya'ni
// chegara haqiqiy foydalanuvchiga umuman sezilmaydi.
//
// 🔴 BU YOLG'IZ YETARLI EMAS: cheklov IP bo'yicha bo'linadi va bitta
//    maktab bitta NAT ortida turadi (`FixedWindowByIp` izohi). ASOSIY
//    himoya RAQAM bo'yicha va use-case ichida:
//    `EnrollmentApplicationService` (10 daqiqalik oyna, sutkada 3 ta).
var publicFormPermitLimit = PositiveSetting(
    builder.Configuration, "RateLimiting:PublicForm:PermitLimit", defaultValue: 5);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // ★ `Retry-After` — foydalanuvchi QANCHA kutishini bilsin.
    //
    // Ilgari 429 javobi bo'sh tana va sarlavhasiz kelardi; frontend esa
    // "Juda tez-tez so'rov yubordingiz. Biroz kuting" degan umumiy matnni
    // ko'rsatardi. "Biroz" — bu necha soniya? Foydalanuvchi bilmagach qayta
    // bosaveradi va oynani yana uzaytiradi.
    //
    // Qat'iy oynada (fixed window) aniq qolgan vaqtni limiter bermaydi,
    // shuning uchun eng yomon holat — to'liq oyna uzunligi — beriladi.
    // Bu HTTP standarti ruxsat bergan yondashuv va har doim xavfsiz tomonga
    // yaxlitlaydi.
    options.OnRejected = (context, _) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var hint)
            ? (int)Math.Ceiling(hint.TotalSeconds)
            : authWindowSeconds;

        context.HttpContext.Response.Headers.RetryAfter =
            retryAfter.ToString(CultureInfo.InvariantCulture);

        return ValueTask.CompletedTask;
    };

    // Kirish — bir martalik kod so'raladigan va tekshiriladigan joy.
    //
    // 20/daqiqa. Ilgari bu chegara PAROL topishga qarshi edi; 2026-08-13
    // dan parol yo'q, lekin chegara AYNI darajada kerak — endi u kod
    // so'rovlari toshqiniga qarshi.
    //
    // 🔴 BU CHEGARA YOLG'IZ YETARLI EMAS va u ASOSIY himoya ham EMAS.
    // Sabab quyidagi `FixedWindowByIp` izohida: proksi ortida hamma
    // bitta bo'limga tushadi, ya'ni bitta maktab o'zini o'zi bloklaydi,
    // hujumchi esa IP almashtirib chetlab o'tadi.
    //
    // ★ ASOSIY HIMOYA — TELEFON RAQAMI BO'YICHA va u use-case ichida:
    //   `IPhoneLoginCodeStore` (60 s qayta yuborish oynasi, sutkada 10 ta
    //   kod, bitta kodga 5 ta urinish). U IP'ga umuman bog'liq emas va
    //   Redis'da atomar hisoblanadi. Ya'ni bu yerdagi cheklov —
    //   birinchi, qo'pol filtr; hisob darajasidagi chegara esa pastda.
    options.AddPolicy(AuthController.LoginRateLimitPolicy,
        context => FixedWindowByIp(context, authPermitLimit, authWindowSeconds));

    // Yangilash — boshqa tahdid modeli, kengroq budjet (izoh: AuthController).
    options.AddPolicy(AuthController.RefreshRateLimitPolicy,
        context => FixedWindowByIp(context, authRefreshPermitLimit, authWindowSeconds));

    // Chipta holatini so'rab turish — sabab yuqoridagi `authPollPermitLimit` izohida.
    options.AddPolicy(AuthController.PollRateLimitPolicy,
        context => FixedWindowByIp(context, authPollPermitLimit, authWindowSeconds));

    // Landing'dagi ariza formasi — sabab `publicFormPermitLimit` izohida.
    options.AddPolicy(ApplicationsController.PublicFormRateLimitPolicy,
        context => FixedWindowByIp(context, publicFormPermitLimit, authWindowSeconds));
});

// IP bo'yicha qat'iy oyna (fixed window).
static RateLimitPartition<string> FixedWindowByIp(
    HttpContext context, int permitLimit, int windowSeconds) =>
    RateLimitPartition.GetFixedWindowLimiter(

        // DIQQAT: proksi orqasida bu proksining IP'si bo'ladi va hamma
        // bitta bo'limga tushadi. `X-Forwarded-For` ni to'g'ri hisobga
        // olish uchun `ForwardedHeaders` middleware kerak;
        // ishonchsiz header'ni shu yerda o'qish esa cheklovni bitta
        // qalbaki qator bilan chetlab o'tish imkonini berardi.
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),

            // Navbat YO'Q: oshgan so'rov kutmaydi, darhol 429 oladi. Navbat
            // bo'lsa hujumchining so'rovlari server resurslarini ushlab
            // turib, cheklovning o'zi DoS vositasiga aylanardi.
            QueueLimit = 0,
        });

// Musbat butun sonli sozlama; yo'q yoki buzuq bo'lsa — standart qiymat.
static int PositiveSetting(IConfiguration configuration, string key, int defaultValue) =>
    int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture,
        out var value) && value > 0
        ? value
        : defaultValue;

// ---------------------------------------------------------------- MVC + hujjat
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // ENUM'LAR JSON'DA SATR KO'RINISHIDA — ikki tomonga ham.
        //
        // NIMA UCHUN: bunsiz API ASSIMETRIK bo'lib qolardi — so'rovda
        // `"role": 3` (raqam) kutilardi, javobda esa `"role": "Academic"`
        // (satr) qaytardi. Klient har safar ikki tomonga o'girishga majbur
        // bo'lardi va JSON'dagi raqam hech narsa anglatmasdi.
        //
        // Yomoni: enum tartibi o'zgarsa klient JIMGINA noto'g'ri rol
        // yuborardi — `3` endi boshqa rolni anglatib qolardi.
        //
        // Satr bilan: `"role": "Academic"` o'zini tushuntiradi, noto'g'ri
        // qiymatga 400 qaytadi, va enum'ga yangi qiymat qo'shilishi mavjud
        // klientlarni buzmaydi.
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---------------------------------------------------------------- sog'liq
// LiveKit tekshiruvi uchun alohida HttpClient: qisqa timeout MAJBURIY,
// aks holda LiveKit osilib qolsa probe ham osilib, konteyner "unhealthy"
// bo'lguncha 30+ sekund ketadi.
builder.Services.AddHttpClient(LiveKitHealthCheck.HttpClientName,
    client => client.Timeout = LiveKitHealthCheck.Timeout);

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres sozlanmagan."),
        name: "postgres",
        tags: ["ready"])
    .AddRedis(
        redisConnection ?? "localhost:6379",
        name: "redis",
        tags: ["ready"])
    // LiveKit yiqilsa jonli dars ishlamaydi, LEKIN login/jadval/hisobot
    // ishlayveradi — shuning uchun Unhealthy emas, Degraded (izoh:
    // LiveKitHealthCheck.cs). Degraded'da HTTP 200 qaytadi va `web`
    // konteyneri (api: service_healthy) o'chib qolmaydi.
    .AddCheck<LiveKitHealthCheck>(
        LiveKitHealthCheck.Name,
        failureStatus: HealthStatus.Degraded,
        tags: ["ready"]);

var app = builder.Build();

// ============================================================================
// MIDDLEWARE KETMA-KETLIGI — tartib MUHIM
// ============================================================================

// 1) Xato ushlagichi ENG BIRINCHI: pastdagi hamma narsani qamrab oladi
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(CorsPolicy);
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<LiveClassHub>("/hubs/live");

// Guruh chati ALOHIDA hub'da — nima uchun `LiveClassHub` kengaytirilmagani
// `GroupChatHub` sinfi izohida batafsil (qisqasi: dars hub'ining uzilish
// yo'li DAVOMAT yozadi va unga tegilmadi).
//
// Auth uchun qo'shimcha kod KERAK EMAS: yuqoridagi `OnMessageReceived`
// `/hubs` bilan boshlanadigan HAR yo'l uchun query'dagi tokenni qabul qiladi.
app.MapHub<GroupChatHub>("/hubs/group-chat");

// Bildirishnomalar (R35/R36) — UCHINCHI hub.
//
// ★ AUTH KODI YANA KERAK EMAS (yuqoridagi `OnMessageReceived` `/hubs` bilan
//   boshlanadigan HAR yo'lni qamrab oladi), LEKIN bu yerda YANA BIR
//   sozlamaga tayanish bor: ulanish egasi `Clients.User(...)` uchun
//   SignalR ning standart `DefaultUserIdProvider` i orqali
//   `ClaimTypes.NameIdentifier` claim'idan aniqlanadi. Maxsus
//   `IUserIdProvider` ATAYLAB ro'yxatdan o'tkazilmagan: standarti
//   allaqachon to'g'ri claim'ni o'qiydi (tokendagi `sub` shu turga
//   xaritalanadi — yuqoridagi `TokenValidationParameters` izohi), o'z
//   provayderimiz esa xuddi shu qatorni takrorlab, kelajakda claim
//   nomi o'zgarganda IKKI joyni tuzatishni talab qilardi.
app.MapHub<NotificationHub>("/hubs/notifications");

// Sog'liq tekshiruvi: /health — tirikmi, /health/ready — xizmat ko'rsatishga tayyormi
app.MapHealthChecks("/health", new HealthCheckOptions
{
    // ARZON tiriklik probe'i: hech qanday bog'liqlikka tegmaydi.
    // Baza sekinlashganda ham jarayon o'zi tirik ekanini ko'rsatadi —
    // orkestrator konteynerni bekordan qayta ishga tushirmaydi.
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),

    // Har bog'liqlik ALOHIDA ko'rsatiladi (nom, holat, davomiylik) —
    // izoh va javob shakli: Observability/HealthCheckResponse.cs.
    ResponseWriter = HealthCheckResponse.WriteAsync,
});

// ---------------------------------------------------------------- migratsiya + seed
// Konteyner ko'tarilganda sxema qo'llanadi va birinchi admin yaratiladi.
await DbInitializer.InitializeAsync(app.Services);

ApiLog.ApiStarted(app.Logger, app.Environment.EnvironmentName);

// Kuzatuv holati LOGDA ko'rinsin: "Sentry nega ishlamayapti?" degan savolga
// javob birinchi qatorda turadi (DSN berilmagan bo'lsa — "o'chirilgan").
ApiLog.ObservabilityConfigured(
    app.Logger,
    SentrySetup.IsEnabled(app.Configuration) ? "yoqilgan" : "o'chirilgan",
    app.Environment.IsDevelopment() ? "matn" : "json",
    AppInfo.Release);

await app.RunAsync();

/// <summary>Integratsiya testlari uchun ochiq (WebApplicationFactory).</summary>
public partial class Program;
