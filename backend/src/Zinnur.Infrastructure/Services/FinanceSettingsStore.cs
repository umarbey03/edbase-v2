using System.Globalization;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Enums;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="IFinanceSettingsStore"/> port'ining amalga oshirilishi.
///
/// ★ NIMA O'ZGARDI (UMUMLASHTIRISH): ilgari bu sinf <c>AppSettings</c>
/// jadvaliga TO'G'RIDAN-TO'G'RI murojaat qilar, kalit nomlarini, standart
/// qiymatni va tahlil (parse) qoidasini O'ZIDA saqlar edi. Endi u UMUMIY
/// sozlamalar registrining ustida ishlaydi: kalitlar, standart qiymat,
/// chegara va tahlil qoidasi <see cref="SettingsRegistry"/> da BIR MARTA
/// e'lon qilingan.
///
/// NIMA UCHUN: aks holda platformada IKKI parallel sozlamalar tizimi
/// bo'lardi — biri moliya uchun, ikkinchisi qolgan hammasi uchun. Ular
/// muqarrar ravishda bir-biridan chetga chiqardi: masalan panelda chegara
/// yuqori chegarasi tekshirilar, moliya yo'lida esa tekshirilmasdi.
///
/// ★ SIRTQI XATTI-HARAKAT O'ZGARMADI (moliya testlari buni qo'riqlaydi):
///  • kalit nomlari o'sha-o'sha (<c>payment_block_threshold</c>,
///    <c>payment_block_scope</c>) — ko'chirish skripti uchun;
///  • chegara va qamrov BAZADAN, qattiq rejim KONFIGURATSIYADAN;
///  • buzuq qiymat ilovani yiqitmaydi, standartga qaytadi;
///  • <c>SaveChanges</c> bu yerda CHAQIRILMAYDI — chaqiruvchi
///    (<c>PaymentService</c>) sozlama va audit yozuvini BITTA
///    tranzaksiyada saqlaydi.
///
/// ★ QATTIQ REJIM NIMA UCHUN HAMON MUHITDAN: staging bazasi odatda prod
/// nusxasidan tiklanadi. Kalit bazada tursa prod'ning "qattiq rejim"
/// qiymati staging'ga ham ko'chib o'tardi va sinov foydalanuvchilari
/// bloklanib qolardi. Registrda u <see cref="SettingSource.Environment"/>
/// deb belgilangan, ya'ni panel uni faqat KO'RSATADI.
/// </summary>
public sealed class FinanceSettingsStore(ISettingsResolver resolver, ISettingsStore store)
    : IFinanceSettingsStore
{
    /// <summary>Eski tizim bilan bir xil kalit — ko'chirish skripti uchun.</summary>
    public const string ThresholdKey = SettingsRegistry.FinanceKeys.Threshold;

    /// <summary>Eski tizim bilan bir xil kalit.</summary>
    public const string ScopeKey = SettingsRegistry.FinanceKeys.Scope;

    public async Task<FinanceSettings> GetAsync(CancellationToken ct = default)
    {
        // Uchalasi BITTA baza so'rovi bilan: bu yo'l blok tekshiruvida, ya'ni
        // deyarli har so'rovda bajariladi.
        var resolved = await resolver
            .ResolveManyAsync([ThresholdSetting, ScopeSetting, EnforceSetting], ct)
            .ConfigureAwait(false);

        return new FinanceSettings(
            ReadThreshold(resolved[0]),
            ReadScope(resolved[1]),
            ReadEnforce(resolved[2]));
    }

    public async Task<FinanceSettings> SaveAsync(
        decimal blockThreshold,
        PaymentBlockScope blockScope,
        long? actorId,
        CancellationToken ct = default)
    {
        await store.SetAsync(
                ThresholdSetting.StorageKey,
                blockThreshold.ToString(CultureInfo.InvariantCulture),
                actorId,
                ct)
            .ConfigureAwait(false);

        // Qiymat ENUM NOMI sifatida yoziladi ("Video"), raqam sifatida emas:
        // bazani qo'lda ko'rgan odam nima yozilganini tushunishi kerak, va
        // enum raqamlari kelajakda ma'no o'zgartirsa qiymat jimgina boshqa
        // qamrovga aylanardi.
        await store.SetAsync(ScopeSetting.StorageKey, blockScope.ToString(), actorId, ct)
            .ConfigureAwait(false);

        // Qattiq rejim YOZILMAYDI — u muhitdan. Lekin javobda joriy qiymati
        // qaytariladi, aks holda chaqiruvchi uni alohida so'rashga majbur
        // bo'lardi.
        var enforce = await resolver.ResolveAsync(EnforceSetting, ct).ConfigureAwait(false);

        return new FinanceSettings(blockThreshold, blockScope, ReadEnforce(enforce));
    }

    /// <summary>
    /// Buzuq qiymat ilovani YIQITMAYDI — standartga qaytadi. Registr
    /// allaqachon manbalarni tartib bilan sinab chiqadi; bu esa oxirgi
    /// himoya qatlami (masalan registr standarti ham buzilgan bo'lsa).
    /// </summary>
    private static decimal ReadThreshold(ResolvedSetting resolved) =>
        SettingValueParser.TryReadDecimal(ThresholdSetting, resolved.Value, out var value)
            ? value
            : DefaultThreshold;

    /// <summary>Eski tizimdagi <c>"video"</c> kabi kichik harfli qiymat ham o'qiladi.</summary>
    private static PaymentBlockScope ReadScope(ResolvedSetting resolved) =>
        SettingValueParser.TryReadEnum<PaymentBlockScope>(resolved.Value, out var value)
            ? value
            : DefaultScope;

    /// <summary>
    /// Qiymat buzuq bo'lsa <c>true</c> — ya'ni QATTIQ rejim. Xavfsiz
    /// yo'nalish ataylab shu tomonda: buzuq satr tufayli bloklash jimgina
    /// o'chib qolsa, qarzdorlar cheksiz foydalanaverardi va buni hech kim
    /// payqamasdi.
    /// </summary>
    private static bool ReadEnforce(ResolvedSetting resolved) =>
        !SettingValueParser.TryReadBool(resolved.Value, out var value) || value;

    private static readonly SettingDefinition ThresholdSetting =
        Definition(SettingsRegistry.Keys.BlockThreshold);

    private static readonly SettingDefinition ScopeSetting =
        Definition(SettingsRegistry.Keys.BlockScope);

    private static readonly SettingDefinition EnforceSetting =
        Definition(SettingsRegistry.Keys.EnforceBlock);

    private static readonly decimal DefaultThreshold =
        decimal.Parse(ThresholdSetting.DefaultValue, CultureInfo.InvariantCulture);

    private static readonly PaymentBlockScope DefaultScope =
        Enum.Parse<PaymentBlockScope>(ScopeSetting.DefaultValue);

    private static SettingDefinition Definition(string key) =>
        SettingsRegistry.TryGet(key, out var definition)
            ? definition

            // Bu holat FAQAT registr buzilganda yuzaga keladi — ya'ni
            // dasturchi xatosi. Jimgina standartga qaytish xatoni yashirib,
            // bloklash sozlamasi ishlamayotganini oylab payqatmasdi.
            : throw new InvalidOperationException($"Registrda '{key}' sozlamasi yo'q.");
}
