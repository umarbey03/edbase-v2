using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Scheduling;

/// <summary>
/// Guruh jadval qoidasidan dars ro'yxatini quradi. SOF funksiya — bazasiz,
/// tarmoqsiz, tasodifiylikdan tashqari hech qanday tashqi holatga bog'liq emas.
///
/// Shu tufayli butun jadval mantig'i bazasiz test qilinadi.
/// </summary>
public static class ScheduleGenerator
{
    /// <summary>
    /// Bitta guruh uchun bir necha darsni yaratishning yuqori chegarasi.
    ///
    /// NIMA UCHUN: 24 oy × 7 kun ≈ 730 dars. Chegara noto'g'ri kiritilgan
    /// ma'lumot (masalan 1900-yildan boshlangan kurs) minglab qator
    /// yaratib bazani to'ldirib qo'yishidan himoya qiladi.
    /// </summary>
    public const int MaxSessionsPerGroup = 1000;

    /// <summary>
    /// Guruh qoidasidan dars rejasini quradi.
    /// </summary>
    /// <param name="group">Jadval qoidasi bo'lgan guruh.</param>
    /// <param name="timeZone">
    /// Guruh soati QAYSI zonada berilgan (odatda Asia/Tashkent).
    /// Domain aniq zonani BILMAYDI — u tashqaridan beriladi, shuning uchun
    /// bu mantiq boshqa mintaqada ham qayta ishlatiladi.
    /// </param>
    /// <param name="startingIndex">
    /// Dars raqamlashni qaysi sondan boshlash (qayta tuzishda o'tilgan
    /// darslardan keyin davom etish uchun). Standart 1.
    /// </param>
    public static IReadOnlyList<PlannedSession> Build(
        Group group,
        TimeZoneInfo timeZone,
        int startingIndex = 1)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(timeZone);

        group.ValidateScheduleRule();

        var weekdays = group.Weekdays.ToHashSet();
        var sessions = new List<PlannedSession>();
        var index = startingIndex;

        var end = group.EndDate;

        for (var day = group.StartDate; day <= end; day = day.AddDays(1))
        {
            if (!weekdays.Contains(day.DayOfWeek))
                continue;

            if (sessions.Count >= MaxSessionsPerGroup)
                throw new DomainException(
                    $"Jadval juda uzun ({MaxSessionsPerGroup}+ dars). " +
                    "Boshlanish sanasi va kurs davomiyligini tekshiring.");

            var start = LocalWallClock.ToUtc(day, group.StartTime, timeZone);

            sessions.Add(new PlannedSession(
                Index: index,
                Start: start,
                End: start.AddMinutes(group.DurationMinutes),
                Type: group.PlannedSessionType,
                Title: BuildTitle(group, index),
                RoomName: LiveSession.GenerateRoomName()));

            index++;
        }

        return sessions;
    }

    /// <summary>
    /// Dars sarlavhasi: "ATF-1 — 12-dars" yoki kurator uchun
    /// "ATF-1 — 12-yordamchi dars".
    /// </summary>
    private static string BuildTitle(Group group, int index) =>
        group.IsCuratorGroup
            ? $"{group.Name} — {index}-yordamchi dars"
            : $"{group.Name} — {index}-dars";

    // Mahalliy devor-vaqtini UTC ga o'girish `LocalWallClock` da —
    // aynan shu konvertatsiya oylik reyting oralig'ida ham kerak
    // (`BillingPeriod.UtcRange`), va DST tuzog'i ikkalasida bir xil.
}

/// <summary>
/// Rejalashtirilgan bitta dars — hali bazaga yozilmagan.
/// </summary>
/// <param name="Index">Kurs boshidan hisoblangan tartib raqami (1 dan).</param>
/// <param name="Start">Boshlanish vaqti (UTC).</param>
/// <param name="End">Tugash vaqti (UTC).</param>
/// <param name="Type">Ustoz darsi yoki kurator darsi.</param>
/// <param name="Title">Ko'rinadigan sarlavha.</param>
/// <param name="RoomName">Takrorlanmas LiveKit xona nomi.</param>
public sealed record PlannedSession(
    int Index,
    DateTimeOffset Start,
    DateTimeOffset End,
    SessionType Type,
    string Title,
    string RoomName)
{
    /// <summary>Bazaga yozish uchun <see cref="LiveSession"/> ga aylantiradi.</summary>
    public LiveSession ToEntity(long groupId, long? hostId) => new()
    {
        GroupId = groupId,
        HostId = hostId,
        Title = Title,
        Type = Type,
        Status = SessionStatus.Scheduled,
        ScheduledStart = Start,
        ScheduledEnd = End,
        RoomName = RoomName,
    };
}
