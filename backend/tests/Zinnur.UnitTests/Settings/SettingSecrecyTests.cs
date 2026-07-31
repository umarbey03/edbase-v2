using Zinnur.Application.Settings;

namespace Zinnur.UnitTests.Settings;

/// <summary>
/// ========================================================================
/// SIRLARNI HIMOYALASHNING IKKI QOIDASI
/// ========================================================================
///
///  1) sir HTTP javobiga TO'LIQ chiqmaydi (maskalanadi);
///  2) sir AUDIT jadvaliga umuman yozilmaydi.
///
/// ★ NIMA UCHUN IKKALASI HAM KERAK: faqat birinchisi bo'lsa, maskalash
/// ma'nosiz bo'lardi — sirni auditdan o'qib olish mumkin bo'lardi. Faqat
/// ikkinchisi bo'lsa, sir brauzer keshiga tushardi.
/// </summary>
public class SettingSecrecyTests
{
    private const string RealSecret = "123456789:AAH-super-maxfiy-bot-tokeni-3f2a";

    /// <summary>Maskalangan qiymat ASL sirni O'Z ICHIGA OLMAYDI.</summary>
    [Fact]
    public void Mask_NeverContainsTheSecret()
    {
        var masked = SettingMask.Mask(RealSecret)!;

        masked.Should().NotContain(RealSecret);

        // Boshidagi qism ham chiqmasin: xom HMAC kalitlarida "prefiks" degan
        // tushuncha yo'q va boshidan bir necha belgi ko'rsatish to'g'ridan-
        // to'g'ri kalit materialini oshkor qilish bo'lardi.
        masked.Should().NotContain("123456789");
        masked.Should().NotContain("AAH");

        // Oxirgi 4 belgi qoladi — admin "ha, men o'rnatgan kalit shu" deb
        // taniy olishi uchun.
        masked.Should().EndWith("3f2a");
    }

    /// <summary>
    /// Qisqa sir BUTUNLAY yashiriladi: 6 belgilik qiymatdan 4 tasini
    /// ko'rsatish deyarli hammasini ko'rsatish demakdir.
    /// </summary>
    [Fact]
    public void Mask_HidesShortSecretsCompletely()
    {
        var masked = SettingMask.Mask("qisqa")!;

        masked.Should().Be(SettingMask.Hidden);
        masked.Should().NotContain("qisqa");
    }

    /// <summary>Qiymat yo'q bo'lsa — <c>null</c>: panel "o'rnatilmagan" deydi.</summary>
    [Fact]
    public void Mask_ReturnsNullWhenNotSet()
    {
        SettingMask.Mask(null).Should().BeNull();
        SettingMask.Mask(string.Empty).Should().BeNull();
    }

    /// <summary>
    /// 🔴 ENG MUHIM TEST: sir sozlamaning eski va yangi qiymati auditga
    /// TUSHMAYDI. O'zgarish FAKTI esa yozilaveradi (izoh orqali).
    /// </summary>
    [Fact]
    public void SecretValues_AreNeverWrittenToAudit()
    {
        var definition = Require("security.jwt_secret");

        var values = SettingAuditPolicy.For(definition, "eski-sir-qiymati", RealSecret);

        values.OldValue.Should().BeNull();
        values.NewValue.Should().BeNull();
        values.Note.Should().Be(SettingAuditPolicy.SecretNote);
    }

    /// <summary>Sir bo'lmagan sozlama uchun qiymatlar odatdagidek yoziladi.</summary>
    [Fact]
    public void NonSecretValues_AreWrittenToAudit()
    {
        var definition = Require(SettingsRegistry.Keys.BlockThreshold);

        var values = SettingAuditPolicy.For(definition, "540000", "600000");

        values.OldValue.Should().Be("540000");
        values.NewValue.Should().Be("600000");
        values.Note.Should().BeNull();
    }

    /// <summary>
    /// Registrdagi HAR sir uchun qoida bir xil ishlashini tekshiramiz —
    /// yangi sir qo'shilganda uni ro'yxatga kiritishni unutish mumkin emas.
    /// </summary>
    [Fact]
    public void EverySecretInRegistry_IsExcludedFromAudit()
    {
        var secrets = SettingsRegistry.All.Where(d => d.IsSecret).ToList();

        secrets.Should().NotBeEmpty();

        foreach (var definition in secrets)
        {
            var values = SettingAuditPolicy.For(definition, "eski", "yangi");

            values.OldValue.Should().BeNull(definition.Key);
            values.NewValue.Should().BeNull(definition.Key);
        }
    }

    private static SettingDefinition Require(string key)
    {
        SettingsRegistry.TryGet(key, out var definition).Should().BeTrue();
        return definition;
    }
}
