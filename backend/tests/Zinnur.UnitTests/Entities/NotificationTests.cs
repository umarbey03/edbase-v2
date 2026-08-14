using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// Qo'ng'iroqcha yozuvining qoidalari.
///
/// ★ NIMA UCHUN MUHIM: bu entity BAHOLASH tranzaksiyasi ichida yasaladi.
/// Uning konstruktoridan chiqqan HAR istisno USTOZNING BAHOSINI saqlanmay
/// qoldiradi — ya'ni "uzun izoh" kabi zararsiz narsa biznes amalini
/// yiqitardi. Shuning uchun bu yerdagi testlar asosan "nima istisno
/// TASHLAMAYDI" ni qulflaydi.
/// </summary>
public class NotificationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- yasash

    [Fact]
    public void Create_SetsUnreadAndCreatedAt()
    {
        var row = Notification.Create(
            42, NotificationKind.SubmissionGraded, "Vazifa tekshirildi", "5/5 ball.", 7, Now);

        row.UserId.Should().Be(42);
        row.Kind.Should().Be(NotificationKind.SubmissionGraded);
        row.EntityId.Should().Be(7);
        row.CreatedAt.Should().Be(Now);
        row.ReadAt.Should().BeNull("yangi bildirishnoma DOIM o'qilmagan bo'ladi");
    }

    /// <summary>
    /// ★ UZUN IZOH ISTISNO TASHLAMAYDI, QIRQILADI.
    ///
    /// Ustozning izohi 2000 belgigacha bo'lishi mumkin
    /// (<c>Submission.MaxFeedbackLength</c>), qo'ng'iroqcha qatori esa
    /// 1000. Rad etilsa BAHOLASH endpointi 500 qaytarardi — ya'ni ustoz
    /// batafsil izoh yozgani uchun bahosi saqlanmasdi.
    /// </summary>
    [Fact]
    public void Create_WithOverlongBody_TruncatesInsteadOfThrowing()
    {
        var row = Notification.Create(
            1,
            NotificationKind.SubmissionGraded,
            "Sarlavha",
            new string('a', Notification.MaxBodyLength + 500),
            null,
            Now);

        row.Body.Should().HaveLength(Notification.MaxBodyLength);
    }

    [Fact]
    public void Create_WithOverlongTitle_Truncates()
    {
        var row = Notification.Create(
            1,
            NotificationKind.SubmissionGraded,
            new string('b', Notification.MaxTitleLength + 50),
            "tana",
            null,
            Now);

        row.Title.Should().HaveLength(Notification.MaxTitleLength);
    }

    /// <summary>
    /// ★ EMOJI O'RTASIDAN KESILMAYDI: yolg'iz surrogat Postgres'ga
    /// yozilganda buziladi (<c>MessageText</c> dagi AYNI sabab).
    /// </summary>
    [Fact]
    public void Create_TruncatingAtSurrogatePair_KeepsTextValid()
    {
        // 999 ta oddiy belgi + emoji => chegara AYNAN juftlik o'rtasiga tushadi.
        var body = new string('x', Notification.MaxBodyLength - 1) + "😀";

        var row = Notification.Create(
            1, NotificationKind.SubmissionGraded, "S", body, null, Now);

        row.Body.Should().HaveLength(Notification.MaxBodyLength - 1);
        char.IsHighSurrogate(row.Body[^1]).Should().BeFalse("yolg'iz surrogat qolmasligi kerak");
    }

    [Fact]
    public void Create_WithEmptyTitle_Throws() =>
        FluentActions.Invoking(() => Notification.Create(
                1, NotificationKind.SubmissionGraded, "   ", "tana", null, Now))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_WithoutRecipient_Throws() =>
        FluentActions.Invoking(() => Notification.Create(
                0, NotificationKind.SubmissionGraded, "Sarlavha", "tana", null, Now))
            .Should().Throw<DomainException>();

    /// <summary>Izoh bo'lmasa tana bo'sh bo'lishi MUMKIN — bu xato emas.</summary>
    [Fact]
    public void Create_WithEmptyBody_IsAllowed() =>
        Notification.Create(1, NotificationKind.SubmissionGraded, "Sarlavha", "", null, Now)
            .Body.Should().BeEmpty();

    // ---------------------------------------------------------------- o'qildi

    [Fact]
    public void MarkRead_FirstTime_SetsReadAt()
    {
        var row = Notification.Create(
            1, NotificationKind.SubmissionGraded, "S", "b", null, Now);

        row.MarkRead(Now).Should().BeTrue();
        row.ReadAt.Should().Be(Now);
    }

    /// <summary>
    /// ★ IDEMPOTENT VA VAQT QAYTA YOZILMAYDI.
    ///
    /// Klient ro'yxatni ochganda bir necha so'rov parallel ketishi mumkin
    /// (ekran ochildi + hub hodisasi keldi). Ikkinchisi <c>ReadAt</c> ni
    /// qayta yozsa, "qachon ko'rdi" javobi HAR ochilishda yangilanib, o'z
    /// ma'nosini yo'qotardi.
    /// </summary>
    [Fact]
    public void MarkRead_SecondTime_IsNoOpAndKeepsFirstTimestamp()
    {
        var row = Notification.Create(
            1, NotificationKind.SubmissionGraded, "S", "b", null, Now);

        row.MarkRead(Now).Should().BeTrue();
        row.MarkRead(Now.AddHours(3)).Should().BeFalse();

        row.ReadAt.Should().Be(Now, "birinchi o'qish vaqti saqlanib qolishi kerak");
    }
}
