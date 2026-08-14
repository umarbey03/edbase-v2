using Zinnur.Domain.Entities;
using Zinnur.Domain.Staffing;

namespace Zinnur.UnitTests.Staffing;

/// <summary>
/// ========================================================================
/// R33 + R40 NING YAGONA QOIDASI
/// ========================================================================
///
/// Bu testlar UCH ishni qiladi va uchinchisi eng muhimi:
///
///   1) STANDART SOZLAMADA BUGUNGI XATTI-HARAKAT saqlanishini qotiradi —
///      migratsiya kuni hech kimning ekrani o'zgarmasligi kerak;
///   2) tanlov va zaxira yo'l qoidalarini tekshiradi;
///   3) 🔴 IKKI KO'RINISHNI BIR-BIRIGA QARSHI tekshiradi. Qoida ikki
///      shaklda yozilgan — so'rov ifodasi (<c>Predicate</c>, EF uchun) va
///      ro'yxat funksiyasi (<c>Responsible</c>, tartib uchun). Ular
///      ajralib ketsa xato JIMGINA bo'lardi: xodim ro'yxatda ko'rinardi,
///      lekin so'rov uni topmasdi (yoki aksincha). Oxirgi blokdagi
///      matritsa testi buni har sozlama uchun tekshiradi.
/// </summary>
public class StaffResponsibilityTests
{
    private const long Teacher = 100;
    private const long Assistant = 200;
    private const long CuratorGroupTeacher = 300;
    private const long CuratorGroupAssistant = 400;
    private const long Stranger = 999;

    /// <summary>
    /// Sinov guruhi. <paramref name="linked"/> — bog'langan KURATOR guruhi
    /// (bilvosita o'rindiq yo'li).
    /// </summary>
    private static Group MakeGroup(
        long? teacherId = Teacher,
        long? assistantId = Assistant,
        Group? linked = null,
        GroupStaffRole grading = GroupStaffRole.Both,
        GroupStaffRole questions = GroupStaffRole.Assistant) =>
        new()
        {
            Name = "Sinov",
            TeacherId = teacherId,
            AssistantId = assistantId,
            CuratorGroup = linked,
            CuratorGroupId = linked is null ? null : 7,
            AssignmentGraderRole = grading,
            QuestionResponderRole = questions,
        };

    private static Group LinkedCuratorGroup(
        long? teacherId = CuratorGroupTeacher, long? assistantId = CuratorGroupAssistant) =>
        new() { Name = "Kurator guruhi", TeacherId = teacherId, AssistantId = assistantId };

    private static bool Responsible(
        Group group, long staffId, StaffDuty duty, GroupStaffRole? assignmentOverride = null) =>
        StaffResponsibility.Predicate(staffId, duty, assignmentOverride).Compile()(group);

    // ================================================================= 1) bugungi xulq

    /// <summary>
    /// 🔴 ENG MUHIM TEST: standart sozlamada BAHOLASH bugungidek —
    /// ustoz ham, kurator ham baholaydi. Buzilsa migratsiya kuni
    /// yarim markazning baholash navbati yo'qolardi.
    /// </summary>
    [Theory]
    [InlineData(Teacher)]
    [InlineData(Assistant)]
    [InlineData(CuratorGroupTeacher)]
    [InlineData(CuratorGroupAssistant)]
    public void Grading_DefaultBoth_KeepsTodaysBehaviour(long staffId)
    {
        var group = MakeGroup(linked: LinkedCuratorGroup());

        Responsible(group, staffId, StaffDuty.Grading).Should().BeTrue();
    }

    /// <summary>
    /// 🔴 IKKINCHI ENG MUHIM TEST: standart sozlamada SAVOLLARGA faqat
    /// kurator javob beradi. Buzilsa deploy kuni har bir ustozning
    /// "Savollar" ekraniga butun guruh oqib kelardi.
    /// </summary>
    [Fact]
    public void Questions_DefaultAssistant_KeepsTodaysBehaviour()
    {
        var group = MakeGroup(linked: LinkedCuratorGroup());

        Responsible(group, Assistant, StaffDuty.Questions).Should().BeTrue();
        Responsible(group, CuratorGroupAssistant, StaffDuty.Questions).Should().BeTrue();

        Responsible(group, Teacher, StaffDuty.Questions).Should().BeFalse(
            "ustoz bugun `/ustoz/savollar` da bo'sh ro'yxat ko'radi");
        Responsible(group, CuratorGroupTeacher, StaffDuty.Questions).Should().BeFalse();
    }

    [Fact]
    public void Access_IsNeverNarrowed_ByEitherSetting()
    {
        // Ikkala sozlama ham eng qattiq holatda.
        var group = MakeGroup(
            grading: GroupStaffRole.Assistant, questions: GroupStaffRole.Teacher);

        Responsible(group, Teacher, StaffDuty.Access).Should().BeTrue(
            "javob faylini ko'rish tekshiruvchi tanloviga bog'liq emas");
        Responsible(group, Assistant, StaffDuty.Access).Should().BeTrue();
    }

    [Fact]
    public void Stranger_IsNeverResponsible()
    {
        var group = MakeGroup(linked: LinkedCuratorGroup());

        Responsible(group, Stranger, StaffDuty.Access).Should().BeFalse();
        Responsible(group, Stranger, StaffDuty.Grading).Should().BeFalse();
        Responsible(group, Stranger, StaffDuty.Questions).Should().BeFalse();
    }

    // ================================================================= 2) tanlov

    [Fact]
    public void Grading_Assistant_ExcludesTeacher()
    {
        var group = MakeGroup(grading: GroupStaffRole.Assistant);

        Responsible(group, Assistant, StaffDuty.Grading).Should().BeTrue();
        Responsible(group, Teacher, StaffDuty.Grading).Should().BeFalse();
    }

    [Fact]
    public void Grading_Teacher_ExcludesAssistant()
    {
        var group = MakeGroup(grading: GroupStaffRole.Teacher);

        Responsible(group, Teacher, StaffDuty.Grading).Should().BeTrue();
        Responsible(group, Assistant, StaffDuty.Grading).Should().BeFalse();
    }

    [Fact]
    public void Questions_Both_GivesStudentTwoPeers()
    {
        var group = MakeGroup(questions: GroupStaffRole.Both);

        Responsible(group, Assistant, StaffDuty.Questions).Should().BeTrue();
        Responsible(group, Teacher, StaffDuty.Questions).Should().BeTrue();
    }

    [Fact]
    public void AssignmentOverride_BeatsGroupSetting()
    {
        var group = MakeGroup(grading: GroupStaffRole.Teacher);

        Responsible(group, Assistant, StaffDuty.Grading, GroupStaffRole.Assistant)
            .Should().BeTrue("vazifa istisnosi guruh ustunini yengadi");

        Responsible(group, Teacher, StaffDuty.Grading, GroupStaffRole.Assistant)
            .Should().BeFalse();
    }

    // ================================================================= 3) zaxira yo'l

    /// <summary>
    /// 🔴 EGASIZ QOLGAN ISH BO'LMASIN: "kurator tekshirsin" deb qo'yilgan,
    /// lekin kuratori olib tashlangan guruhda topshirilgan ishni ustoz
    /// baholay olishi kerak. Aks holda o'quvchining javobi hech kimga
    /// yetmasdi va buni faqat u shikoyat qilganda bilinardi.
    /// </summary>
    [Fact]
    public void Grading_FallsBackToTeacher_WhenAssistantSeatEmpty()
    {
        var group = MakeGroup(assistantId: null, grading: GroupStaffRole.Assistant);

        Responsible(group, Teacher, StaffDuty.Grading).Should().BeTrue();
    }

    [Fact]
    public void Grading_FallsBackToAssistant_WhenTeacherSeatEmpty()
    {
        var group = MakeGroup(teacherId: null, grading: GroupStaffRole.Teacher);

        Responsible(group, Assistant, StaffDuty.Grading).Should().BeTrue();
    }

    /// <summary>
    /// ★ BILVOSITA O'RINDIQ ZAXIRA YO'LNI OCHMAYDI: o'rindiq bo'sh emas —
    /// unda kurator guruhi orqali odam o'tirgan.
    /// </summary>
    [Fact]
    public void Grading_DoesNotFallBack_WhenSeatFilledViaCuratorGroup()
    {
        var group = MakeGroup(
            assistantId: null,
            linked: LinkedCuratorGroup(teacherId: null),
            grading: GroupStaffRole.Assistant);

        Responsible(group, CuratorGroupAssistant, StaffDuty.Grading).Should().BeTrue();
        Responsible(group, Teacher, StaffDuty.Grading).Should().BeFalse(
            "kurator o'rindig'i bog'langan guruh orqali TO'LA");
    }

    /// <summary>
    /// 🔴 SAVOLLARDA ZAXIRA YO'L YO'Q va bu ATAYLAB: hali hech narsa
    /// yozilmagan, ya'ni egasiz qoladigan ish ham yo'q. Zaxira yo'l
    /// qo'shilsa kuratorsiz guruhlarning savollari deploy kuniyoq
    /// ustozlarga oqib ketardi.
    /// </summary>
    [Fact]
    public void Questions_DoNotFallBack_WhenSeatEmpty()
    {
        var group = MakeGroup(assistantId: null, questions: GroupStaffRole.Assistant);

        Responsible(group, Teacher, StaffDuty.Questions).Should().BeFalse();
    }

    // ================================================================= o'rindiq bandmi

    [Fact]
    public void HasSeat_SeesIndirectSeat()
    {
        var group = MakeGroup(assistantId: null, linked: LinkedCuratorGroup());

        StaffResponsibility.HasSeat(GroupStaffRole.Assistant).Compile()(group)
            .Should().BeTrue("kurator bog'langan guruhdan keladi");
    }

    [Fact]
    public void HasSeat_FalseWhenBothPathsEmpty()
    {
        var group = MakeGroup(assistantId: null, linked: LinkedCuratorGroup(assistantId: null));

        StaffResponsibility.HasSeat(GroupStaffRole.Assistant).Compile()(group)
            .Should().BeFalse();
    }

    // ================================================================= tartib

    /// <summary>
    /// Suhbatlar ro'yxatining TARTIBI: kurator birinchi. Bugun o'quvchining
    /// yagona suhbatdoshi kurator, va `Both` yoqilganda ham u ro'yxat
    /// boshida qolishi kerak — aks holda mavjud o'quvchilarning odatlangan
    /// birinchi qatori bir kechada almashardi.
    /// </summary>
    [Fact]
    public void Responsible_Both_PutsAssistantFirst()
    {
        var seats = new StaffResponsibility.StaffSeats(
            Teacher, Assistant, CuratorGroupTeacher, CuratorGroupAssistant);

        StaffResponsibility.Responsible(seats, GroupStaffRole.Both, StaffDuty.Questions)
            .Should().Equal(Assistant, CuratorGroupAssistant, Teacher, CuratorGroupTeacher);
    }

    // ================================================================= 3) IKKI KO'RINISH MOSLIGI

    /// <summary>
    /// 🔴 QOIDANING IKKI SHAKLI AJRALIB KETMASIN.
    ///
    /// <c>Predicate</c> (SQL uchun) va <c>Responsible</c> (tartib uchun)
    /// bitta qoidani ikki xil ifodalaydi. Bu test HAR o'rindiq
    /// kombinatsiyasi × HAR rol × HAR ish uchun ikkalasini taqqoslaydi:
    /// biri o'zgarib ikkinchisi qolsa — darhol qizil.
    /// </summary>
    [Fact]
    public void Predicate_And_Responsible_AlwaysAgree()
    {
        long?[] options = [null, Teacher, Assistant, CuratorGroupTeacher, CuratorGroupAssistant];
        long[] actors = [Teacher, Assistant, CuratorGroupTeacher, CuratorGroupAssistant, Stranger];

        GroupStaffRole[] roles =
            [GroupStaffRole.Both, GroupStaffRole.Teacher, GroupStaffRole.Assistant];

        StaffDuty[] duties = [StaffDuty.Access, StaffDuty.Grading, StaffDuty.Questions];

        foreach (var teacherId in options)
        foreach (var assistantId in options)
        foreach (var linkedTeacherId in options)
        foreach (var linkedAssistantId in options)
        foreach (var role in roles)
        foreach (var duty in duties)
        {
            var linked = LinkedCuratorGroup(linkedTeacherId, linkedAssistantId);

            var group = MakeGroup(
                teacherId,
                assistantId,
                linked,
                grading: role,
                questions: role);

            var seats = new StaffResponsibility.StaffSeats(
                teacherId, assistantId, linkedTeacherId, linkedAssistantId);

            var listed = StaffResponsibility.Responsible(seats, role, duty).ToHashSet();

            foreach (var actor in actors)
            {
                Responsible(group, actor, duty).Should().Be(
                    listed.Contains(actor),
                    "qoidaning ikki shakli mos kelishi shart: "
                    + $"ustoz={teacherId}, kurator={assistantId}, "
                    + $"bog'langan=({linkedTeacherId},{linkedAssistantId}), "
                    + $"rol={role}, ish={duty}, xodim={actor}");
            }
        }
    }
}
