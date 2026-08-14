using Microsoft.Extensions.Configuration;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.IntegrationTests.Bootstrap;

/// <summary>
/// ========================================================================
/// 🔴 YANGI O'RNATISHDA ADMINISTRATOR HAQIQATAN KIRA OLADIMI
/// ========================================================================
///
/// ★ NIMA UCHUN BU TESTLAR BOR
///
/// 2026-08-13 dan kirish faqat telefon orqali. `DbInitializer` esa
/// administratorni TELEFONSIZ yaratardi — ya'ni bo'sh bazaga qurilgan
/// har bir yangi deploy o'zini o'zi qulflab qo'yardi:
///   kirish uchun raqam kerak -> raqamni kiritish uchun kirish kerak.
///
/// Bu nosozlik faqat BIRINCHI kirish urinishida — odatda deploy'dan bir
/// necha soat keyin, ish vaqti tashqarisida — ma'lum bo'lardi. Shuning
/// uchun tekshiruv qattiq (istisno) va u shu yerda qulflanadi.
///
/// ⚠️ BAZA KERAK EMAS: `BootstrapAdmin` — sof konfiguratsiya o'qish va
/// tekshirish mantig'i. Test integratsiya loyihasida turibdi faqat
/// shuning uchunki `Zinnur.Infrastructure` ga havola AYNAN shu yerda bor
/// (unit loyihasi ataylab Domain + Application bilan cheklangan).
/// </summary>
public sealed class BootstrapAdminTests
{
    // ================================================================= telefon

    /// <summary>
    /// ★★ ASOSIY HIMOYA: prod'da raqamsiz seeding TO'XTAYDI.
    /// </summary>
    [Fact]
    public void EnsureUsable_WithoutPhone_ThrowsOutsideDevelopment()
    {
        var bootstrap = Read(isDevelopment: false);

        var act = bootstrap.EnsureUsable;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Bootstrap__AdminPhone*",
                "xato xabari AYNAN qaysi o'zgaruvchini qo'yishni ko'rsatishi kerak — "
                + "operator uni yarim tunda o'qiydi");
    }

    /// <summary>
    /// 🔴 RAQAMSIZ MATN ("-", "yo'q") BO'SH BILAN BIR XIL RAD ETILADI.
    ///
    /// Bu — eng ayyor holat: qiymat bo'sh emas, ya'ni oddiy
    /// "null yoki bo'shmi" tekshiruvi undan O'TKAZIB YUBORARDI.
    /// `User.SetPhone` esa uni raqamsiz deb `PhoneNormalized = null`
    /// qilib qo'yardi — natijada tekshiruvdan o'tgan, lekin baribir
    /// kira olmaydigan administrator. Aynan shu turdagi ma'lumot eski
    /// tizimdan ko'chirishda butun bir guruhda mavjud.
    /// </summary>
    [Theory]
    [InlineData("-")]
    [InlineData("yo'q")]
    [InlineData("   ")]
    public void EnsureUsable_WithNonNumericPhone_Throws(string phone)
    {
        var bootstrap = Read(isDevelopment: false, phone: phone);

        var act = bootstrap.EnsureUsable;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureUsable_WithValidPhone_Passes()
    {
        var bootstrap = Read(isDevelopment: false, phone: "+998901234567");

        var act = bootstrap.EnsureUsable;

        act.Should().NotThrow();
    }

    /// <summary>
    /// Xom, normalizatsiyalanmagan ko'rinish ham qabul qilinadi —
    /// tekshiruv `User.NormalizePhone` orqali, ya'ni bazaga yoziladigan
    /// qiymat bilan AYNI qoida bo'yicha.
    /// </summary>
    [Fact]
    public void EnsureUsable_AcceptsUnnormalizedPhone()
    {
        var bootstrap = Read(isDevelopment: false, phone: "90 123 45 67");

        var act = bootstrap.EnsureUsable;

        act.Should().NotThrow();
    }

    /// <summary>
    /// ★ DEV'DA STANDART QIYMAT BOR: `docker compose up` hech qanday
    ///   qo'shimcha sozlamasiz ishlashi kerak, aks holda har yangi
    ///   dasturchi birinchi kuni to'xtab qolardi.
    ///
    /// Prod'da esa standart ATAYLAB yo'q: "hammaga ma'lum raqam"
    /// administrator hisobini istalgan odamga ochib qo'yardi.
    /// </summary>
    [Fact]
    public void Read_InDevelopment_FallsBackToDefaultPhone()
    {
        var bootstrap = Read(isDevelopment: true);

        bootstrap.AdminPhone.Should().Be(DbInitializer.DevAdminPhone);

        var act = bootstrap.EnsureUsable;
        act.Should().NotThrow();
    }

    /// <summary>Muhitda qiymat bo'lsa dev'da ham U ustun turadi.</summary>
    [Fact]
    public void Read_InDevelopment_PrefersConfiguredPhone()
    {
        var bootstrap = Read(isDevelopment: true, phone: "+998911112233");

        bootstrap.AdminPhone.Should().Be("+998911112233");
    }

    // ================================================================= Telegram ID

    /// <summary>
    /// 🔴 BUZUQ TELEGRAM ID JIMGINA TASHLANMAYDI.
    ///
    /// "abc" yoki "0" yozgan operator ID qo'yganiga ISHONIB qolardi,
    /// tizim esa uni e'tiborsiz qoldirardi — ya'ni jimgina yolg'on.
    /// </summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public void EnsureUsable_WithBrokenTelegramId_Throws(string raw)
    {
        var bootstrap = Read(isDevelopment: true, telegramId: raw);

        var act = bootstrap.EnsureUsable;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*" + DbInitializer.AdminTelegramIdKey + "*");
    }

    /// <summary>Telegram ID IXTIYORIY — berilmasa hech narsa buzilmaydi.</summary>
    [Fact]
    public void Read_WithoutTelegramId_IsValid()
    {
        var bootstrap = Read(isDevelopment: true);

        bootstrap.AdminTelegramId.Should().BeNull();

        var act = bootstrap.EnsureUsable;
        act.Should().NotThrow();
    }

    [Fact]
    public void Read_WithValidTelegramId_ParsesIt()
    {
        var bootstrap = Read(isDevelopment: true, telegramId: "123456789");

        bootstrap.AdminTelegramId.Should().Be(123456789L);
    }

    // ================================================================= yordamchi

    private static BootstrapAdmin Read(
        bool isDevelopment, string? phone = null, string? telegramId = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (phone is not null)
            values[DbInitializer.AdminPhoneKey] = phone;

        if (telegramId is not null)
            values[DbInitializer.AdminTelegramIdKey] = telegramId;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return BootstrapAdmin.Read(configuration, isDevelopment);
    }
}
