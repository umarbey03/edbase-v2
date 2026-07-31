using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// ========================================================================
/// <see cref="IRuntimeSettings"/> AMALGA OSHIRILISHI — SOZLAMALAR KESHI
/// ========================================================================
///
/// ★ NIMA UCHUN KESH UMUMAN KERAK: bu qiymatlarni fayl yuklashning HAR
/// bosqichi, har webhook so'rovi va har token berish yo'li o'qiydi. Ularning
/// bir qismi SINXRON xossalar (<c>ISubmissionStorage.IsConfigured</c>) —
/// ya'ni "har chaqiruvda `SELECT`" varianti texnik jihatdan ham mumkin emas.
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★★ KESH YANGILANISHI — UCHTA MUSTAQIL YO'L (ataylab ortiqcha)
///
/// Kesh bilan bog'liq ASOSIY XAVF — "yangilanish ko'rinmay qolishi": admin
/// qiymatni o'zgartiradi, tizim esa eskisi bilan ishlayveradi. Bu aynan biz
/// tuzatayotgan muammoning boshqa shakli, shuning uchun bitta mexanizmga
/// tayanilmadi:
///
///   1) SAQLAGAN INSTANSIYADA — KECHIKISH 0 s.
///      <c>SettingsService</c> <c>SaveChanges</c> dan KEYIN va HTTP javob
///      qaytishidan OLDIN <see cref="RefreshAsync"/> ni KUTIB chaqiradi.
///      Ya'ni panel "saqlandi" deganda tizim allaqachon yangi qiymatda.
///
///   2) BOSHQA INSTANSIYALARDA — Redis pub/sub, odatda bir soniyagacha.
///      <see cref="RefreshAsync"/> kanalga xabar tashlaydi, qolganlar esa
///      keshni qayta o'qiydi.
///
///   3) KAFOLATLANGAN CHEGARA — <see cref="RefreshInterval"/> = 10 SEKUND.
///      Fon sikli keshni shu oraliqda baribir qayta o'qiydi.
///
/// ★ NIMA UCHUN FAQAT (3) YETARLI EMAS: paneldan qiymatni o'zgartirib,
/// DARHOL sinab ko'rgan operator uchun 10 sekundlik kechikish "ishlamadi"
/// degan xulosaga olib kelardi.
///
/// ★ NIMA UCHUN FAQAT (2) YETARLI EMAS: Redis pub/sub xabari
/// KAFOLATLANMAGAN — yetkazilmasa qayta yuborilmaydi, endi ko'tarilgan
/// instansiya esa o'tgan xabarlarni umuman ko'rmaydi. (3) — shu teshikni
/// yopadigan orqa tayanch.
///
/// 🔴 ENG YOMON HOLATDAGI KECHIKISH: 10 SEKUND (Redis butunlay yiqilgan
/// bo'lsa ham). Bu son <see cref="RefreshInterval"/> da, bitta joyda.
/// ══════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN <c>IHostedService</c> HAM: ilova ishga tushganda kesh BIR
/// MARTA to'ldirilishi kerak — aks holda birinchi so'rovlar muhitdagi
/// (eski) qiymat bilan ishlardi. <c>StartAsync</c> migratsiyalardan KEYIN,
/// server so'rov qabul qilishdan OLDIN chaqiriladi
/// (<c>Program.cs</c>: <c>DbInitializer</c> -&gt; <c>app.RunAsync()</c>).
/// </summary>
public sealed class RuntimeSettings : IRuntimeSettings, IHostedService, IDisposable
{
    /// <summary>
    /// Fon sikli keshni qayta o'qish oralig'i — YA'NI ENG YOMON HOLATDAGI
    /// YANGILANISH KECHIKISHI. O'zgartirilsa shartnoma o'zgaradi:
    /// <c>IRuntimeSettings</c> izohidagi "10 sekund" ham yangilansin.
    /// </summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Redis kanali nomining oxirgi bo'lagi. Oldiga kalit MAKONI qo'yiladi
    /// (<c>Redis:KeyPrefix</c>) — bitta Redis'ni dev/staging va integratsiya
    /// testlari baham ko'rganda ular bir-birining keshini qayta o'qishga
    /// majbur qilmasin.
    /// </summary>
    public const string ChannelSuffix = ":settings:changed";

    private readonly IServiceScopeFactory _scopes;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RuntimeSettings> _logger;
    private readonly RedisChannel _channel;

    /// <summary>
    /// Shu instansiyaning belgisi. O'z xabarini qayta ishlamaslik uchun:
    /// saqlagan instansiya keshni ALLAQACHON yangilagan, ikkinchi o'qish
    /// esa faqat ortiqcha so'rov bo'lardi.
    /// </summary>
    private readonly RedisValue _instanceId = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Qayta o'qishlarni KETMA-KET qiladi. ★ NIMA UCHUN: ikkita parallel
    /// o'qish tugash tartibi bo'yicha bir-birini bosib ketishi mumkin edi —
    /// ya'ni ESKI kesim YANGISINING ustiga yozilib, o'zgarish "yo'qolardi".
    /// </summary>
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    private readonly CancellationTokenSource _stopping = new();

    private SettingsSnapshot _current = SettingsSnapshot.Empty;
    private long _version;
    private int _subscribed;
    private Task? _loop;

    public RuntimeSettings(
        IServiceScopeFactory scopes,
        IConnectionMultiplexer redis,
        ILogger<RuntimeSettings> logger,
        string? keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(logger);

        _scopes = scopes;
        _redis = redis;
        _logger = logger;

        var prefix = string.IsNullOrWhiteSpace(keyPrefix) ? RedisCacheService.DefaultPrefix : keyPrefix;
        _channel = RedisChannel.Literal(prefix + ChannelSuffix);
    }

    /// <inheritdoc />
    public SettingsSnapshot Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // TARTIB MUHIM: avval O'ZIMIZ o'qiymiz, keyin boshqalarga aytamiz.
        // Teskarisi bo'lsa, saqlagan instansiya HTTP javobni hali eski
        // qiymat bilan qaytarishi mumkin edi.
        await ReloadAsync(ct).ConfigureAwait(false);
        await AnnounceAsync().ConfigureAwait(false);
    }

    // ================================================================= hosted service

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureSubscribedAsync().ConfigureAwait(false);

        // BOSHLANG'ICH YUKLASH. Xato bo'lsa ilova baribir ko'tariladi:
        // kesim "yuklanmagan" holatda qoladi va iste'molchilar MUHIT
        // qiymatiga qaytadi (izoh: `SettingsSnapshot.Empty`). Bazadagi
        // qiymat esa fon siklining birinchi urinishida kuchga kiradi —
        // ya'ni baza kech ko'tarilgani butun API'ni yiqitmaydi.
        await ReloadAsync(cancellationToken).ConfigureAwait(false);

        _loop = LoopAsync(_stopping.Token);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);

        if (_loop is { } loop)
        {
            // Sikl faqat `OperationCanceledException` bilan tugaydi — uni
            // yutamiz, boshqa har qanday xato esa ko'rinsin.
            try
            {
                await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // To'xtatish — normal yakun.
            }
        }
    }

    /// <summary>
    /// Resurslar SHU YERDA yopiladi, <see cref="StopAsync"/> da emas.
    ///
    /// ★ NIMA UCHUN: <c>StopAsync</c> dan keyin ham DI konteyneri
    /// singletonlarni bir muddat ushlab turadi va Redis ilgagi
    /// (<see cref="OnChanged"/>) kechikkan xabar bilan bir marta chaqirilishi
    /// mumkin. Semaforni o'sha yerda yopish "yopilgan obyektga murojaat"
    /// xatosini to'g'ridan-to'g'ri to'xtatish yo'liga olib kirardi.
    /// </summary>
    public void Dispose()
    {
        _stopping.Dispose();
        _reloadGate.Dispose();
    }

    // ================================================================= ichki

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                // Obuna ishga tushishda yiqilgan bo'lsa (Redis kech ko'tarildi)
                // shu yerda qayta urinamiz — aks holda instansiya butun umri
                // davomida faqat 10 sekundlik orqa tayanchga qolardi.
                await EnsureSubscribedAsync().ConfigureAwait(false);
                await ReloadAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Ilova to'xtatilmoqda — normal yakun.
        }
    }

    /// <summary>
    /// Kesimni bazadan qayta o'qiydi. XATO TASHLAMAYDI — sabab
    /// <see cref="StartAsync"/> izohida: sozlamalar keshining vaqtinchalik
    /// eskirishi butun so'rovni (yoki butun ilovani) yiqitishdan yaxshi.
    /// Eng yomon holatda qiymat <see cref="RefreshInterval"/> ichida yetadi.
    /// </summary>
    private async Task ReloadAsync(CancellationToken ct)
    {
        try
        {
            await _reloadGate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        try
        {
            // ★ YANGI SCOPE: `ISettingsResolver` va uning ortidagi
            // `DbContext` — SCOPED xizmatlar, bu sinf esa SINGLETON.
            // Ularni konstruktorda ushlab qolish "captive dependency" bo'lardi:
            // bitta `DbContext` butun ilova umri davomida yashab, xotirani
            // to'ldirardi va oqimlar orasida bo'lishilib buzilardi.
            using var scope = _scopes.CreateScope();

            var resolver = scope.ServiceProvider.GetRequiredService<ISettingsResolver>();
            var resolved = await resolver
                .ResolveManyAsync(SettingsRegistry.Runtime, ct)
                .ConfigureAwait(false);

            var values = new Dictionary<string, string?>(resolved.Count, StringComparer.Ordinal);

            foreach (var item in resolved)
                values[item.Definition.Key] = item.Value;

            // Raqam MONOTON o'sadi — iste'molchilar tayyorlangan obyektni
            // aynan shu raqam bo'yicha keshlaydi (`RuntimeOptions<T>`).
            var version = Interlocked.Increment(ref _version);

            Volatile.Write(ref _current, new SettingsSnapshot(values, version));
        }
        catch (OperationCanceledException)
        {
            // So'rov bekor qilindi yoki ilova to'xtatilmoqda.
        }
        catch (Exception ex)
        {
            // 🔴 QIYMAT LOGGA YOZILMAYDI — kesim ichida sirlar bor.
            SettingsRuntimeLog.ReloadFailed(_logger, ex);
        }
        finally
        {
            try
            {
                _reloadGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Ilova to'xtatilayotganda semafor allaqachon yopilgan bo'lishi mumkin.
            }
        }
    }

    /// <summary>Boshqa instansiyalarga "keshni yangilanglar" deb aytadi.</summary>
    private async Task AnnounceAsync()
    {
        try
        {
            await _redis.GetSubscriber()
                .PublishAsync(_channel, _instanceId)
                .ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            // Redis yiqilgan bo'lsa ham SAQLASH muvaffaqiyatli bo'lgan va
            // SHU instansiya yangi qiymatni allaqachon ko'radi. Boshqalari
            // 10 sekundlik orqa tayanch orqali yetib oladi.
            SettingsRuntimeLog.AnnounceFailed(_logger, ex);
        }
    }

    private async Task EnsureSubscribedAsync()
    {
        if (Volatile.Read(ref _subscribed) == 1)
            return;

        try
        {
            await _redis.GetSubscriber()
                .SubscribeAsync(_channel, OnChanged)
                .ConfigureAwait(false);

            Volatile.Write(ref _subscribed, 1);
        }
        catch (RedisException ex)
        {
            // Redis hali ko'tarilmagan. Sikl keyingi urinishda qaytadi.
            SettingsRuntimeLog.SubscribeFailed(_logger, ex);
        }
    }

    /// <summary>
    /// Redis xabari. ⚠️ Redis OQIMIDA ishlaydi — bu yerda BLOKLASH MUMKIN
    /// EMAS, shuning uchun qayta o'qish fon vazifasiga uzatiladi
    /// (<c>ReloadAsync</c> o'zi hech qanday xato tashlamaydi).
    /// </summary>
    private void OnChanged(RedisChannel channel, RedisValue message)
    {
        if (message == _instanceId)
            return;

        _ = ReloadAsync(_stopping.Token);
    }
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848).
///
/// 🔴 SOZLAMA QIYMATI HECH QACHON YOZILMAYDI: kesim ichida bot tokeni va
/// ombor kalitlari bor, log esa Sentry'ga va konteyner chiqishiga ketadi.
/// </summary>
internal static partial class SettingsRuntimeLog
{
    [LoggerMessage(
        EventId = 6100,
        Level = LogLevel.Warning,
        Message = "Sozlamalar keshini qayta o'qib bo'lmadi. Eski qiymatlar bilan davom etilmoqda.")]
    internal static partial void ReloadFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 6101,
        Level = LogLevel.Warning,
        Message = "Sozlama o'zgarishi haqida xabar yuborilmadi (Redis). "
                  + "Boshqa instansiyalar keshni fon sikli orqali yangilaydi.")]
    internal static partial void AnnounceFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 6102,
        Level = LogLevel.Warning,
        Message = "Sozlamalar kanaliga obuna bo'lib bo'lmadi (Redis). Keyingi urinish fon siklida.")]
    internal static partial void SubscribeFailed(ILogger logger, Exception exception);
}
