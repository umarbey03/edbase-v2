using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// MEDIA TESTLARI UCHUN UMUMIY FIXTURE (bitta API host, bitta baza)
/// ========================================================================
///
/// ★ NIMA UCHUN `IClassFixture` EMAS, `ICollectionFixture`
/// (nom `Fixture` bilan tugaydi: CA1711 tur nomi `Collection` bilan
/// tugashini taqiqlaydi, loyihada esa ogohlantirish = xato):
///
/// `IClassFixture` HAR TEST SINFI uchun ALOHIDA `WebApplicationFactory`
/// yasaydi — ya'ni alohida API host, alohida test bazasi, alohida Redis
/// obunasi va alohida fon xizmatlari to'plami. Uchta yangi sinf qo'shilishi
/// bilan bir vaqtda ishlayotgan host soni sezilarli oshdi va to'plam
/// beqaror bo'lib qoldi: xUnit sinflarni PARALLEL yuritadi, natijada
/// o'nlab host bir paytda ko'tarilib, keyin bir paytda to'xtatilardi.
///
/// Bitta kolleksiya fixture'i bilan:
///   • uchala sinf BITTA host va BITTA bazani bo'lishadi;
///   • sinflar KETMA-KET yuradi (xUnit kolleksiya ichida parallellik
///     qo'llamaydi) — bu sozlamani o'zgartiradigan testlar uchun ham
///     xavfsizroq (`lesson.video_max_mb` tekshiruvi);
///   • ko'tarish/yopish narxi uch baravar kamayadi.
///
/// ⚠️ OQIBATI: bu uchta sinf bir-birining ma'lumotini KO'RADI. Shuning
/// uchun har test o'z kursini/guruhini/vazifasini O'ZI yaratadi va
/// "jadvalda nechta qator bor" turidagi global tasdiqlar ISHLATILMAYDI.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LessonMediaFixture : ICollectionFixture<StorageBackedApiFactory>
{
    public const string Name = "lesson-media";
}
