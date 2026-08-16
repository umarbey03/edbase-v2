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
/// Ular MIGRATSIYA QARORINI qulflaydi, xatti-harakatni emas.
///
/// ⚠️ IKKI USTUN — IKKI XIL STANDART (2026-08-15 dan):
///
///  • <c>Group.RecordingsVisibleToStudents</c> — hamon <c>true</c>. Bu
///    GURUH darajasidagi kalit va uning ma'nosi o'zgarmagan: yangi guruh
///    yaratilganda uning yozuvlari (agar boshqa ikki kalit ham ruxsat
///    bersa) ko'rinadigan bo'lib boshlanadi — bu haqiqatan ham "eskicha
///    qoldirish" qarori (usiz migratsiya `NOT NULL` ustunni `false` bilan
///    to'ldirib, HAR BIR guruh yozuvi bo'limini bo'shatib qo'yardi).
///
///  • <c>SessionRecording.IsVisibleToStudents</c> — endi <c>false</c>. Bu
///    YOZUV darajasidagi kalit va loyiha egasi 2026-08-15 da buni ANIQ
///    teskarisiga o'zgartirishni so'radi: yangi yozuv o'quv bo'limi/ustoz
///    ochmaguncha ko'rinmasin. Sabab batafsil
///    <c>SessionRecording.IsVisibleToStudents</c> izohida. `HasDefaultValue`
///    baribir MAVJUD qatorlarga TEGMAYDI (faqat yangi INSERT'larga) —
///    shuning uchun bu qaror ham "MA'LUMOT xatosi" xavfidan xoli.
///
/// Ikkalasida ham `HasSentinel(...)` — `UserConfiguration` dagi `IsActive`
/// TUZOG'INI yopadi: EF qiymat sentinel'ga teng bo'lsa ustunni INSERT'dan
/// tashlab yuboradi, ya'ni sentinel HAR USTUNDA o'ZINING standart qiymati
/// bilan bir xil bo'lishi shart — aks holda ATAYLAB o'sha qiymat bilan
/// yaratilgan qator bazada TESKARISIGA yozilib qolardi.
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

    [Fact]
    public void GroupRecordingsVisibility_DefaultsToTrue_SoExistingGroupsKeepTodaysBehaviour()
    {
        AssertDefaultAndSentinel(typeof(Group), nameof(Group.RecordingsVisibleToStudents), true);
    }

    /// <summary>
    /// 2026-08-15 dan STANDART <c>false</c> — sabab
    /// <c>SessionRecording.IsVisibleToStudents</c> izohida.
    /// </summary>
    [Fact]
    public void SessionRecordingVisibility_DefaultsToFalse_SoNewRecordingsStayHiddenUntilShown()
    {
        AssertDefaultAndSentinel(typeof(SessionRecording), nameof(SessionRecording.IsVisibleToStudents), false);
    }

    private static void AssertDefaultAndSentinel(Type entity, string propertyName, bool expected)
    {
        using var db = NewContext();

        var property = db.Model.FindEntityType(entity)!.FindProperty(propertyName)!;

        property.GetDefaultValue().Should().Be(
            expected,
            $"baza ustun defaulti `{expected}` bo'lishi shart (izoh: sinf sarlavhasi)");

        property.Sentinel.Should().Be(
            expected,
            "sentinel standart qiymat bilan BIR XIL bo'lishi shart, aks holda ATAYLAB "
                + "shu qiymat bilan yaratilgan qator bazada teskarisiga yozilib qolardi");
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
