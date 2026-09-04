using Microsoft.Extensions.Options;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Infrastructure.Options;

namespace Zinnur.IntegrationTests.Settings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// <c>RuntimeStorageOptions.Compose</c> — TIMEOUT MAYDONLARI TUSHIB
/// QOLMASLIGI (SPEC-RECORDING-V2, 5.9-3)
/// ════════════════════════════════════════════════════════════════════════
///
/// 🔴 NIMA UCHUN BU ALOHIDA TEST BO'LISHGA ARZIYDI
///
/// <c>Compose</c> baza kesimidan YANGI <c>StorageOptions</c> yasaydi. Ya'ni
/// u yerda ko'chirilmagan har qanday maydon jimgina KONSTRUKTORDAGI
/// standart qiymatiga qaytadi. <c>LargeUploadTimeoutSeconds</c> aynan
/// shunday tushib qolgan edi va nosozlik quyidagi shaklda ko'rinadi:
///
///   • sovuq startda (kesim hali o'qilmagan) hammasi TO'G'RI ishlaydi;
///   • birinchi kesim yuklanishi bilan qiymat standartga qaytadi.
///
/// Ya'ni bu "ba'zan ishlaydi" turkumidagi nosozlik — eng qimmati. Tungi
/// yig'uvchi uchun esa u halokatli: o'lchangan bitta dars 1.75 GB, 60
/// soniyalik chegara unga ~250 Mbit/s doimiy tezlik talab qilardi.
///
/// ⚠️ Test <c>RuntimeOptionsTests</c> ga qo'shilmadi, ALOHIDA fayl
/// yasaldi: <c>backend/tests</c> ni boshqa modul egallaydi va kelishuv —
/// "yangi fayl qo'sh, mavjudini tahrirlama".
/// </summary>
public class RuntimeStorageTimeoutTests
{
    /// <summary>
    /// Muhitdan (appsettings/env) kelgan BOSHLANG'ICH qiymatlar —
    /// ikkala timeout ham ATAYLAB standart bo'lmagan raqamda, aks holda
    /// "ko'chirildi" va "standartga qaytdi" holatlari bir xil ko'rinardi.
    /// </summary>
    private static readonly StorageOptions Seed = new()
    {
        ServiceUrl = "http://muhit:9000",
        Bucket = "muhit-bucket",
        AccessKey = "muhit-kalit",
        SecretKey = "muhit-sir",
        Region = "us-east-1",
        KeyPrefix = "muhit-prefiks",
        TimeoutSeconds = 42,
        LargeUploadTimeoutSeconds = 2400,
    };

    /// <summary>
    /// 🔴 ASOSIY TASDIQ: kesim yuklangandan KEYIN ham katta yuklash
    /// chegarasi muhitdagi qiymatda qoladi.
    ///
    /// Kesimda ombor kalitlari bor, ya'ni "baza o'qildi" holati AYNAN
    /// modellashtirilgan — nosozlik faqat shu holatda ko'rinardi.
    /// </summary>
    [Fact]
    public void LoadedSnapshot_KeepsBothTimeoutsFromTheEnvironmentSeed()
    {
        var runtime = new FakeRuntimeSettings(Snapshot(1, new()
        {
            [SettingsRegistry.Keys.StorageServiceUrl] = "http://baza:9000",
            [SettingsRegistry.Keys.StorageBucket] = "baza-bucket",
            [SettingsRegistry.Keys.StorageAccessKey] = "baza-kalit",
            [SettingsRegistry.Keys.StorageSecretKey] = "baza-sir",
        }));

        var current = new RuntimeStorageOptions(runtime, Options.Create(Seed)).Current;

        current.Bucket.Should().Be("baza-bucket", "kesim ustun — bu qoida o'zgarmadi");

        current.TimeoutSeconds.Should().Be(42);
        current.LargeUploadTimeoutSeconds.Should().Be(
            2400, "yig'uvchi 1-2 GB faylni shu chegara bilan yuklaydi");
    }

    /// <summary>
    /// Sovuq startda (kesim bo'sh) <c>Seed</c> ning O'ZI qaytariladi —
    /// ya'ni u yerda maydon tushib qolishi MUMKIN EMAS. Test shuning
    /// uchun bor: yiqilgan tasdiq "muammo `Compose` da" deb aniq
    /// ko'rsatsin, "qayerdadir" emas.
    /// </summary>
    [Fact]
    public void ColdStart_AlreadyCarriedTheLargeUploadTimeout()
    {
        var runtime = new FakeRuntimeSettings(SettingsSnapshot.Empty);

        var current = new RuntimeStorageOptions(runtime, Options.Create(Seed)).Current;

        current.Should().BeSameAs(Seed);
        current.LargeUploadTimeoutSeconds.Should().Be(2400);
    }

    // ================================================================= yordamchilar

    private static SettingsSnapshot Snapshot(long version, Dictionary<string, string?> values) =>
        new(new Dictionary<string, string?>(values, StringComparer.Ordinal), version);

    /// <summary>
    /// Soxta kesh: kesimni testdan boshqarish uchun. Redis ham, baza ham
    /// aralashmaydi — tekshiriladigan narsa faqat YIG'ISH qoidasi.
    /// </summary>
    private sealed class FakeRuntimeSettings(SettingsSnapshot current) : IRuntimeSettings
    {
        public SettingsSnapshot Current { get; set; } = current;

        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
