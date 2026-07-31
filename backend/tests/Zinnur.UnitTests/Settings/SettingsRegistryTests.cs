using Zinnur.Application.Settings;

namespace Zinnur.UnitTests.Settings;

/// <summary>
/// ========================================================================
/// SOZLAMALAR REGISTRINING O'ZI (bazasiz, HTTP'siz)
/// ========================================================================
///
/// ★ NIMA UCHUN BU TESTLAR MUHIM: registr — kodda e'lon qilingan MA'LUMOT.
/// Undagi xato (chegara noto'g'ri, standart qiymat o'z qoidasidan o'tmaydi,
/// "faqat o'qish" sababi yozilmagan) kompilyatsiyada KO'RINMAYDI va faqat
/// admin panelni ochganda — ya'ni eng noqulay paytda — chiqadi.
/// </summary>
public class SettingsRegistryTests
{
    /// <summary>
    /// Registr o'z-o'ziga zid emas: kalit shakli, takrorlanmaslik, tanlov
    /// ro'yxati, "faqat o'qish" sababi va standart qiymatning haqiqiyligi.
    /// </summary>
    [Fact]
    public void Registry_IsInternallyConsistent()
    {
        var problems = SettingsRegistry.Validate();

        problems.Should().BeEmpty(string.Join(" | ", problems));
    }

    /// <summary>
    /// Registrda kamida bitta sozlama bor va guruhlar to'ldirilgan —
    /// "bo'sh registr" holatida yuqoridagi test ham yashil bo'lardi.
    /// </summary>
    [Fact]
    public void Registry_IsNotEmpty()
    {
        SettingsRegistry.All.Should().NotBeEmpty();
        SettingsRegistry.Groups.Should().NotBeEmpty();

        foreach (var group in SettingsRegistry.Groups)
            SettingsRegistry.GroupName(group).Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// 🔴 Tizimni QULFLAY yoki huquqni KENGAYTIRA oladigan kalitlar hech
    /// qachon bazadan boshqarilmasin.
    ///
    /// Bu test aynan REGRESSIYAdan himoya qiladi: kimdir "qulay bo'lsin" deb
    /// JWT kalitini tahrirlanadigan qilib qo'ysa, panelni egallagan odam
    /// istalgan foydalanuvchi nomidan token qalbakilashtira olardi.
    ///
    /// ⚠️ RO'YXATDAN NIMA CHIQDI VA NIMA UCHUN: `telegram.bot_token`,
    /// `storage.secret_key` va `livekit.api_secret` endi BAZADAN o'qiladi.
    /// Ular AYLANTIRIB (rotate) qutuladigan sirlar — sizib chiqsa paneldan
    /// bir daqiqada almashtiriladi va bu ularni serverga kirib almashtirishdan
    /// KO'RA XAVFSIZ. Ro'yxatdagi kalitlar esa boshqa turkum: ularni
    /// aylantirish muammoni hal qilmaydi, aksincha tizimni qulflaydi
    /// (izoh: `SettingsRegistry` va `ISettingsStore`).
    /// </summary>
    [Theory]
    [InlineData("security.jwt_secret")]
    [InlineData("security.jwt_issuer")]
    [InlineData("security.jwt_audience")]
    [InlineData("security.jwt_access_minutes")]
    [InlineData("security.jwt_refresh_days")]
    [InlineData("security.postgres_connection")]
    [InlineData("security.redis_connection")]
    [InlineData("security.sentry_dsn")]
    [InlineData("finance.enforce_block")]
    [InlineData("general.time_zone")]
    [InlineData("telegram.api_base_url")]
    [InlineData("livekit.url")]
    [InlineData("livekit.public_url")]
    [InlineData("storage.key_prefix")]
    public void LockoutCriticalKeys_AreReadOnly(string key)
    {
        SettingsRegistry.TryGet(key, out var definition).Should().BeTrue();

        definition.Source.Should().Be(SettingSource.Environment);
        definition.IsEditable.Should().BeFalse();

        // Panelda ko'rinadigan sabab MAJBURIY — "nega o'chirilgan?" degan
        // savol umuman tug'ilmasin.
        definition.ReadOnlyReason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// ★ TESKARI SHARTNOMA: aylantiriladigan kalitlar HAQIQATAN paneldan
    /// boshqariladi. Busiz yuqoridagi test "hammasini `Environment` qilib
    /// qo'yish" bilan ham yashil bo'lardi — ya'ni butun ishning ma'nosi
    /// jimgina yo'qolishi mumkin edi.
    ///
    /// 🔴 `isEditable` — FRONTEND BILAN SHARTNOMA: panel formani AYNAN shu
    /// bayroqqa qarab quradi.
    /// </summary>
    [Theory]
    [InlineData("storage.service_url")]
    [InlineData("storage.bucket")]
    [InlineData("storage.access_key")]
    [InlineData("storage.secret_key")]
    [InlineData("storage.region")]
    [InlineData("telegram.bot_token")]
    [InlineData("telegram.webhook_secret")]
    [InlineData("telegram.mini_app_url")]
    [InlineData("telegram.bot_username")]
    [InlineData("livekit.api_key")]
    [InlineData("livekit.api_secret")]
    [InlineData("finance.block_threshold")]
    [InlineData("finance.block_scope")]
    public void RotatableKeys_AreRuntimeEditable(string key)
    {
        SettingsRegistry.TryGet(key, out var definition).Should().BeTrue();

        definition.Source.Should().Be(SettingSource.Database);
        definition.IsEditable.Should().BeTrue();

        // Tahrirlanadigan kalitda "nega bo'lmaydi" sababi turishi mantiqsiz
        // bo'lardi — panel uni maydon yonida ko'rsatib, foydalanuvchini
        // chalg'itardi.
        definition.ReadOnlyReason.Should().BeNull();

        // Kesh AYNAN shu ro'yxatni o'qiydi (`IRuntimeSettings`).
        SettingsRegistry.Runtime.Should().Contain(definition);
    }

    /// <summary>
    /// Kesh ro'yxati registrga MOS: "faqat muhit" kalitlari unga UMUMAN
    /// tushmaydi. Aks holda JWT kaliti kabi sirlar butun ilova umri davomida
    /// yashaydigan singleton lug'atda yotardi — hech qanday foydasiz,
    /// ortiqcha oshkorlik bilan.
    /// </summary>
    [Fact]
    public void RuntimeList_ContainsOnlyDatabaseBackedKeys()
    {
        SettingsRegistry.Runtime.Should().NotBeEmpty();

        SettingsRegistry.Runtime.Should().OnlyContain(d => d.Source == SettingSource.Database);

        SettingsRegistry.Runtime.Should().BeEquivalentTo(
            SettingsRegistry.All.Where(d => d.IsEditable));
    }

    /// <summary>
    /// Moliya kalitlari ESKI TIZIM nomida saqlanadi — ko'chirish skripti
    /// eski `settings` jadvalidan qiymatni AYNAN shu nom bilan ko'chiradi.
    /// </summary>
    [Fact]
    public void FinanceKeys_KeepLegacyStorageNames()
    {
        SettingsRegistry.TryGet(SettingsRegistry.Keys.BlockThreshold, out var threshold)
            .Should().BeTrue();
        SettingsRegistry.TryGet(SettingsRegistry.Keys.BlockScope, out var scope)
            .Should().BeTrue();

        threshold.StorageKey.Should().Be("payment_block_threshold");
        scope.StorageKey.Should().Be("payment_block_scope");

        // Va ular paneldan o'zgartiriladi — bu moliya modulining butun ma'nosi.
        threshold.IsEditable.Should().BeTrue();
        scope.IsEditable.Should().BeTrue();
    }

    [Fact]
    public void UnknownKey_IsNotFound()
    {
        SettingsRegistry.TryGet("shunday.kalit.yoq", out _).Should().BeFalse();
        SettingsRegistry.TryGet(null, out _).Should().BeFalse();
        SettingsRegistry.TryGet("  ", out _).Should().BeFalse();
    }
}
