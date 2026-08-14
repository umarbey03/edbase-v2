namespace Zinnur.Application.Settings.Services;

/// <summary>
/// Sozlamaning HISOBLANGAN holati: ta'rif + amaldagi qiymat + qiymat qayerdan kelgani.
/// </summary>
/// <param name="Definition">Registrdagi metama'lumot.</param>
/// <param name="Value">
/// ⚠️ XOM va TO'LIQ qiymat — SIR bo'lsa ham. Bu ICHKI tur: uni to'g'ridan-to'g'ri
/// javobga solish TAQIQ. API'ga chiqarish faqat <c>SettingsService</c> orqali
/// bo'ladi va u sirlarni <see cref="SettingMask"/> bilan maskalaydi.
/// </param>
/// <param name="Origin">Qiymat amalda qayerdan keldi (baza / muhit / standart).</param>
/// <param name="UpdatedAt">Bazadagi qator qachon o'zgartirilgani (bo'lsa).</param>
/// <param name="UpdatedById">Kim o'zgartirgani (bo'lsa).</param>
public sealed record ResolvedSetting(
    SettingDefinition Definition,
    string? Value,
    SettingOrigin Origin,
    DateTimeOffset? UpdatedAt,
    long? UpdatedById)
{
    /// <summary>Qiymat umuman o'rnatilganmi (sir uchun panel shu bayroqni ko'rsatadi).</summary>
    public bool IsSet => !string.IsNullOrEmpty(Value);

    /// <summary>
    /// 🔴 Qiymat SHOSHILINCH muhit o'zgaruvchisi bilan ustidan yozilganmi
    /// (<c>SettingDefinition.OverrideConfigurationKey</c>).
    ///
    /// ★ NIMA UCHUN <see cref="Origin"/> DAN ALOHIDA BAYROQ KERAK:
    /// <c>Origin</c> — "qiymat qayerdan keldi" degan FAKT, bu esa
    /// "paneldan o'zgartirish endi TA'SIR QILMAYDI" degan XULOSA.
    /// Panel aynan shu xulosaga qarab maydonni qulflaydi
    /// (<c>SettingsService.ToDto</c>) — aks holda administrator qiymatni
    /// saqlab, "saqlandi" javobini olib, tizim esa eski qiymat bilan
    /// ishlayverardi. Registrdagi eng qattiq qoida shuni taqiqlaydi:
    /// jimgina yolg'on bo'lmasin.
    /// </summary>
    public bool IsOverridden { get; init; }
}

/// <summary>
/// Sozlamaning AMALDAGI qiymatini hisoblaydi (baza -&gt; muhit -&gt; standart).
///
/// ★ RUXSAT TEKSHIRUVI YO'Q — ataylab. Bu ICHKI o'qish yo'li: uni moliya
/// bloki (har so'rovda) va boshqa modullar chaqiradi, ular esa o'quvchi
/// nomidan ishlaydi. Rol tekshiruvi <c>ISettingsService</c> da —
/// ya'ni PANEL yo'lida.
/// </summary>
public interface ISettingsResolver
{
    /// <summary>Bitta sozlamaning amaldagi qiymati.</summary>
    Task<ResolvedSetting> ResolveAsync(SettingDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Bir nechta sozlama — BITTA baza so'rovi bilan. Blok tekshiruvi shu
    /// yo'lni ishlatadi: ikkita kalit uchun ikkita so'rov yuborish
    /// eng ko'p chaqiriladigan yo'lni ikki barobar qimmat qilardi.
    /// </summary>
    Task<IReadOnlyList<ResolvedSetting>> ResolveManyAsync(
        IReadOnlyCollection<SettingDefinition> definitions,
        CancellationToken ct = default);

    /// <summary>Registrdagi hamma sozlama (panel ro'yxati uchun).</summary>
    Task<IReadOnlyList<ResolvedSetting>> ResolveAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Bazadagi qator O'CHIRILSA qiymat NIMA bo'lishini hisoblaydi
    /// (muhit -&gt; registrdagi standart).
    ///
    /// ★ NIMA UCHUN KERAK: "standart qiymatga qaytarish" amali bog'langan
    /// to'plamni (masalan `Storage:*`) yarim sozlangan holatga tushirib
    /// qo'yishi mumkin. Buni SAQLASHDAN OLDIN bilish kerak, ya'ni "o'chirib
    /// ko'ramiz, keyin qaraymiz" yo'li mos kelmaydi. Hisob-kitob
    /// <c>SettingsResolver</c> ning O'ZIDA — ustunlik qoidasi nusxalanmasin.
    /// </summary>
    Task<ResolvedSetting> ResolveWithoutStoredAsync(
        SettingDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Kalit bo'yicha xom qiymat — boshqa modullar uchun qulay yo'l
    /// (masalan Telegram yuboruvchisi bot tokenini shu orqali oladi).
    /// Noma'lum kalit — <see cref="ArgumentException"/> (dasturchi xatosi).
    /// </summary>
    Task<string?> GetValueAsync(string key, CancellationToken ct = default);
}
