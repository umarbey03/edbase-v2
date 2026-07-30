using Zinnur.Domain.Enums;
using Zinnur.Domain.Progress;

namespace Zinnur.UnitTests.Progress;

/// <summary>
/// Davomat hisobi.
///
/// ★ ENG MUHIM QOIDA: <c>Late</c> va <c>Partial</c> — QATNASHGAN.
/// Kechikkan yoki yarim qatnashgan o'quvchini butunlay kelmagan bilan
/// tenglashtirish uning davomat foizini ham, reyting balini ham
/// asossiz pasaytirardi.
/// </summary>
public class AttendanceMathTests
{
    [Theory]
    [InlineData(AttendanceStatus.Present, true)]
    [InlineData(AttendanceStatus.Late, true)]
    [InlineData(AttendanceStatus.Partial, true)]
    [InlineData(AttendanceStatus.Absent, false)]
    public void IsAttended_CountsLateAndPartialAsPresent(AttendanceStatus status, bool expected)
    {
        AttendanceMath.IsAttended(status).Should().Be(expected);
    }

    [Fact]
    public void Streak_CountsConsecutiveAttendanceFromNewest()
    {
        AttendanceStatus?[] newestFirst =
        [
            AttendanceStatus.Present,
            AttendanceStatus.Late,
            AttendanceStatus.Partial,
            AttendanceStatus.Absent,        // shu yerda uziladi
            AttendanceStatus.Present,
        ];

        AttendanceMath.Streak(newestFirst).Should().Be(3);
    }

    /// <summary>
    /// ★ "DAVOMAT YOZUVI YO'Q" = "KELMAGAN". Qator faqat xonaga KIRGAN
    /// o'quvchi uchun yaratiladi, ya'ni <c>null</c> — hech qachon
    /// "hali baholanmagan" degani emas.
    /// </summary>
    [Fact]
    public void Streak_TreatsMissingRecordAsAbsence()
    {
        AttendanceStatus?[] newestFirst =
        [
            AttendanceStatus.Present,
            null,
            AttendanceStatus.Present,
        ];

        AttendanceMath.Streak(newestFirst).Should().Be(1);
    }

    [Fact]
    public void Streak_WhenLastLessonMissed_IsZero()
    {
        AttendanceMath.Streak([AttendanceStatus.Absent, AttendanceStatus.Present]).Should().Be(0);
        AttendanceMath.Streak([]).Should().Be(0);
    }

    [Fact]
    public void Tally_ComputesMissedAndPercent()
    {
        var tally = AttendanceTally.Empty
            .Add(attended: true)
            .Add(attended: true)
            .Add(attended: false)
            .Add(attended: true);

        tally.Total.Should().Be(4);
        tally.Attended.Should().Be(3);
        tally.Missed.Should().Be(1);
        tally.Percent.Should().Be(75m);
    }

    /// <summary>
    /// Hali dars o'tilmagan bo'lsa foiz 0 — 100 EMAS. "0 tadan 0 ta"
    /// ni 100% deb ko'rsatish yangi o'quvchiga "mukammal davomat"
    /// nishonini bekorga berardi.
    /// </summary>
    [Fact]
    public void Tally_WithNoLessons_IsZeroPercent()
    {
        AttendanceTally.Empty.Percent.Should().Be(0m);
        AttendanceTally.Empty.Missed.Should().Be(0);
    }
}
