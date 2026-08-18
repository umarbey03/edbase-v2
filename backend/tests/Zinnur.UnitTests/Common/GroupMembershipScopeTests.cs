using Zinnur.Application.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.UnitTests.Common;

/// <summary>
/// ========================================================================
/// "SHU GURUHNING O'QUVCHILARI" — YAGONA QOIDA
/// ========================================================================
///
/// <see cref="GroupMembershipScope.ActiveIn"/> — EF uchun yozilgan ifoda,
/// lekin u SOF funksiya: kompilyatsiya qilib oddiy obyektda baholash
/// mumkin (<c>StaffResponsibilityTests</c> dagi AYNI naqsh). Bazasiz
/// sinaladi, shuning uchun domain testlari bilan bir loyihada.
///
/// 🔴 NIMA UCHUN QOTIRILADI: aynan shu qoida loyihada 14 joyda qo'lda
/// takrorlangan va ikki xil javob bergan edi — kurator darsi boshlanganda
/// o'quvchiga xabar ketardi, lekin darsga kirmoqchi bo'lganda 403 olardi.
/// Test ikkala shoxni ham (to'g'ridan-to'g'ri va kurator orqali) va
/// "faqat faol a'zo" shartini qulflaydi.
/// </summary>
public class GroupMembershipScopeTests
{
    private const long TeacherGroupId = 10;
    private const long CuratorGroupId = 20;
    private const long OtherGroupId = 30;

    /// <param name="linkedCuratorGroupId">
    /// A'zo turgan USTOZ guruhi qaysi kurator guruhiga bog'langan.
    /// </param>
    private static GroupMember Member(
        long groupId,
        long? linkedCuratorGroupId = null,
        MemberStatus status = MemberStatus.Active) =>
        new()
        {
            GroupId = groupId,
            StudentId = 1,
            Status = status,
            Group = new Group
            {
                Name = "Sinov",
                CuratorGroupId = linkedCuratorGroupId,
            },
        };

    private static bool InScope(GroupMember member, long groupId) =>
        GroupMembershipScope.ActiveIn(groupId).Compile()(member);

    [Fact]
    public void DirectMember_IsInScope()
    {
        InScope(Member(TeacherGroupId), TeacherGroupId).Should().BeTrue();
    }

    [Fact]
    public void MemberOfOtherGroup_IsNotInScope()
    {
        InScope(Member(OtherGroupId), TeacherGroupId).Should().BeFalse();
    }

    /// <summary>
    /// 🔴 ASOSIY HOLAT: kurator guruhida o'quvchi TO'G'RIDAN-TO'G'RI a'zo
    /// bo'lmaydi (<c>GroupService.EnsureAcceptsDirectMembers</c> buni
    /// majburlaydi) — u bog'langan ustoz guruhidan keladi. Bu shoxsiz
    /// kurator darsining har bir ro'yxati BO'SH chiqardi.
    /// </summary>
    [Fact]
    public void MemberOfLinkedTeacherGroup_IsInCuratorGroupScope()
    {
        var member = Member(TeacherGroupId, linkedCuratorGroupId: CuratorGroupId);

        InScope(member, CuratorGroupId).Should().BeTrue();
    }

    /// <summary>
    /// Boshqa kuratorga bog'langan guruh a'zosi bu kurator guruhining
    /// ro'yxatiga TUSHMAYDI — kengaytirish "hammani qo'shish" emas.
    /// </summary>
    [Fact]
    public void MemberOfGroupLinkedToAnotherCurator_IsNotInScope()
    {
        var member = Member(TeacherGroupId, linkedCuratorGroupId: OtherGroupId);

        InScope(member, CuratorGroupId).Should().BeFalse();
    }

    /// <summary>
    /// Oddiy guruhda ikkinchi shox HECH QACHON rost bo'lmaydi: bog'lanish
    /// yo'q bo'lsa <c>CuratorGroupId</c> — <c>null</c>.
    /// </summary>
    [Fact]
    public void UnlinkedGroup_HasNoCuratorBranch()
    {
        InScope(Member(TeacherGroupId), CuratorGroupId).Should().BeFalse();
    }

    [Theory]
    [InlineData(MemberStatus.Paused)]
    [InlineData(MemberStatus.Stopped)]
    [InlineData(MemberStatus.Moved)]
    public void NonActiveMember_IsNeverInScope(MemberStatus status)
    {
        var direct = Member(TeacherGroupId, status: status);
        var viaCurator = Member(TeacherGroupId, CuratorGroupId, status);

        InScope(direct, TeacherGroupId).Should().BeFalse();
        InScope(viaCurator, CuratorGroupId).Should().BeFalse();
    }
}
