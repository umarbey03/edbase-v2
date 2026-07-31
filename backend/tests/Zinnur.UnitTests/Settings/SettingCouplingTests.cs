using Zinnur.Application.Settings;

namespace Zinnur.UnitTests.Settings;

/// <summary>
/// ========================================================================
/// «TO'LIQ YOKI BO'SH» HIMOYASI — ENDI YOZISH PAYTIDA
/// ========================================================================
///
/// ★ NIMA UCHUN BU TESTLAR MUHIM: bu qoida ilgari <c>ValidateOnStart</c> da
/// yashardi va uni ISHGA TUSHIRISH orqali sinash mumkin edi ("ilova
/// ko'tarildimi?"). Endi u yozish yo'lida — ya'ni sinovsiz qolsa, birinchi
/// regressiyada JIMGINA yo'qolardi va buni faqat ishlab turgan ombor
/// paneldan o'chirilgan kuni bilardik.
///
/// Qoida ASSIMETRIK (sabab: <see cref="SettingCoupling"/>):
///   BO'SH  -> YARIM   RUXSAT  («qurish» bosqichi)
///   YARIM  -> YARIM   RUXSAT  (qurish davom etmoqda)
///   TO'LIQ -> YARIM   TAQIQ   (ishlab turgan integratsiyani buzish)
/// </summary>
public class SettingCouplingTests
{
    /// <summary>Ombor to'plami — to'rtta kalit birga ishlaydi.</summary>
    private static readonly SettingCouplingRule Storage =
        SettingCoupling.RuleFor(SettingsRegistry.Keys.StorageServiceUrl)!;

    /// <summary>Telegram to'plami — token va webhook siri.</summary>
    private static readonly SettingCouplingRule Telegram =
        SettingCoupling.RuleFor(SettingsRegistry.Keys.TelegramBotToken)!;

    // ================================================================= to'plamlar

    [Fact]
    public void Rules_CoverStorageAndTelegram()
    {
        Storage.Should().NotBeNull();
        Telegram.Should().NotBeNull();

        Storage.Keys.Should().Contain(SettingsRegistry.Keys.StorageSecretKey);
        Telegram.Keys.Should().Contain(SettingsRegistry.Keys.TelegramWebhookSecret);

        // Xato matnida foydalanuvchiga TUSHUNTIRISH bo'lishi shart —
        // "saqlanmadi" degan javob sababsiz qolmasin.
        Storage.Explanation.Should().NotBeNullOrWhiteSpace();
        Telegram.Explanation.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Kalitlarning ko'pchiligi hech qanday to'plamga kirmaydi.</summary>
    [Fact]
    public void RuleFor_UnrelatedKey_IsNull()
    {
        SettingCoupling.RuleFor(SettingsRegistry.Keys.BlockThreshold).Should().BeNull();
        SettingCoupling.RuleFor("shunday.kalit.yoq").Should().BeNull();
    }

    // ================================================================= holat

    [Fact]
    public void StateOf_EmptySet_IsEmpty()
    {
        SettingCoupling.StateOf(Telegram, Read()).Should().Be(SettingSetState.Empty);
    }

    /// <summary>Bo'sh joydan iborat qiymat ham "to'ldirilmagan" hisoblanadi.</summary>
    [Fact]
    public void StateOf_WhitespaceValue_CountsAsEmpty()
    {
        var values = Read((SettingsRegistry.Keys.TelegramBotToken, "   "));

        SettingCoupling.StateOf(Telegram, values).Should().Be(SettingSetState.Empty);
    }

    [Fact]
    public void StateOf_HalfFilledSet_IsPartial()
    {
        var values = Read((SettingsRegistry.Keys.TelegramBotToken, "123456:AAH-token"));

        SettingCoupling.StateOf(Telegram, values).Should().Be(SettingSetState.Partial);
    }

    [Fact]
    public void StateOf_FullSet_IsComplete()
    {
        SettingCoupling.StateOf(Telegram, FullTelegram()).Should().Be(SettingSetState.Complete);
    }

    // ================================================================= assimetriya

    /// <summary>
    /// 🔴 ASOSIY TAQIQ: ishlab turgan to'plamni yarim holatga tushirib
    /// bo'lmaydi. Aks holda fayl yuklash ERTASIGA, haqiqiy o'quvchi javob
    /// topshirayotganda ishlamay qolardi.
    /// </summary>
    [Fact]
    public void Breakage_CompleteToPartial_IsRejectedWithReason()
    {
        var before = FullStorage();
        var after = Read(
            (SettingsRegistry.Keys.StorageServiceUrl, "http://minio:9000"),
            (SettingsRegistry.Keys.StorageBucket, "zinnur"),
            (SettingsRegistry.Keys.StorageAccessKey, "kalit"));

        var breakage = SettingCoupling.Breakage(Storage, before, after);

        breakage.Should().NotBeNullOrWhiteSpace();

        // Sabab foydalanuvchiga KO'RSATILADI — to'plam nomi va tushuntirish
        // ichida bo'lsin, aks holda "nega saqlanmadi?" degan savol qoladi.
        breakage.Should().Contain(Storage.Name);
        breakage.Should().Contain(Storage.Explanation);
    }

    /// <summary>
    /// ★ «QURISH» RUXSAT ETILADI. Shartnoma bo'yicha har kalit ALOHIDA
    /// resurs (<c>PUT /api/v1/settings/{key}</c>) va bitta so'rovda to'rtta
    /// qiymatni birga yuborish imkoni YO'Q. Agar birinchi kalitni saqlash
    /// rad etilsa, omborni paneldan SOZLASH umuman mumkin bo'lmasdi.
    /// </summary>
    [Fact]
    public void Breakage_EmptyToPartial_IsAllowed()
    {
        var after = Read((SettingsRegistry.Keys.StorageServiceUrl, "http://minio:9000"));

        SettingCoupling.Breakage(Storage, Read(), after).Should().BeNull();
    }

    /// <summary>Qurish davom etmoqda — yarimdan yarimga o'tish ham ruxsat.</summary>
    [Fact]
    public void Breakage_PartialToPartial_IsAllowed()
    {
        var before = Read((SettingsRegistry.Keys.StorageServiceUrl, "http://minio:9000"));

        var after = Read(
            (SettingsRegistry.Keys.StorageServiceUrl, "http://minio:9000"),
            (SettingsRegistry.Keys.StorageBucket, "zinnur"));

        SettingCoupling.Breakage(Storage, before, after).Should().BeNull();
    }

    /// <summary>To'liqdan to'liqqa — bu oddiy tahrirlash, taqiq yo'q.</summary>
    [Fact]
    public void Breakage_CompleteToComplete_IsAllowed()
    {
        var after = Read(
            (SettingsRegistry.Keys.StorageServiceUrl, "http://minio:9000"),
            (SettingsRegistry.Keys.StorageBucket, "zinnur"),
            (SettingsRegistry.Keys.StorageAccessKey, "yangi-kalit"),
            (SettingsRegistry.Keys.StorageSecretKey, "yangi-sir"));

        SettingCoupling.Breakage(Storage, FullStorage(), after).Should().BeNull();
    }

    /// <summary>
    /// TO'LIQ -> BO'SH ham ruxsat: bu "integratsiyani o'chirish" degan ONGLI
    /// amal, yarim qolgan xatar emas (`SettingsService` buni butun to'plamni
    /// standartga qaytarish orqali bajaradi).
    /// </summary>
    [Fact]
    public void Breakage_CompleteToEmpty_IsAllowed()
    {
        SettingCoupling.Breakage(Storage, FullStorage(), Read()).Should().BeNull();
    }

    // ================================================================= yordamchilar

    private static Func<string, string?> Read(params (string Key, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);

        return key => map.GetValueOrDefault(key);
    }

    private static Func<string, string?> FullStorage() => Read(
        (SettingsRegistry.Keys.StorageServiceUrl, "http://minio:9000"),
        (SettingsRegistry.Keys.StorageBucket, "zinnur"),
        (SettingsRegistry.Keys.StorageAccessKey, "kalit"),
        (SettingsRegistry.Keys.StorageSecretKey, "sir"));

    private static Func<string, string?> FullTelegram() => Read(
        (SettingsRegistry.Keys.TelegramBotToken, "123456:AAH-token"),
        (SettingsRegistry.Keys.TelegramWebhookSecret, "webhook-siri"));
}
