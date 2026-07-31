namespace Zinnur.Application.Settings.Services;

/// <summary>
/// ========================================================================
/// SOZLAMALARNING KESHLANGAN KESIMI — O'ZGARMAS (immutable)
/// ========================================================================
///
/// ★ NIMA UCHUN LUG'AT EMAS, ALOHIDA TUR: iste'molchi kesimni BIR MARTA
/// oladi va butun chaqiruv davomida AYNI kesim bilan ishlaydi. Bu muhim:
/// S3 imzosi <c>AccessKey</c> va <c>SecretKey</c> ni birga oladi, orada
/// kesh yangilansa esa imzo bir kalit bilan, sarlavha boshqasi bilan
/// chiqib, so'rov 403 bo'lardi — va sabab hech qayerda ko'rinmasdi.
///
/// ★ <see cref="Version"/> — kesim RAQAMI. Iste'molchi shu raqam bo'yicha
/// o'zining tayyorlangan (composed) obyektini keshlaydi: har chaqiruvda
/// yangi <c>StorageOptions</c> yasash keraksiz ish bo'lardi.
/// </summary>
public sealed class SettingsSnapshot
{
    /// <summary>
    /// Hali BIR MARTA ham yuklanmagan kesim.
    ///
    /// ★ NIMA UCHUN "bo'sh" va "yuklanmagan" AJRATILGAN: bo'sh qiymat
    /// ("sozlanmagan") ham, yuklanmagan holat ("hali bilmaymiz") ham
    /// <c>null</c> qaytaradi, lekin ma'nosi butunlay boshqa. Sovuq startda
    /// (baza hali o'qilmagan) iste'molchi MUHIT qiymatiga qaytishi kerak,
    /// "sozlanmagan" degan xulosaga emas — aks holda konteyner ko'tarilgan
    /// birinchi soniyalarda fayl yuklash sababsiz 503 bo'lardi.
    /// </summary>
    public static SettingsSnapshot Empty { get; } =
        new(new Dictionary<string, string?>(StringComparer.Ordinal), version: 0);

    private readonly IReadOnlyDictionary<string, string?> _values;

    public SettingsSnapshot(IReadOnlyDictionary<string, string?> values, long version)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
        Version = version;
    }

    /// <summary>Kesim raqami. <c>0</c> — hali yuklanmagan.</summary>
    public long Version { get; }

    /// <summary>Kesim bazadan kamida bir marta o'qilganmi.</summary>
    public bool IsLoaded => Version > 0;

    /// <summary>
    /// Kalitning AMALDAGI qiymati (baza -&gt; muhit -&gt; registrdagi standart).
    /// Kalit kesimda yo'q yoki hech qayerda o'rnatilmagan bo'lsa — <c>null</c>.
    /// </summary>
    public string? Value(string key) =>
        _values.TryGetValue(key, out var value) ? value : null;
}

/// <summary>
/// ========================================================================
/// ISH JARAYONIDA O'QILADIGAN SOZLAMALAR — PORT
/// ========================================================================
///
/// ★ NIMA UCHUN BU UMUMAN KERAK. <c>IOptions&lt;T&gt;</c> qiymatni ilova
/// ISHGA TUSHGANDA bir marta o'qiydi va singleton xizmatga qotirib qo'yadi.
/// <c>IOptionsMonitor&lt;T&gt;</c> ham yordam bermaydi: u KONFIGURATSIYA
/// manbaini kuzatadi, bizning manbamiz esa BAZA — uni <c>IConfiguration</c>
/// umuman ko'rmaydi. Natijada panel "saqlandi" derdi-yu, tizim eski qiymat
/// bilan ishlayverardi — eng yomon turdagi xato: JIMGINA YOLG'ON.
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★★ KESH VA YANGILANISH KECHIKISHI — ANIQ SHARTNOMA
///
/// O'qish yo'li SINXRON va BAZAGA BORMAYDI. Sabab: uni fayl yuklash,
/// webhook va token berish yo'llari chaqiradi, ularning ba'zilari esa
/// sinxron xossalar (<c>ISubmissionStorage.IsConfigured</c>) — ya'ni
/// "har chaqiruvda `SELECT`" varianti umuman mumkin emas.
///
/// Kesim quyidagi hollarda yangilanadi:
///
///   1) SAQLAGAN INSTANSIYADA — DARHOL (kechikish 0 s).
///      <c>SettingsService</c> <c>SaveChanges</c> dan keyin
///      <see cref="RefreshAsync"/> ni KUTIB chaqiradi, ya'ni HTTP javob
///      qaytganda tizim allaqachon yangi qiymat bilan ishlaydi.
///
///   2) BOSHQA INSTANSIYALARDA — Redis pub/sub xabari orqali, odatda
///      bir soniyagacha.
///
///   3) KAFOLATLANGAN CHEGARA — 10 SEKUND. Fon yangilovchisi kesimni shu
///      oraliqda qayta o'qiydi. Ya'ni Redis xabari yo'qolsa ham (tarmoq
///      uzildi, instansiya endi ko'tarildi) eski qiymat 10 sekunddan
///      ortiq ishlatilmaydi.
///
/// ★ NIMA UCHUN FAQAT TTL YETARLI EMAS EDI: 10 sekund kutish paneldan
/// qiymatni o'zgartirib, DARHOL sinab ko'rgan operator uchun "ishlamadi"
/// degan xulosaga olib kelardi — ya'ni tuzatilayotgan muammoning aynan
/// o'zi, boshqa shaklda qaytardi.
///
/// ★ NIMA UCHUN FAQAT PUB/SUB HAM YETARLI EMAS: Redis xabari
/// KAFOLATLANMAGAN (yetkazilmasa qayta yuborilmaydi) va yangi ko'tarilgan
/// instansiya o'tgan xabarlarni umuman ko'rmaydi. TTL — shu teshikni
/// yopadigan ORQA TAYANCH.
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IRuntimeSettings
{
    /// <summary>Joriy kesim. Sinxron va arzon — bazaga bormaydi.</summary>
    SettingsSnapshot Current { get; }

    /// <summary>
    /// Kesimni bazadan QAYTA O'QIYDI va boshqa instansiyalarga ham
    /// "yangilanglar" deb xabar beradi.
    ///
    /// Chaqiruvchi: <c>SettingsService</c>, sozlama saqlangandan KEYIN.
    /// Metod tugaganda SHU instansiya allaqachon yangi qiymatni ko'radi.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);
}
