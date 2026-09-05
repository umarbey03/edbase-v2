using Zinnur.Application.Recordings.Services;
using Zinnur.Infrastructure.Services;

namespace Zinnur.WebApi.Workers;

/// <summary>
/// Tungi yig'ish modulini DI'ga ulaydi (SPEC-RECORDING-V2 §4.2–4.5).
///
/// ★ NIMA UCHUN <c>Program.cs</c> DA EMAS — <c>JobsSetup</c> dagi AYNI
/// sabab: kompozitsiya ildizi allaqachon uzun va bu modul beshta
/// ro'yxatdan o'tkazishni talab qiladi. Bitta kengaytma metodi
/// <c>Program.cs</c> ga bitta qator qo'shadi.
///
/// ★ NIMA UCHUN INFRASTRUCTURE TURLARI SHU YERDA ULANADI, ya'ni
/// <c>AddInfrastructure</c> ichida emas: modul o'z bog'liqliklarini o'zi
/// ulaydi. <c>JobsSetup</c> Postgres advisory qulfini, notifikatsiya
/// moduli esa Redis tezlik cheklagichini AYNAN shunday ulaydi. Bu yerda
/// buning qo'shimcha foydasi ham bor — sozlamalar (<c>Composition:*</c>)
/// faqat shu yerda o'qiladi va Infrastructure ularni umuman bilmaydi.
///
/// ── 🔴 XIZMATLAR DOIM RO'YXATDAN O'TADI, WORKER — YO'Q ──────────────────
///
/// <c>Composition:Enabled</c> FAQAT fon siklini yoqadi. Sabab
/// <c>JobsSetup</c> dagi bilan bir xil: testlar aylanishni O'ZI
/// chaqiradi va fon xizmatining uyqusini kutmaydi. Bo'sh turgan
/// <c>IRecordingComposer</c> singleton'i hech narsa turmaydi — uni hech
/// kim so'ramasa u umuman yaratilmaydi.
///
/// ⚠️ VA AYNAN SHU BAYROQ IKKI KODLOVCHINING OLDINI OLADI (§4.2):
/// <c>api</c> konteyneri uni OSHKORA <c>false</c> qiladi,
/// <c>compositor</c> esa <c>true</c>. Standarti ham <c>false</c>, ya'ni
/// bayroqni unutish xavfsiz tomonga tushadi.
/// </summary>
internal static class CompositionSetup
{
    public static IServiceCollection AddZinnurComposition(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = CompositionOptions.Read(configuration);

        services.AddSingleton(options);

        // Sozlamalar konstruktorlarga QIYMAT sifatida uzatiladi:
        // Application va Infrastructure qatlamlari konfiguratsiya tizimini
        // bilmaydi (izoh: `RecordingWatchdogSettings`).
        services.AddSingleton(options.Composition);
        services.AddSingleton(options.Ffmpeg);

        // ffmpeg adapteri HOLATSIZ (butun holati — ishchi papka, u esa
        // har chaqiruvda yasaladi va o'chiriladi), shuning uchun
        // SINGLETON. Scoped bo'lsa hech narsa yutilmasdi.
        services.AddSingleton<IRecordingComposer, FfmpegRecordingComposer>();

        // ⚠️ SCOPED va bu MUHIM: navbat `ApplicationDbContext` ga
        //    tayanadi. Singleton bo'lsa scoped kontekst ushlab qolinardi
        //    ("captive dependency") va ikkinchi aylanishda allaqachon
        //    yopilgan kontekstga urinilardi.
        services.AddScoped<IRecordingCompositionStore, RecordingCompositionStore>();

        // Aylanishning O'ZI — Application qatlamida, ya'ni testda haqiqiy
        // baza bilan, fon xizmatisiz sinaladi (izoh:
        // `IRecordingCompositionRunner`).
        services.AddScoped<IRecordingCompositionRunner, RecordingCompositionRunner>();

        if (options.Enabled)
            services.AddHostedService<RecordingCompositionWorker>();

        return services;
    }
}
