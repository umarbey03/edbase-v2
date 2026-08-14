namespace Zinnur.Application.Settings.Services;

/// <summary>
/// <see cref="ISettingsResolver"/> amalga oshirilishi — USTUNLIK QOIDASI
/// aynan shu yerda, BITTA joyda yashaydi.
///
/// ★ TARTIB:
///   1) <see cref="SettingSource.Environment"/> kaliti bo'lsa — FAQAT
///      konfiguratsiya. Baza UMUMAN o'qilmaydi.
///   2) 🔴 <c>OverrideConfigurationKey</c> qo'yilgan VA muhitda qiymati
///      bor bo'lsa — O'SHA, bazadan ham USTUN ("break-glass").
///   3) aks holda: bazadagi qator -> konfiguratsiya -> registrdagi standart.
///
/// ★ NIMA UCHUN 1-BAND O'QISHDA TO'SILADI, YOZISHDA EMAS: yozishni to'sish
/// yetarli emas edi — bazaga to'g'ridan-to'g'ri kirgan odam (yoki eski
/// tizimdan ko'chirish skripti) qator qo'shib qo'ysa, u JIMGINA kuchga
/// kirardi. Bu yerda esa bunday qator umuman e'tiborga olinmaydi.
/// </summary>
public sealed class SettingsResolver(ISettingsStore store, ISettingsEnvironment environment)
    : ISettingsResolver
{
    public async Task<ResolvedSetting> ResolveAsync(
        SettingDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var resolved = await ResolveManyAsync([definition], ct).ConfigureAwait(false);
        return resolved[0];
    }

    public async Task<IReadOnlyList<ResolvedSetting>> ResolveManyAsync(
        IReadOnlyCollection<SettingDefinition> definitions, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        // Bazadan FAQAT kerak bo'lishi mumkin bo'lgan kalitlar so'raladi.
        // "Faqat muhit" kalitlari ro'yxatga umuman kirmaydi — ular uchun
        // qator bo'lsa ham o'qilmasligi kerak.
        var storageKeys = definitions
            .Where(d => d.Source == SettingSource.Database)
            .Select(d => d.StorageKey)
            .ToArray();

        var rows = storageKeys.Length > 0
            ? await store.LoadAsync(storageKeys, ct).ConfigureAwait(false)
            : EmptyRows;

        var result = new List<ResolvedSetting>(definitions.Count);

        foreach (var definition in definitions)
            result.Add(Resolve(definition, rows));

        return result;
    }

    public Task<IReadOnlyList<ResolvedSetting>> ResolveAllAsync(CancellationToken ct = default) =>
        ResolveManyAsync(SettingsRegistry.All, ct);

    public Task<ResolvedSetting> ResolveWithoutStoredAsync(
        SettingDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ct.ThrowIfCancellationRequested();

        // Bazaga UMUMAN murojaat qilinmaydi: savol aynan "qator bo'lmasa
        // nima bo'lardi?". Shu sababli metod `async` emas — kutish joyi yo'q,
        // lekin interfeys shakli qolgan metodlar bilan bir xil qoladi.
        return Task.FromResult(Resolve(definition, EmptyRows));
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken ct = default)
    {
        if (!SettingsRegistry.TryGet(key, out var definition))
        {
            // Noma'lum kalit — DASTURCHI xatosi (registrda yo'q nomga murojaat),
            // foydalanuvchi xatosi emas. Jimgina `null` qaytarish xato yozilgan
            // kalitni yashirib, "sozlama ishlamayapti" degan uzoq izlanishga
            // olib kelardi.
            throw new ArgumentException($"Registrda '{key}' sozlamasi yo'q.", nameof(key));
        }

        var resolved = await ResolveAsync(definition, ct).ConfigureAwait(false);
        return resolved.Value;
    }

    private ResolvedSetting Resolve(
        SettingDefinition definition, IReadOnlyDictionary<string, StoredSetting> rows)
    {
        var configured = definition.ConfigurationKey is { } configurationKey
            ? environment.Read(configurationKey)
            : null;

        // ── FAQAT MUHIT KALITLARI ─────────────────────────────────────────
        //
        // Qiymat XOM holda, tekshiruvsiz qaytariladi — ATAYLAB. Bu kalitni
        // tizim bizning registrimizdan emas, to'g'ridan-to'g'ri
        // konfiguratsiyadan o'qiydi (`IOptions`). Agar biz uni "qoidaga
        // to'g'ri kelmadi" deb standartga almashtirsak, panel tizim AMALDA
        // ishlatayotgan qiymatdan BOSHQA narsani ko'rsatardi — ya'ni jimgina
        // yolg'on aytardi. Panel esa aynan haqiqatni ko'rsatishi kerak.
        if (definition.Source == SettingSource.Environment)
        {
            var raw = !string.IsNullOrEmpty(configured) ? configured : Fallback(definition);
            var origin = !string.IsNullOrEmpty(configured)
                ? SettingOrigin.Environment
                : SettingOrigin.Default;

            return new ResolvedSetting(definition, raw, origin, null, null);
        }

        // ── 🔴 SHOSHILINCH USTIDAN YOZISH ("break-glass") ─────────────────
        //
        // Odatiy qoida (baza -> muhit -> standart) SHU YERDA, va FAQAT
        // registrda ataylab belgilangan kalitlar uchun, TESKARI aylanadi.
        //
        // ★ NIMA UCHUN BU YERDA, `IRuntimeOptions` DA EMAS: bu — USTUNLIK
        //   qoidasi, u esa loyihada BITTA joyda yashaydi (shu sinf izohi).
        //   Telegram sozlamalarini o'qiydigan uch joy bor
        //   (`RuntimeTelegramOptions`, `TelegramSetup`, sozlamalar paneli);
        //   qoida ulardan birida bo'lsa qolgan ikkitasi eski qiymatni
        //   ko'rardi va tiklash yarim ishlagan bo'lardi.
        //
        // ★ QIYMAT REGISTR QOIDASIDAN O'TKAZILMAYDI. Ataylab: bu kalit
        //   qo'yilgan payt — tizim allaqachon yiqilgan payt. Operator
        //   yozgan qiymat bizning format tekshiruvimizdan o'tmasa (masalan
        //   Telegram token shakli kelajakda o'zgarsa), uni JIMGINA rad
        //   etib bazadagi buzuq qiymatga qaytish oxirgi tiklash yo'lini
        //   ham yopib qo'yardi. Bu yerda operator — oxirgi instansiya.
        if (definition.OverrideConfigurationKey is { Length: > 0 } overrideKey)
        {
            var forced = environment.Read(overrideKey);

            if (!string.IsNullOrEmpty(forced))
            {
                return new ResolvedSetting(
                    definition, forced, SettingOrigin.EnvironmentOverride, null, null)
                {
                    IsOverridden = true,
                };
            }
        }

        // ── BAZA USTUN BO'LGAN KALITLAR ───────────────────────────────────
        //
        // Har manba qiymati registr qoidasidan O'TKAZILADI va o'tmasa
        // KEYINGI manbaga o'tiladi.
        //
        // ★ NIMA UCHUN: bu qator qo'lda ham tahrirlanadi va eski tizimdan
        // ham ko'chiriladi. "Chegara satri buzuq" degan holat butun
        // platformani ishdan chiqarmasligi kerak — xavfsiz yo'nalish
        // keyingi manba. Yon foyda: eski tizimdagi `"video"` qiymati
        // kanonik `"Video"` ga o'zi keltiriladi.
        if (rows.TryGetValue(definition.StorageKey, out var row)
            && SettingValueParser.TryNormalize(definition, row.Value, out var stored, out _))
        {
            return new ResolvedSetting(
                definition, stored, SettingOrigin.Database, row.UpdatedAt, row.UpdatedById);
        }

        if (SettingValueParser.TryNormalize(definition, configured, out var seed, out _)
            && !string.IsNullOrEmpty(configured))
        {
            return new ResolvedSetting(definition, seed, SettingOrigin.Environment, null, null);
        }

        return new ResolvedSetting(definition, Fallback(definition), SettingOrigin.Default, null, null);
    }

    /// <summary>
    /// Registrdagi standart. Bo'sh bo'lsa qiymat "o'rnatilmagan" hisoblanadi
    /// (<c>IsSet == false</c>) — panel buni aynan shunday ko'rsatadi va
    /// sirlar uchun bu normal holat.
    /// </summary>
    private static string? Fallback(SettingDefinition definition) =>
        definition.DefaultValue.Length > 0 ? definition.DefaultValue : null;

    private static readonly IReadOnlyDictionary<string, StoredSetting> EmptyRows =
        new Dictionary<string, StoredSetting>(StringComparer.Ordinal);
}
