using Microsoft.Extensions.Options;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.TeacherAvailability.Services;
using Zinnur.Application.Telegram.Services;
using Zinnur.Infrastructure.Options;
using Zinnur.Infrastructure.Services;

namespace Zinnur.WebApi.Telegram;

/// <summary>
/// Telegram modulini DI'ga ulaydi (FAZA 5.1).
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN ALOHIDA KENGAYTMA METODI (`AddApplication()` ichida emas)
///
/// Modul KONFIGURATSIYAGA bog'liq: bot tokeni bo'lmasa Telegram
/// yuboruvchisi UMUMAN ro'yxatdan o'tmaydi. `AddApplication()` esa
/// konfiguratsiyani ko'rmaydi (ataylab — u sof use-case ro'yxati).
/// Shuning uchun modul o'z ulagichiga ega — `AddZinnurNotifications` va
/// `AddZinnurSentry` bilan AYNI uslub.
///
/// ★ CHAQIRISH TARTIBI MUHIM — `AddZinnurNotifications()` DAN KEYIN.
///
/// `OutboxDispatcher` bir kanalga ikkita yuboruvchi bo'lsa OXIRGISINI
/// tanlaydi (bu uning izohida ataylab yozib qo'yilgan). Notifikatsiya
/// moduli vaqtinchalik `LoggingMessageSender` ni ro'yxatdan o'tkazadi;
/// bizniki undan KEYIN kelishi kerak, aks holda xabarlar Telegram'ga
/// emas, logga ketardi. Tartib buzilmasligini integratsiya testi
/// qo'riqlaydi (`TelegramSenderRegistrationTests`).
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class TelegramSetup
{
    public static IServiceCollection AddZinnurTelegram(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // SCOPED — ikkalasi ham `ApplicationDbContext` ga tayanadi va
        // yozuvlari AYNI kuzatuvchida to'planishi SHART (idempotentlik
        // izi + bog'lash + javob xabari bitta tranzaksiyada).
        services.AddScoped<ITelegramUpdateLog, TelegramUpdateLog>();
        services.AddScoped<ITelegramUpdateHandler, TelegramUpdateHandler>();

        // Ustoz kunlik tasdiqlash + o'rinbosar (2026-08-17) — `IApplicationDbContext`
        // ga tayanadi, shuning uchun SCOPED (yuqoridagi ikkitasi bilan bir xil sabab).
        services.AddScoped<ITeacherAvailabilityService, TeacherAvailabilityService>();

        // Mini App kirishi `IAuthService` (scoped) ga tayanadi.
        services.AddScoped<ITelegramMiniAppAuth, TelegramMiniAppAuth>();

        // Imzo tekshiruvchisi HOLATSIZ — Singleton.
        services.AddSingleton<ITelegramInitDataValidator, TelegramInitDataValidator>();

        // `answerCallbackQuery` yuboruvchisi — token HAR chaqiruvda qayta
        // o'qiladi (`TelegramMessageSender` dagi bilan AYNI sabab), shuning
        // uchun Singleton bo'lishi xavfsiz.
        services.AddSingleton<ITelegramCallbackAcknowledger, TelegramCallbackAcknowledger>();

        // Nomlangan HTTP klient: socket'larni qayta ishlatadi va DNS ni
        // vaqti-vaqti bilan yangilaydi (`static HttpClient` esa DNS
        // o'zgarganini hech qachon sezmaydi — `R2SubmissionStorage` izohi).
        services.AddHttpClient(TelegramMessageSender.HttpClientName, (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<TelegramOptions>>().Value;

                // Timeout MAJBURIY: Telegram javob bermay qolsa fon worker'i
                // butunlay osilib qolardi va NAVBAT TO'XTARDI.
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 3, 120));
            })

            // ══════════════════════════════════════════════════════════════
            // ★★ SIR SIZIB CHIQISHINI TO'SADI — JONLI SINOVDA TOPILGAN BUG.
            //
            // `IHttpClientFactory` ning O'Z log ilgagi har so'rovda TO'LIQ
            // manzilni Information darajasida yozadi:
            //
            //   "Start processing HTTP request POST
            //    https://api.telegram.org/bot123456789:AAH-.../sendMessage"
            //
            // Telegram Bot API tokenni AYNAN URL ichida talab qiladi
            // (sarlavhada yuborish imkoni yo'q), ya'ni bu qator BOT TOKENINI
            // konteyner logiga, log yig'gichga va Sentry'ga chiqarardi.
            // Prod'da ham chiqardi: `Start processing` — Information, bizning
            // eng past daraja esa aynan Information.
            //
            // `SentryScrubber` bu yerda YORDAM BERMAYDI: u so'rov
            // sarlavhalari va query'sini tozalaydi, log MATNINI emas.
            //
            // Shuning uchun bu klientning standart log ilgaklari BUTUNLAY
            // olib tashlanadi. Yo'qotadigan narsamiz yo'q: kerakli ma'lumot
            // (xabar id'si, holat kodi, Telegram tavsifi) `TelegramSendLog`
            // orqali baribir yoziladi — u yerda esa token yo'q va tashqi
            // matn `Redact` dan o'tadi.
            // ══════════════════════════════════════════════════════════════
            .RemoveAllLoggers();

        // ★ YUBORUVCHI FAQAT SOZLANGAN BO'LSA ro'yxatdan o'tadi.
        //
        // Sozlanmagan bo'lsa notifikatsiya modulining vaqtinchalik
        // `LoggingMessageSender` i O'RNIDA QOLADI — dev mashinasida navbat
        // oqimi uchdan-uchgacha ishlayveradi va har xabar "Telegram
        // sozlanmagan" bilan qizarib turmaydi.
        //
        // Qiymat SHU YERDA, ro'yxatga olish paytida o'qiladi (ishga tushish
        // vaqtidagi qaror), `NotificationsOptions.Read` bilan bir xil uslub.
        //
        // ══════════════════════════════════════════════════════════════════
        // ⚠️ MA'LUM VA ONGLI CHEKLOV — bu QAROR, unutilgan joy emas.
        //
        // Telegram qiymatlari endi paneldan boshqariladi va HAR chaqiruvda
        // qayta o'qiladi (`IRuntimeOptions<TelegramOptions>`). Ya'ni:
        //
        //   ✓ TOKENNI ALMASHTIRISH (asosiy talab — token o'g'irlanganda)
        //     darhol kuchga kiradi: yuboruvchi ro'yxatda turibdi va URL'ni
        //     har yuborishda yangi tokendan yasaydi;
        //   ✓ webhook siri va Mini App manzili ham darhol kuchga kiradi
        //     (controller va tekshiruvchi kesimdan o'qiydi);
        //
        //   ✗ LEKIN Telegram MUTLAQO sozlanmagan holatdan (muhitda ham,
        //     bazada ham token yo'q) paneldagi birinchi token bilan
        //     YUBORUVCHINI jonlantirish uchun API'ni QAYTA ISHGA TUSHIRISH
        //     kerak. Webhook va Mini App esa qayta ishga tushirishsiz ham
        //     ishlay boshlaydi.
        //
        // ★ NIMA UCHUN SHUNDAY QOLDIRILDI: `IMessageSender` ni HAR DOIM
        //   ro'yxatga qo'shish `OutboxDispatcher` ning "oxirgisi yutadi"
        //   qoidasi tufayli `LoggingMessageSender` ni butunlay siqib
        //   chiqarardi — dev mashinasida (token umuman yo'q) har eslatma
        //   "Telegram bot tokeni sozlanmagan" bilan qizarib, navbat oqimini
        //   uchdan-uchgacha sinash imkoni yo'qolardi. Bu ro'yxatga olish
        //   qoidasi, sozlama emas — uni to'g'ri hal qilish `OutboxDispatcher`
        //   tanlovini ish paytiga ko'chirishni talab qiladi (alohida ish).
        // ══════════════════════════════════════════════════════════════════
        var telegram = new TelegramOptions();
        configuration.GetSection(TelegramOptions.SectionName).Bind(telegram);

        if (telegram.IsConfigured)
            services.AddSingleton<IMessageSender, TelegramMessageSender>();

        return services;
    }
}
