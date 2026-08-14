using Zinnur.Application.Jobs;
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

    // 🔴 2026-08-13 da `Environment` dan `Database` ga O'TKAZILDI (loyiha
    //    egasi: Cloudflare ulanish joylari paneldan boshqarilsin). Bu qator
    //    o'zgarishni QULFLAYDI: xato qiymatda ombor "jim 403" beradi va
    //    ilgari uni tuzatishning yagona yo'li qayta joylashtirish edi.
    [InlineData("storage.public_url")]

    [InlineData("telegram.bot_token")]
    [InlineData("telegram.webhook_secret")]
    [InlineData("telegram.mini_app_url")]
    [InlineData("telegram.bot_username")]
    [InlineData("livekit.api_key")]
    [InlineData("livekit.api_secret")]

    // 🔴 2026-08-14 da `Environment` dan `Database` ga O'TKAZILDI. Uni
    //    ushlab turgan YAGONA sabab `LiveKitHealthCheck` ning
    //    `IConfiguration` dan to'g'ridan-to'g'ri o'qishi edi (probe bir
    //    manzilga, token boshqasiga qarab qolardi); endi ikkalasi ham
    //    `IRuntimeOptions<LiveKitOptions>` dan oziqlanadi. Bu qator
    //    o'zgarishni QULFLAYDI: manba yana ajratilsa avval SHU test
    //    qizarishi kerak.
    //
    //    ★ `livekit.public_url` ATAYLAB yuqoridagi (faqat o'qish) ro'yxatda
    //      qoldi — u sertifikat/DNS bilan juftlashgan.
    [InlineData("livekit.url")]
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

    /// <summary>
    /// 🔴 CHAT TARIXINI TOZALASH — QAYTARIB BO'LMAYDIGAN YAGONA SOZLAMA.
    ///
    /// Uchta shart shu yerda QULFLANADI, chunki ularning har biri bitta
    /// belgi o'zgarishi bilan buziladi va oqibati — yo'qolgan yozishma:
    ///
    ///   1) STANDART — O'CHIQ. Yoqilgan holda yetkazilsa, yangilanish
    ///      chiqqan kuni birinchi yurish 3 oydan eski BUTUN yozishmani
    ///      o'chirardi — hech kim so'ramagan holda;
    ///   2) PASTKI CHEGARA >= 1 OY. `0` kesimni joriy onga tenglashtirib,
    ///      keyingi yurishda bugungi savollarni ham o'chirardi;
    ///   3) chegara vazifaning O'ZIDAGI to'siq bilan MOS — nusxa ataylab
    ///      (bazaga qo'lda yozilgan qiymat uchun ikkinchi himoya).
    /// </summary>
    [Fact]
    public void ChatRetention_IsOffByDefault_AndHasHardFloor()
    {
        SettingsRegistry.TryGet(SettingsRegistry.Keys.ChatRetentionEnabled, out var enabled)
            .Should().BeTrue();
        SettingsRegistry.TryGet(SettingsRegistry.Keys.ChatRetentionMonths, out var months)
            .Should().BeTrue();

        enabled.Kind.Should().Be(SettingValueKind.Toggle);
        enabled.DefaultValue.Should().Be(
            "false", "qaytarib bo'lmaydigan tozalash o'z-o'zidan yoqilmasin");

        months.Kind.Should().Be(SettingValueKind.Number);
        months.DefaultValue.Should().Be("3", "egasi talab qilgan standart — 3 oy");
        months.Minimum.Should().BeGreaterThanOrEqualTo(1m, "0 oy butun chatni yo'q qilardi");

        // Ikkalasi ham PANELDAN boshqariladi — butun ishning talabi shu.
        enabled.IsEditable.Should().BeTrue();
        months.IsEditable.Should().BeTrue();

        // Registr chegarasi va vazifadagi `Math.Clamp` bir-biriga MOS.
        months.Minimum.Should().Be(ChatRetentionJob.MinMonths);
        months.Maximum.Should().Be(ChatRetentionJob.MaxMonths);
    }

    [Fact]
    public void UnknownKey_IsNotFound()
    {
        SettingsRegistry.TryGet("shunday.kalit.yoq", out _).Should().BeFalse();
        SettingsRegistry.TryGet(null, out _).Should().BeFalse();
        SettingsRegistry.TryGet("  ", out _).Should().BeFalse();
    }
}
