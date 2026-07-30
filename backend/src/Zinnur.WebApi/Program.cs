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
using Zinnur.Infrastructure;
using Zinnur.Infrastructure.Persistence;
using Zinnur.WebApi;
using Zinnur.WebApi.Controllers;
using Zinnur.WebApi.Hubs;
using Zinnur.WebApi.Middleware;
using Zinnur.WebApi.Observability;
using Zinnur.WebApi.Services;

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

// ---------------------------------------------------------------- auth
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret sozlanmagan.");

if (jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret kamida 32 belgi bo'lishi kerak.");

// Tokendagi ism claim'ining QISQA nomi (JwtTokenService shu nom bilan yozadi).
const string JwtNameClaim = "name";

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
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity
                    && identity.FindFirst(ClaimTypes.Name) is null
                    && identity.FindFirst(JwtNameClaim) is { Value.Length: > 0 } shortName)
                {
                    identity.AddClaim(new Claim(ClaimTypes.Name, shortName.Value));
                }

                return Task.CompletedTask;
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

var authWindowSeconds = PositiveSetting(
    builder.Configuration, "RateLimiting:Auth:WindowSeconds", defaultValue: 60);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Kirish — parol taxmin qilinadigan YAGONA joy.
    //
    // 20/daqiqa (ilgari 10). BCrypt WorkFactor=11 da bitta urinish ~120 ms,
    // ya'ni hujumchi uchun 10 ham, 20 ham bir xil darajada umidsiz. Farq
    // FOYDALANUVCHI tomonida: 10 talik budjet bilan bitta NAT orqasidagi
    // sinf dars boshida o'zini o'zi bloklardi — bu hujum emas, oddiy ish
    // kuni. Hisob darajasidagi (email bo'yicha) bloklash — keyingi qadam.
    options.AddPolicy(AuthController.LoginRateLimitPolicy,
        context => FixedWindowByIp(context, authPermitLimit, authWindowSeconds));

    // Yangilash — boshqa tahdid modeli, kengroq budjet (izoh: AuthController).
    options.AddPolicy(AuthController.RefreshRateLimitPolicy,
        context => FixedWindowByIp(context, authRefreshPermitLimit, authWindowSeconds));
});

// IP bo'yicha qat'iy oyna (fixed window).
static RateLimitPartition<string> FixedWindowByIp(
    HttpContext context, int permitLimit, int windowSeconds) =>
    RateLimitPartition.GetFixedWindowLimiter(

        // DIQQAT: proksi orqasida bu proksining IP'si bo'ladi va hamma
        // bitta bo'limga tushadi. `X-Forwarded-For` ni to'g'ri hisobga
        // olish uchun `ForwardedHeaders` middleware kerak (ROADMAP);
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
