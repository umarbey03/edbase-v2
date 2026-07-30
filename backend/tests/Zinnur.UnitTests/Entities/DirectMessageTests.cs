using Zinnur.Domain.Entities;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// Kurator ↔ o'quvchi yozishmasining Domain qoidalari.
///
/// ★ NIMA UCHUN O'QILGAN BAYROQLARI TEST BILAN QOTIRILGAN: eski tizimda
/// ular har endpointda QO'LDA yozilardi (<c>read_by_student=True</c>) va
/// bir joyda tushib qolgandi — natijada o'quvchining O'Z savoli o'ziga
/// "o'qilmagan" bo'lib qaytardi va bildirishnoma nishoni hech qachon
/// so'nmasdi.
/// </summary>
public class DirectMessageTests
{
    private const long StudentId = 10;
    private const long StaffId = 20;

    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static DirectMessage FromStudent(string body = "Savol bor") =>
        DirectMessage.Create(StudentId, StaffId, StudentId, null, body, Now);

    private static DirectMessage FromStaff(string body = "Javob") =>
        DirectMessage.Create(StudentId, StaffId, StaffId, null, body, Now);

    // ------------------------------------------------------------------ yaratish

    [Fact]
    public void Create_ByStudent_MarksReadForSenderOnly()
    {
        var message = FromStudent();

        message.SentByStudent.Should().BeTrue();
        message.ReadByStudent.Should().BeTrue("yuboruvchi o'z xabarini ko'rib turibdi");
        message.ReadByStaff.Should().BeFalse("kurator uni hali ko'rmagan");
    }

    [Fact]
    public void Create_ByStaff_MarksReadForStaffOnly()
    {
        var message = FromStaff();

        message.SentByStudent.Should().BeFalse();
        message.ReadByStaff.Should().BeTrue();
        message.ReadByStudent.Should().BeFalse();
    }

    [Fact]
    public void Create_TrimsBody_AndRejectsEmpty()
    {
        FromStudent("   Salom   ").Body.Should().Be("Salom");

        var act = () => FromStudent("   ");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_TruncatesOverlyLongBody()
    {
        var body = new string('a', DirectMessage.MaxBodyLength + 500);

        FromStudent(body).Body.Should().HaveLength(DirectMessage.MaxBodyLength);
    }

    /// <summary>
    /// ★ UCHINCHI SHAXS YOZA OLMAYDI. Bu tekshiruv Domain'da bo'lgani
    /// uchun servis qatlamidagi ruxsat tekshiruvi chetlab o'tilsa ham
    /// begona foydalanuvchi suhbatga xabar QO'SHOLMAYDI.
    /// </summary>
    [Fact]
    public void Create_BySomeoneOutsideConversation_IsRejected()
    {
        var act = () => DirectMessage.Create(StudentId, StaffId, senderId: 999, null, "Salom", Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithSameParticipantOnBothSides_IsRejected()
    {
        var act = () => DirectMessage.Create(StudentId, StudentId, StudentId, null, "Salom", Now);

        act.Should().Throw<DomainException>();
    }

    // ------------------------------------------------------------------ o'qildi

    [Fact]
    public void MarkRead_ByRecipient_SetsFlagOnce()
    {
        var message = FromStudent();

        message.MarkRead(StaffId, Now).Should().BeTrue();
        message.ReadByStaff.Should().BeTrue();

        // IDEMPOTENT: ikkinchi chaqiruv hech nima o'zgartirmaydi.
        message.MarkRead(StaffId, Now).Should().BeFalse();
    }

    /// <summary>
    /// ★ O'Z XABARINGIZNI "O'QISH" MA'NOSIZ. Aks holda o'quvchi o'z
    /// savolini ochganda kurator uni ko'rgandek bo'lib qolardi —
    /// "javob kutilmoqda" hisoboti buzilardi.
    /// </summary>
    [Fact]
    public void MarkRead_BySender_ChangesNothing()
    {
        var message = FromStudent();

        message.MarkRead(StudentId, Now).Should().BeFalse();
        message.ReadByStaff.Should().BeFalse("kurator hali ko'rmagan");
    }

    [Fact]
    public void MarkRead_ByOutsider_IsRejected()
    {
        var message = FromStudent();

        var act = () => message.MarkRead(readerId: 999, Now);

        act.Should().Throw<DomainException>();
    }
}
