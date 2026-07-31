using Microsoft.Extensions.Options;
using Zinnur.Application.Settings.Services;

namespace Zinnur.Infrastructure.Options;

/// <summary>
/// ========================================================================
/// <c>IOptions&lt;T&gt;</c> NING ISH JARAYONIDA O'QILADIGAN O'RINBOSARI
/// ========================================================================
///
/// ★ NIMA UCHUN <c>IOptions&lt;T&gt;</c> YARAMAYDI: u qiymatni ilova ishga
/// tushganda BIR MARTA o'qiydi va singleton xizmatga qotirib qo'yadi.
/// <c>IOptionsMonitor&lt;T&gt;</c> ham yaramaydi: u KONFIGURATSIYA manbaini
/// kuzatadi, bizning manbamiz esa BAZA — uni <c>IConfiguration</c> umuman
/// ko'rmaydi.
///
/// ★ NIMA UCHUN O'SHA <c>TOptions</c> SINFLARI QAYTA ISHLATILADI (yangi
/// "runtime" modeli yasalmadi): <c>StorageOptions.IsConfigured</c>,
/// <c>TelegramOptions.HasValidBotToken</c>, <c>LiveKitOptions.EffectivePublicUrl</c>
/// kabi QOIDALAR o'sha sinflarda yashaydi va ular testlar bilan qoplangan.
/// Ikkinchi model yasalsa, qoidalar nusxalanib, ikki nusxa asta-sekin
/// bir-biridan chetga chiqardi.
/// </summary>
/// <typeparam name="TOptions">Sozlamalar modeli (<c>StorageOptions</c> va h.k.).</typeparam>
public interface IRuntimeOptions<out TOptions>
    where TOptions : class
{
    /// <summary>
    /// AMALDAGI qiymatlar. Har chaqiruvda YANGI kesim bo'yicha hisoblanadi,
    /// lekin kesim o'zgarmagan bo'lsa AYNI obyekt qaytadi.
    ///
    /// ⚠️ CHAQIRUVCHIGA TALAB: bitta mantiqiy amal davomida obyektni BIR
    /// MARTA oling va shu obyekt bilan ishlang. Aks holda amal o'rtasida
    /// kesh yangilanib, masalan S3 imzosi bir kalit bilan, `Authorization`
    /// sarlavhasi boshqasi bilan chiqib qolardi.
    /// </summary>
    TOptions Current { get; }
}

/// <summary>
/// <see cref="IRuntimeOptions{TOptions}"/> uchun umumiy asos: kesimni
/// o'qiydi, sovuq startni hal qiladi va tayyorlangan obyektni keshlaydi.
///
/// ★ NIMA UCHUN KESHLASH KERAK: <c>IsConfigured</c> fayl yuklashning HAR
/// bosqichida so'raladi. Har safar yangi <c>StorageOptions</c> yasash
/// keraksiz allokatsiya bo'lardi. Kesim raqami (<c>Version</c>) o'zgarmasa
/// — obyekt ham o'zgarmaydi.
/// </summary>
public abstract class RuntimeOptions<TOptions> : IRuntimeOptions<TOptions>
    where TOptions : class
{
    private readonly IRuntimeSettings _runtime;
    private Composed? _composed;

    protected RuntimeOptions(IRuntimeSettings runtime, IOptions<TOptions> seed)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(seed);

        _runtime = runtime;
        Seed = seed.Value;
    }

    /// <summary>
    /// Muhit/appsettings dan bog'langan BOSHLANG'ICH qiymatlar.
    ///
    /// Ikki holatda kerak: (a) sovuq startda, kesim hali o'qilmaganda;
    /// (b) registrda UMUMAN yo'q maydonlar uchun (masalan
    /// <c>TimeoutSeconds</c>) — ular baribir faqat konfiguratsiyadan keladi.
    /// </summary>
    protected TOptions Seed { get; }

    /// <inheritdoc />
    public TOptions Current
    {
        get
        {
            var snapshot = _runtime.Current;

            // SOVUQ START: baza hali bir marta ham o'qilmagan. Muhitdagi
            // qiymat — aynan to'g'ri javob: registrda ham u BOSHLANG'ICH
            // manba sifatida turadi. "Sozlanmagan" deb xulosa qilish esa
            // konteyner ko'tarilgan birinchi soniyalarda fayl yuklashni
            // sababsiz 503 qilardi.
            if (!snapshot.IsLoaded)
                return Seed;

            var cached = Volatile.Read(ref _composed);

            if (cached is not null && cached.Version == snapshot.Version)
                return cached.Options;

            var options = Compose(snapshot);

            // Poyga bo'lsa ham zarari yo'q: ikki oqim AYNI kesimdan AYNI
            // natijani yasaydi, ya'ni qaysi biri yozgani ahamiyatsiz.
            Volatile.Write(ref _composed, new Composed(snapshot.Version, options));

            return options;
        }
    }

    /// <summary>Kesimdan yangi sozlamalar obyektini yig'adi.</summary>
    protected abstract TOptions Compose(SettingsSnapshot snapshot);

    private sealed record Composed(long Version, TOptions Options);
}
