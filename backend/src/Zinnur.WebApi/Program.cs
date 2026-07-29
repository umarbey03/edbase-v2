using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Zinnur.Application;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Infrastructure;
using Zinnur.Infrastructure.Persistence;
using Zinnur.WebApi;
using Zinnur.WebApi.Hubs;
using Zinnur.WebApi.Middleware;
using Zinnur.WebApi.Services;

// ============================================================================
// ZIN-NUR API — kompozitsiya ildizi (composition root).
// Bu YAGONA joy barcha qatlamlarni bir-biriga ulaydi.
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- logging
// Serilog: strukturali log. Konteynerda stdout'ga chiqadi va Docker yig'adi.
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

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
        };

        // MUHIM: WebSocket qo'l berishi (handshake) Authorization header
        // yubora olmaydi. Shuning uchun SignalR tokeni query'da keladi.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                    context.Token = token;

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
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// ---------------------------------------------------------------- MVC + hujjat
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres sozlanmagan."),
        name: "postgres",
        tags: ["ready"])
    .AddRedis(
        redisConnection ?? "localhost:6379",
        name: "redis",
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
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,      // faqat jarayon tirikligi
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

// ---------------------------------------------------------------- migratsiya + seed
// Konteyner ko'tarilganda sxema qo'llanadi va birinchi admin yaratiladi.
await DbInitializer.InitializeAsync(app.Services);

ApiLog.ApiStarted(app.Logger, app.Environment.EnvironmentName);

await app.RunAsync();

/// <summary>Integratsiya testlari uchun ochiq (WebApplicationFactory).</summary>
public partial class Program;
