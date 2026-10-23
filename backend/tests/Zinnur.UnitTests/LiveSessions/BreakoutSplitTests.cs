using Zinnur.Application.LiveSessions.Services;

namespace Zinnur.UnitTests.LiveSessions;

/// <summary>
/// <see cref="BreakoutService.Split"/> — loyiha egasi qoidasi: har guruhda
/// ko'pi bilan 4 o'quvchi, avtomatik bo'lishda (2+ o'quvchi) hech kim yolg'iz
/// qolmaydi, hech kim tushib qolmaydi va takrorlanmaydi.
/// </summary>
public class BreakoutSplitTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(7, 2)]
    [InlineData(8, 2)]
    [InlineData(9, 3)]
    [InlineData(13, 4)]
    [InlineData(30, 8)]
    public void Every_student_lands_in_exactly_one_room_of_two_to_four(int students, int expectedRooms)
    {
        var ids = Enumerable.Range(1, students).Select(i => (long)i).ToList();

        var rooms = BreakoutService.Split(ids);

        rooms.Should().HaveCount(expectedRooms);
        rooms.Select(room => room.Number).Should().Equal(Enumerable.Range(1, expectedRooms));
        rooms.Should().OnlyContain(room =>
            room.StudentIds.Count >= BreakoutService.MinGroupSize
            && room.StudentIds.Count <= BreakoutService.MaxGroupSize);

        // Ikki va undan ko'p o'quvchida hech kim yolg'iz qolmaydi.
        if (students >= 2)
            rooms.Should().OnlyContain(room => room.StudentIds.Count >= 2);
        rooms.SelectMany(room => room.StudentIds).Should().BeEquivalentTo(ids);

        var sizes = rooms.Select(room => room.StudentIds.Count).ToList();
        (sizes.Max() - sizes.Min()).Should().BeLessThanOrEqualTo(1);
    }
}
