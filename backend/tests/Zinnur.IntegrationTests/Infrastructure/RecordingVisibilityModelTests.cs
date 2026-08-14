using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.IntegrationTests.Infrastructure;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// EF MODELI — R5 KO'RINISH USTUNLARINING BAZA STANDARTI
/// ════════════════════════════════════════════════════════════════════════
///
/// 🔴 NIMA UCHUN BU TESTLAR ALOHIDA VA NIMA UCHUN ULAR BAZASIZ ISHLAYDI:
///
/// `DbContext` ni `UseNpgsql(...)` bilan qurish ULANISH OCHMAYDI — model
/// birinchi murojaatda XOTIRADA quriladi. Ya'ni bu testlar Postgres'siz
/// yuguradi va CI'da eng arzon tekshiruv bo'ladi.
///
/// Ular MIGRATSIYA QARORINI qulflaydi, xatti-harakatni emas:
///
///  1) `HasDefaultValue(true)` — MAVJUD qatorlar uchun. Usiz migratsiya
///     `NOT NULL` ustunni `false` bilan to'ldirardi va deploy kunida HAR
///     BIR o'quvchining "Dars yozuvlari" bo'limi bo'shab qolardi.
///     Bu — kod xatosi emas, MA'LUMOT xatosi: uni testsiz faqat
///     foydalanuvchi sezardi.
///
///  2) `HasSentinel(true)` — `UserConfiguration` dagi `IsActive` TUZOG'I.
///     EF qiymat sentinel'ga teng bo'lsa ustunni INSERT'dan tashlab
///     yuboradi. Standart sentinel — CLR default (`false`), ya'ni ATAYLAB
///     `false` qilib yaratilgan qator bazada `true` bo'lib qolardi.
///     Sentinel `true` bo'lgach mantiq to'g'rilanadi.
///
/// ⚠️ `HasSentinel` — EF 9 ning imkoniyati va u KOMPILYATSIYADA emas,
///    MODEL QURILISHIDA tekshiriladi. Ya'ni noto'g'ri sozlansa `build`
///    yashil bo'lib, ilova birinchi so'rovda yiqilardi — aynan shu sabab
///    tekshiruv testga chiqarildi.
/// </summary>
public sealed class RecordingVisibilityModelTests
{
    /// <summary>
    /// Ulanish HECH QACHON ochilmaydi — faqat provayder kerak (u ustun
    /// turlarini va standart qiymat sintaksisini biladi).
    /// </summary>
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=x;Password=x")
            .Options);

    [Theory]
    [InlineData(typeof(SessionRecording), nameof(SessionRecording.IsVisibleToStudents))]
    [InlineData(typeof(Group), nameof(Group.RecordingsVisibleToStudents))]
    public void VisibilityColumns_DefaultToTrue_SoExistingRowsKeepTodaysBehaviour(
        Type entity, string propertyName)
    {
        using var db = NewContext();

        var property = db.Model.FindEntityType(entity)!.FindProperty(propertyName)!;

        property.GetDefaultValue().Should().Be(
            true,
            "migratsiya MAVJUD qatorlarni `true` bilan to'ldirishi shart");

        property.Sentinel.Should().Be(
            true,
            "sentinel `false` bo'lsa, ATAYLAB yopilgan qator bazada ochiq bo'lib qolardi");
    }

    /// <summary>
    /// Bitta darsga BITTA tahlil — qoida bazaning O'ZIDA (R29).
    ///
    /// Servisdagi upsert ikkita bir vaqtda kelgan so'rovdan himoya
    /// qilmaydi: ikkala tranzaksiya ham "qator yo'q" deb ko'rib,
    /// ikkitasini qo'shardi va ro'yxatda ikkita nishon chiqardi.
    /// </summary>
    [Fact]
    public void SessionReview_HasAUniqueIndexOnSessionId()
    {
        using var db = NewContext();

        var index = db.Model
            .FindEntityType(typeof(SessionReview))!
            .GetIndexes()
            .Single(i => i.Properties.Count == 1
                      && i.Properties[0].Name == nameof(SessionReview.SessionId));

        index.IsUnique.Should().BeTrue();
    }
}
