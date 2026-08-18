using StackExchange.Redis;
using Zinnur.Application.Notifications.Services;
using Zinnur.Infrastructure.Services;

namespace Zinnur.WebApi.Workers;

/// <summary>
/// Notifikatsiya modulini DI'ga ulaydi (FAZA 5.2).
///
/// ★ NIMA UCHUN <c>Program.cs</c> DA EMAS: kompozitsiya ildizi allaqachon
/// uzun va bu modul beshta ro'yxatdan o'tkazishni talab qiladi. Bitta
/// kengaytma metodi (<c>builder.AddZinnurSentry()</c> bilan bir xil uslub)
/// <c>Program.cs</c> ga bitta qator qo'shadi.
/// </summary>
internal static class NotificationsSetup
{
    public static IServiceCollection AddZinnurNotifications(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = NotificationsOptions.Read(configuration);

        services.AddSingleton(options);

        // SCOPED — ikkalasi ham `ApplicationDbContext` ga tayanadi.
        //
        // ★ `INotificationOutbox` uchun bu SHART, qulaylik emas: yozuv
        // AYNI so'rovning kuzatuvchisiga tushishi kerak, aks holda biznes
        // o'zgarishi va xabar ikki xil tranzaksiyaga bo'linib,
        // commit-then-send kafolati buzilardi.
        services.AddScoped<INotificationOutbox, OutboxWriter>();
        services.AddScoped<IOutboxStore, OutboxStore>();

        // Navbat holatini KALIT bo'yicha o'qish (2026-08-18) — tor port,
        // sabab `IOutboxStatusReader` izohida. Kelmaganlarga yuborilgan
        // xabar haqiqatan yetkazildimi degan savolga javob beradi.
        services.AddScoped<IOutboxStatusReader, OutboxStatusReader>();
        services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();

        // Chegara SINGLETON va holati REDIS'da: ikki instance bitta
        // chelakdan ichadi, ya'ni Telegram'ning 30/s global chegarasi
        // instance soniga ko'paytirilmaydi.
        //
        // Kalit makoni `Redis:KeyPrefix` dan olinadi — bitta Redis'ni ikki
        // muhit baham ko'rsa hisoblagichlar aralashmasin (sabab:
        // `RedisCacheService`).
        services.AddSingleton<IMessageRateLimiter>(sp => new RedisMessageRateLimiter(
            sp.GetRequiredService<IConnectionMultiplexer>(),
            configuration["Redis:KeyPrefix"],
            options.RatePerSecond,
            options.RateBurst));

        // VAQTINCHALIK yuboruvchi: xabarni logga yozadi. FAZA 5.1 da
        // `TelegramMessageSender` qo'shiladi va ayni kanal uchun bo'lgani
        // sababli uning o'rnini oladi (izoh: `LoggingMessageSender`).
        services.AddSingleton<IMessageSender, LoggingMessageSender>();

        // Worker'ni O'CHIRIB QO'YISH mumkin (`Notifications:Enabled=false`):
        // navbatga yozish ishlayveradi, faqat yuborilmaydi. Testlar aynan
        // shu rejimda ishlaydi — aylanishni o'zi chaqirib, natijani darhol
        // tekshiradi (fon xizmatining uyqusini kutmaydi).
        if (options.Enabled)
            services.AddHostedService<OutboxWorker>();

        return services;
    }
}
