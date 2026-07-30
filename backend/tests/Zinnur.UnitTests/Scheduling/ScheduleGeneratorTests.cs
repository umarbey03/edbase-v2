using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;
using Zinnur.Domain.Scheduling;

namespace Zinnur.UnitTests.Scheduling;

/// <summary>
/// <see cref="ScheduleGenerator"/> — 8 oylik dars jadvalini quruvchi SOF funksiya.
///
/// NIMA UCHUN BU TESTLAR ENG MUHIM: jadval bir marta tuziladi va keyin unga
/// davomat, chat, to'lov va LiveKit yozuvlari bog'lanadi. Xato jadval keyin
/// "tuzatiladi" degan narsa yo'q — u butun semestrni buzadi. Eski tizimda
/// aynan shu joyda ikkita jonli bug bor edi:
///   • xona nomi takrorlanardi (B-4) va davomat butunlay to'xtardi;
///   • soat mahalliy vaqt sifatida saqlanardi, konteyner esa UTC'da ishlardi —
///     jadval besh soatga siljib ketardi.
/// </summary>
public class ScheduleGeneratorTests
{
    /// <summary>
    /// 2026-03-02 — DUSHANBA (test ma'lumotining asosi; boshqa kun bo'lsa
    /// hamma kutilgan sanoq buziladi).
    /// </summary>
    private static readonly DateOnly Start = new(2026, 3, 2);

    /// <summary>Toshkent — DST YO'Q, doimiy UTC+5.</summary>
    private static readonly TimeZoneInfo Tashkent =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");

    private const int DurationMinutes = 80;
    private const int CourseMonths = 8;

    /// <summary>
    /// 2026-03-02 .. 2026-11-02 (8 oy, ikki chegara ham kiradi) oralig'idagi
    /// dushanba va chorshanbalar soni: 36 + 35.
    /// </summary>
    private const int ExpectedSessionCount = 71;

    private static Group NewGroup(
        GroupType type = GroupType.Group,
        IEnumerable<DayOfWeek>? weekdays = null,
        int courseMonths = CourseMonths) => new()
        {
            Name = "ATF-1",
            TeacherId = 11,
            AssistantId = 22,
            Type = type,
            StartDate = Start,
            CourseMonths = courseMonths,
            Weekdays = [.. weekdays ?? [DayOfWeek.Monday, DayOfWeek.Wednesday]],
            StartTime = new TimeOnly(19, 0),
            DurationMinutes = DurationMinutes,
        };

    // ================================================================= sanoq va sanalar

    [Fact]
    public void Build_ForEightMonthsTwiceAWeek_ProducesExpectedCount()
    {
        var sessions = ScheduleGenerator.Build(NewGroup(), Tashkent);

        sessions.Should().HaveCount(ExpectedSessionCount);
    }

    [Fact]
    public void Build_PlacesEverySessionOnAChosenWeekday()
    {
        var sessions = ScheduleGenerator.Build(NewGroup(), Tashkent);

        // Kunni MAHALLIY zonada tekshiramiz: UTC'da 14:00 bo'lsa kun bir xil,
        // lekin masalan 23:00 darsda UTC kuni ERTASI kunga o'tib ketardi va
        // "chorshanba darsi" payshanba bo'lib ko'rinardi.
        var actualDays = sessions
            .Select(s => TimeZoneInfo.ConvertTime(s.Start, Tashkent).DayOfWeek)
            .Distinct()
            .Order()
            .ToList();

        actualDays.Should().Equal([DayOfWeek.Monday, DayOfWeek.Wednesday]);
    }

    [Fact]
    public void Build_StartsAtLocalNineteenHundred_ExpressedAsFourteenUtc()
    {
        var sessions = ScheduleGenerator.Build(NewGroup(), Tashkent);

        // Toshkent = UTC+5, DST yo'q => 19:00 mahalliy == 14:00Z, HAR KUNI.
        sessions.Should().AllSatisfy(session =>
        {
            session.Start.Offset.Should().Be(TimeSpan.Zero, "jadval UTC'da saqlanadi");
            session.Start.UtcDateTime.TimeOfDay.Should().Be(new TimeSpan(14, 0, 0));
        });
    }

    [Fact]
    public void Build_KeepsExactDurationForEverySession()
    {
        var sessions = ScheduleGenerator.Build(NewGroup(), Tashkent);

        sessions.Should().AllSatisfy(session =>
            (session.End - session.Start).Should().Be(TimeSpan.FromMinutes(DurationMinutes)));
    }

    [Fact]
    public void Build_FirstSessionIsTheGroupStartDate()
    {
        var sessions = ScheduleGenerator.Build(NewGroup(), Tashkent);

        var first = TimeZoneInfo.ConvertTime(sessions[0].Start, Tashkent);

        DateOnly.FromDateTime(first.DateTime).Should().Be(Start);
        sessions[0].Index.Should().Be(1);
    }

    [Fact]
    public void Build_NeverGoesPastTheCourseEndDate()
    {
        var group = NewGroup();
        var sessions = ScheduleGenerator.Build(group, Tashkent);

        sessions.Should().AllSatisfy(session =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(session.Start, Tashkent).DateTime)
                .Should().BeOnOrBefore(group.EndDate));
    }

    // ================================================================= xona nomi (B-4)

    /// <summary>
    /// ESKI TIZIMNING B-4 BUGI: xona nomi `g{guruh}-l{tartib}` edi va jadval
    /// qayta tuzilganda tartib noldan sanalardi. Ikki dars bir xil nom olib,
    /// LiveKit webhook'i `MultipleResultsFound` bilan yiqilardi — O'SHA KUNGI
    /// BARCHA DAVOMAT YOZILMAY QOLARDI.
    ///
    /// Shuning uchun butun 8 oylik generatsiya (71 dars, bitta paketda, bir
    /// sekund ichida) tekshiriladi: BARCHA nom takrorlanmas bo'lishi shart.
    /// </summary>
    [Fact]
    public void Build_GivesEverySessionAUniqueRoomName()
    {
        var sessions = ScheduleGenerator.Build(NewGroup(), Tashkent);

        sessions.Select(s => s.RoomName).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Nomlar guruhlar ORASIDA ham to'qnashmasin: `UX_LiveSessions_RoomName`
    /// unikal indeksi butun jadval bo'yicha, bitta guruh bo'yicha emas.
    /// </summary>
    [Fact]
    public void Build_GivesUniqueRoomNamesAcrossSeparateGroups()
    {
        var first = ScheduleGenerator.Build(NewGroup(), Tashkent);
        var second = ScheduleGenerator.Build(NewGroup(), Tashkent);

        first.Concat(second).Select(s => s.RoomName).Should().OnlyHaveUniqueItems();
    }

    // ================================================================= startingIndex

    [Fact]
    public void Build_WithStartingIndex_ShiftsIndicesAndTitles()
    {
        const int StartingIndex = 25;

        var sessions = ScheduleGenerator.Build(NewGroup(), Tashkent, startingIndex: StartingIndex);

        sessions[0].Index.Should().Be(StartingIndex);
        sessions[0].Title.Should().Be("ATF-1 — 25-dars");

        sessions[1].Index.Should().Be(StartingIndex + 1);
        sessions[1].Title.Should().Be("ATF-1 — 26-dars");

        sessions[^1].Index.Should().Be(StartingIndex + sessions.Count - 1);
    }

    [Fact]
    public void Build_WithDefaultStartingIndex_NumbersFromOne()
    {
        var sessions = ScheduleGenerator.Build(NewGroup(), Tashkent);

        sessions.Select(s => s.Index).Should().Equal(Enumerable.Range(1, sessions.Count));
        sessions[0].Title.Should().Be("ATF-1 — 1-dars");
    }

    /// <summary>
    /// `startingIndex` sanalarga TA'SIR QILMAYDI — u faqat raqamlash.
    /// Qayta tuzishda shu xossaga tayanamiz: reja bir xil, faqat raqam suriladi.
    /// </summary>
    [Fact]
    public void Build_WithStartingIndex_DoesNotChangeTheDates()
    {
        var plain = ScheduleGenerator.Build(NewGroup(), Tashkent);
        var shifted = ScheduleGenerator.Build(NewGroup(), Tashkent, startingIndex: 40);

        shifted.Select(s => s.Start).Should().Equal(plain.Select(s => s.Start));
    }

    // ================================================================= tur va sarlavha

    [Fact]
    public void Build_ForCuratorGroup_ProducesAssistantSessions()
    {
        var group = NewGroup(
            GroupType.Curator,
            [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]);

        var sessions = ScheduleGenerator.Build(group, Tashkent);

        sessions.Should().AllSatisfy(s => s.Type.Should().Be(SessionType.Assistant));
        sessions[0].Title.Should().Be("ATF-1 — 1-yordamchi dars");
    }

    [Fact]
    public void Build_ForTeacherGroup_ProducesTeacherSessions()
    {
        var sessions = ScheduleGenerator.Build(NewGroup(), Tashkent);

        sessions.Should().AllSatisfy(s => s.Type.Should().Be(SessionType.Teacher));
    }

    // ================================================================= chegaralar

    /// <summary>
    /// ENG UZUN ruxsat etilgan jadval (24 oy × haftada 7 kun ≈ 730 dars)
    /// generator chegarasidan (<see cref="ScheduleGenerator.MaxSessionsPerGroup"/>)
    /// PASTDA turishi kerak.
    ///
    /// Ya'ni chegara faqat HIMOYA: `CourseMonths` validatsiyasi buzilgan yoki
    /// olib tashlangan taqdirda ishga tushadi, haqiqiy ma'lumotda esa hech
    /// qachon to'sqinlik qilmaydi. Bu test shu ikki chegara bir-biriga mos
    /// turishini qo'riqlaydi: `MaxCourseMonths` oshirilsa test yiqiladi va
    /// `MaxSessionsPerGroup` ni ham qayta ko'rish kerakligi darhol ko'rinadi.
    ///
    /// Yon foyda: xona nomi takrorlanmasligi ~730 qatorli eng katta paketda
    /// ham tekshiriladi.
    /// </summary>
    [Fact]
    public void Build_ForTheLongestAllowedCourse_StaysUnderTheHardLimit()
    {
        var group = NewGroup(
            GroupType.Individual,
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
             DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday],
            courseMonths: Group.MaxCourseMonths);

        var sessions = ScheduleGenerator.Build(group, Tashkent);

        sessions.Count.Should().BeLessThan(ScheduleGenerator.MaxSessionsPerGroup);
        sessions.Select(s => s.RoomName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Build_ValidatesTheGroupBeforeGenerating()
    {
        // Oddiy guruh haftada ANIQ 2 kun — bittasi bilan generatsiya
        // BOSHLANMASLIGI kerak (yarim jadval yaratilib qolmasin).
        var group = NewGroup(GroupType.Group, [DayOfWeek.Monday]);

        var build = () => ScheduleGenerator.Build(group, Tashkent);

        build.Should().Throw<DomainException>();
    }

    [Fact]
    public void Build_WithoutTimeZone_Throws()
    {
        var build = () => ScheduleGenerator.Build(NewGroup(), timeZone: null!);

        build.Should().Throw<ArgumentNullException>();
    }
}
