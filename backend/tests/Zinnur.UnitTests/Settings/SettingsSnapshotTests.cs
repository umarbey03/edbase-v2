using Zinnur.Application.Settings.Services;

namespace Zinnur.UnitTests.Settings;

/// <summary>
/// ========================================================================
/// SOZLAMALAR KESIMI — «BO'SH» va «YUKLANMAGAN» FARQI
/// ========================================================================
///
/// ★ NIMA UCHUN BU ALOHIDA TEST: ikkala holat ham <c>null</c> qaytaradi,
/// lekin MA'NOSI butunlay boshqa:
///
///   • YUKLANMAGAN — "hali bilmaymiz" (konteyner endi ko'tarildi, baza
///     o'qilmagan). Iste'molchi MUHIT qiymatiga qaytishi kerak.
///   • BO'SH — "bilamiz: sozlanmagan". Iste'molchi integratsiyani o'chirishi
///     kerak.
///
/// Ikkalasi aralashib ketsa, konteyner ko'tarilgan birinchi soniyalarda
/// fayl yuklash SABABSIZ 503 bo'lardi — va bu faqat deploy paytida,
/// takrorlash qiyin bo'lgan holatda chiqardi.
/// </summary>
public class SettingsSnapshotTests
{
    [Fact]
    public void Empty_IsNotLoaded()
    {
        SettingsSnapshot.Empty.IsLoaded.Should().BeFalse();
        SettingsSnapshot.Empty.Version.Should().Be(0);
        SettingsSnapshot.Empty.Value("storage.bucket").Should().BeNull();
    }

    /// <summary>
    /// Bazadan o'qilgan, LEKIN hech qanday qiymati bo'lmagan kesim —
    /// YUKLANGAN hisoblanadi. Aynan shu holat "integratsiya sozlanmagan"
    /// degani va u ishga tushish paytidagi noaniqlikdan farq qilishi shart.
    /// </summary>
    [Fact]
    public void LoadedButEmpty_IsStillLoaded()
    {
        var snapshot = new SettingsSnapshot(
            new Dictionary<string, string?>(StringComparer.Ordinal), version: 1);

        snapshot.IsLoaded.Should().BeTrue();
        snapshot.Value("storage.bucket").Should().BeNull();
    }

    [Fact]
    public void Value_ReturnsStoredValue()
    {
        var snapshot = new SettingsSnapshot(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["storage.bucket"] = "zinnur",
                ["storage.region"] = null,
            },
            version: 7);

        snapshot.Version.Should().Be(7);
        snapshot.Value("storage.bucket").Should().Be("zinnur");

        // Lug'atda BOR, lekin qiymati yo'q — "o'rnatilmagan" bilan bir xil.
        snapshot.Value("storage.region").Should().BeNull();

        // Lug'atda umuman yo'q kalit istisno TASHLAMAYDI: kesim registrga
        // qarab quriladi va yangi kalit qo'shilgan deploy paytida eski
        // kesim bilan yangi kod bir zumda birga yashaydi.
        SettingsSnapshot.Empty.Value("yangi.kalit").Should().BeNull();
    }

    [Fact]
    public void Constructor_RejectsNullValues()
    {
        var act = () => new SettingsSnapshot(null!, version: 1);

        act.Should().Throw<ArgumentNullException>();
    }
}
