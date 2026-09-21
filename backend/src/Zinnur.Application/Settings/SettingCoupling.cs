namespace Zinnur.Application.Settings;

/// <summary>
/// Bog'langan to'plamning AMALDAGI holati.
/// </summary>
public enum SettingSetState
{
    /// <summary>Hech biri to'ldirilmagan — integratsiya ATAYLAB o'chiq.</summary>
    Empty = 0,

    /// <summary>Bir qismi to'ldirilgan — integratsiya ISHLAMAYDI.</summary>
    Partial = 1,

    /// <summary>Hammasi to'ldirilgan — integratsiya ishlaydi.</summary>
    Complete = 2,
}

/// <summary>
/// BIRGA ishlaydigan kalitlar to'plami.
/// </summary>
/// <param name="Name">Panelda va xato matnida ko'rinadigan nom.</param>
/// <param name="Keys">To'plam a'zolari (registrdagi ommaviy kalitlar).</param>
/// <param name="Explanation">
/// Xato matniga qo'shiladigan tushuntirish — foydalanuvchi "nega saqlanmadi?"
/// degan savol bilan qolmasin.
/// </param>
public sealed record SettingCouplingRule(
    string Name,
    IReadOnlyList<string> Keys,
    string Explanation);

/// <summary>
/// Tanlov kaliti ma'lum qiymatda bo'lganda to'plam TO'LIQ bo'lishi SHART.
/// </summary>
/// <param name="SelectorKey">Tanlov kaliti (masalan <c>livekit.provider</c>).</param>
/// <param name="ActiveValue">To'plamni ishga soladigan tanlov qiymati.</param>
/// <param name="Set">Shu qiymatda ishlatiladigan bog'langan to'plam.</param>
/// <param name="Explanation">Rad javobidagi tushuntirish.</param>
public sealed record SettingActivationRule(
    string SelectorKey,
    string ActiveValue,
    SettingCouplingRule Set,
    string Explanation);

/// <summary>
/// ========================================================================
/// «TO'LIQ YOKI BO'SH» HIMOYASI — ENDI YOZISH PAYTIDA
/// ========================================================================
///
/// ★ NIMA UCHUN KO'CHIRILDI. Ilgari bu qoida <c>ValidateOnStart</c> da
/// yashardi: yarim to'ldirilgan <c>Storage:*</c> yoki <c>Telegram:*</c> da
/// ilova UMUMAN ko'tarilmasdi. Qiymatlar bazadan kela boshlagach, o'sha
/// tekshiruv MA'NOSINI YO'QOTADI — ishga tushish paytida baza hali o'qilgan
/// ham bo'lmaydi. Uni shunchaki olib tashlash noto'g'ri bo'lardi: yarim
/// sozlangan holat baribir xatarli. Shuning uchun qoida YOZISH yo'liga
/// ko'chirildi.
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★★ QOIDA ASSIMETRIK — VA BU ATAYLAB. Uchta holat bor:
///
///   BO'SH  -> YARIM   RUXSAT.  «Qurish» bosqichi.
///   YARIM  -> YARIM   RUXSAT.  Qurish davom etmoqda.
///   TO'LIQ -> YARIM   TAQIQ.   Ishlab turgan integratsiyani buzish.
///
/// ★ NIMA UCHUN «qurish» ta'qiqlanmaydi: shartnoma bo'yicha har kalit
/// ALOHIDA resurs (<c>PUT /api/v1/settings/{key}</c>) va bitta so'rovda
/// to'rtta qiymatni birga yuborish imkoni YO'Q. Agar birinchi kalitni
/// saqlash "to'plam yarim qoladi" deb rad etilsa, omborni paneldan
/// SOZLASH UMUMAN MUMKIN BO'LMASDI — himoya funksiyani butunlay o'ldirardi.
///
/// ★ NIMA UCHUN «qurish» XAVFSIZ: yarim to'ldirilgan to'plam INERT.
/// <c>IsConfigured</c> BARCHA a'zoni talab qiladi, ya'ni:
///   • yarim <c>Storage:*</c>  -> fayl yuklash 503 (sozlanmagan bilan bir xil);
///   • yarim <c>Telegram:*</c> -> webhook 404, yuboruvchi ro'yxatdan o'tmagan.
/// <c>ValidateOnStart</c> qo'riqlagan ENG XAVFLI holat — «token bor, webhook
/// siri yo'q, ya'ni webhook OCHIQ» — bu kodda YUZAGA KELMAYDI: controller
/// endpointni to'plam TO'LIQ bo'lmaguncha 404 qiladi.
///
/// ★ NIMA UCHUN «buzish» TAQIQ: bu yagona holat, unda operator o'zi
/// bilmagan holda ISHLAYOTGAN integratsiyani o'chiradi. Fayl yuklash
/// ertasiga, haqiqiy o'quvchi javob topshirayotganda ishlamay qolardi.
/// ══════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN ALOHIDA SINF: bu qoida bazasiz, HTTP'siz test qilinadi va
/// yangi bog'langan to'plam qo'shilganda uni BITTA joyga yozish kifoya.
/// </summary>
public static class SettingCoupling
{
    /// <summary>Barcha bog'langan to'plamlar.</summary>
    /// <summary>
    /// LiveKit Cloud to'plami (2026-09-21). Alohida xususiyat, chunki
    /// <see cref="Activations"/> ham AYNI obyektga tayanadi.
    ///
    /// ⚠️ <see cref="Rules"/> DAN OLDIN turishi SHART: statik
    /// initsializatorlar matn tartibida bajariladi, keyin tursa
    /// <see cref="Rules"/> ichiga <c>null</c> tushardi.
    /// </summary>
    public static SettingCouplingRule LiveKitCloud { get; } = new(
        "LiveKit Cloud",
        [
            SettingsRegistry.Keys.LiveKitCloudUrl,
            SettingsRegistry.Keys.LiveKitCloudApiKey,
            SettingsRegistry.Keys.LiveKitCloudApiSecret,
        ],
        "Cloud manzili, API kaliti va siri BIRGA ishlaydi: bittasi yetishmasa "
        + "Cloud'ga ulanib bo'lmaydi.");

    public static IReadOnlyList<SettingCouplingRule> Rules { get; } =
    [
        new(
            "Ombor (fayllar)",
            [
                SettingsRegistry.Keys.StorageServiceUrl,
                SettingsRegistry.Keys.StorageBucket,
                SettingsRegistry.Keys.StorageAccessKey,
                SettingsRegistry.Keys.StorageSecretKey,
            ],
            "Manzil, bucket, kirish kaliti va maxfiy kalit BIRGA ishlaydi: "
            + "bittasi yetishmasa fayl yuklash butunlay o'chadi."),

        new(
            "Telegram",
            [
                SettingsRegistry.Keys.TelegramBotToken,
                SettingsRegistry.Keys.TelegramWebhookSecret,
            ],
            "Bot tokeni va webhook siri BIRGA ishlaydi: bittasi yetishsa "
            + "bot xabar ham yubormaydi, yangilanish ham qabul qilmaydi."),

        LiveKitCloud,
    ];

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// FAOLLASHTIRISH QOIDALARI — «tanlov X bo'lsa, to'plam TO'LIQ bo'lsin»
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// ★ NIMA UCHUN <see cref="Rules"/> YETARLI EMAS: u faqat «TO'LIQ -&gt;
    /// YARIM» ni to'sadi. Cloud to'plami esa BO'SH yoki YARIM holatda ham
    /// INERT emas, agar tanlov <c>Cloud</c> ga qo'yilsa — shu paytdan BARCHA
    /// jonli dars tokeni bo'sh kalit bilan imzolanib, hamma dars ochilmay
    /// qolardi. Shuning uchun ikki yo'nalish ham to'siladi:
    ///   • to'plam TO'LIQ bo'lmaguncha tanlovni <c>Cloud</c> ga qo'yish;
    ///   • tanlov <c>Cloud</c> turganda to'plamni YARIM/BO'SH qilish
    ///     (jumladan «standartga qaytarish» orqali).
    ///
    /// ★ ZAXIRA HAM BOR: baribir yarim holat yuzaga kelsa (masalan bazani
    /// qo'lda tahrirlash), <c>RuntimeLiveKitOptions</c> o'z serverimizga
    /// qaytadi — bu qoida esa uni panel orqali yuzaga keltirmaydi.
    /// </summary>
    public static IReadOnlyList<SettingActivationRule> Activations { get; } =
    [
        new(
            SettingsRegistry.Keys.LiveKitProvider,
            SettingsRegistry.LiveKitProviders.Cloud,
            LiveKitCloud,
            "«Qaysi server ishlatilsin» = Cloud faqat Cloud manzili, API kaliti va "
            + "siri TO'LIQ to'ldirilganda ishlaydi. Avval ularni to'ldiring; Cloud "
            + "sozlamalarini o'chirish uchun esa avval serverni SelfHosted ga qaytaring."),
    ];

    /// <summary>
    /// O'zgarishdan KEYINGI holat faollashtirish qoidasini buzadimi.
    /// Buzsa — foydalanuvchiga ko'rsatiladigan sabab, aks holda <c>null</c>.
    /// </summary>
    public static string? ActivationBreakage(SettingActivationRule rule, Func<string, string?> after)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(after);

        if (!string.Equals(after(rule.SelectorKey)?.Trim(), rule.ActiveValue, StringComparison.OrdinalIgnoreCase))
            return null;

        if (StateOf(rule.Set, after) == SettingSetState.Complete)
            return null;

        return rule.Explanation;
    }

    /// <summary>
    /// Kalit qaysi to'plamga tegishli. Hech qaysisiga tegishli bo'lmasa —
    /// <c>null</c> (kalitlarning ko'pchiligi shunday).
    /// </summary>
    public static SettingCouplingRule? RuleFor(string key)
    {
        foreach (var rule in Rules)
        {
            if (rule.Keys.Contains(key, StringComparer.Ordinal))
                return rule;
        }

        return null;
    }

    /// <summary>To'plamning berilgan qiymatlar bo'yicha holati.</summary>
    /// <param name="rule">To'plam.</param>
    /// <param name="read">Kalit -&gt; amaldagi qiymat (yo'q bo'lsa <c>null</c>).</param>
    public static SettingSetState StateOf(SettingCouplingRule rule, Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(read);

        var filled = 0;

        foreach (var key in rule.Keys)
        {
            if (!string.IsNullOrWhiteSpace(read(key)))
                filled++;
        }

        if (filled == 0)
            return SettingSetState.Empty;

        return filled == rule.Keys.Count ? SettingSetState.Complete : SettingSetState.Partial;
    }

    /// <summary>
    /// O'zgarish ISHLAYOTGAN to'plamni buzadimi (TO'LIQ -&gt; YARIM).
    /// </summary>
    /// <returns>
    /// Buzsa — foydalanuvchiga ko'rsatiladigan o'zbekcha sabab; buzmasa <c>null</c>.
    /// </returns>
    public static string? Breakage(
        SettingCouplingRule rule,
        Func<string, string?> before,
        Func<string, string?> after)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (StateOf(rule, before) != SettingSetState.Complete)
            return null;

        if (StateOf(rule, after) != SettingSetState.Partial)
            return null;

        return $"«{rule.Name}» to'plami hozir TO'LIQ sozlangan va ishlab turibdi. "
               + "Bu o'zgarish uni yarim sozlangan holatga tushirardi — ya'ni integratsiya "
               + "jimgina o'chib qolardi. " + rule.Explanation;
    }
}
