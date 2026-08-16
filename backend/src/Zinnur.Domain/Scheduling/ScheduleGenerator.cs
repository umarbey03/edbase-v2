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
    /// <param name="excludedDates">
    /// ★ BAYRAM KALENDARI (2026-08-16) — umumiy bayramlar + shu guruhning
    /// qo'lda bekor qilingan darslari sanalarining BIRLASHMASI (chaqiruvchi
    /// tayyorlaydi, <c>ScheduleService.ExcludedDatesAsync</c>). Bo'sh
    /// to'plam — hech narsa o'zgarmaydi, eski xatti-harakat saqlanadi.
    ///
    /// ★ SON-ASOSIDA GENERATSIYA (nega SANA OYNASI EMAS): <paramref
    /// name="targetCount"/> — "agar bayram bo'lmaganida shu oynada nechta
    /// dars bo'lardi" (hozirgi <c>group.EndDate</c> formulasi bilan bir xil
    /// hisoblanadi). Keyin kun-kun oldinga yurilib, chiqarib tashlangan
    /// kunlar SONGA QO'SHILMAYDI — natijada oyna avtomatik kerakli
    /// miqdorda oldinga suriladi, "necha kun kerak" degan alohida hisob-
    /// kitobsiz. Talab: *"8 oylik dars qoldirilganiga qarab surilishi
    /// kerak oldinga"*.
    /// </param>
    /// <param name="startingIndex">
    /// Dars raqamlashni qaysi sondan boshlash (qayta tuzishda o'tilgan
    /// darslardan keyin davom etish uchun). Standart 1.
    /// </param>
    public static IReadOnlyList<PlannedSession> Build(
        Group group,
        TimeZoneInfo timeZone,
        IReadOnlySet<DateOnly> excludedDates,
        int startingIndex = 1)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(excludedDates);

        group.ValidateScheduleRule();

        var weekdays = group.Weekdays.ToHashSet();
        var sessions = new List<PlannedSession>();
        var index = startingIndex;

        // Nominal (bayramsiz) oyna — FAQAT "nechta dars kerak" ni
        // hisoblash uchun. Haqiqiy oxirgi sana bundan KEYINGA chiqishi
        // mumkin — buni `GroupDto.EndDate` haqiqiy oxirgi darsdan oladi,
        // bu formuladan EMAS (izoh: `Group.EndDate`).
        var targetCount = CountWeekdayMatches(group.StartDate, group.EndDate, weekdays);

        var day = group.StartDate;
        var placed = 0;

        // ★ ALOHIDA XAVFSIZLIK CHEGARASI (kun sanog'i bo'yicha, `sessions.Count`
        // EMAS): agar `excludedDates` mos keladigan HAR bir kunni to'ssa
        // (masalan kimdir xato bilan cheksiz oraliqni bayram deb belgilasa),
        // `placed` hech qachon o'smaydi va `sessions.Count >= MaxSessionsPerGroup`
        // tekshiruvi HECH QACHON rost bo'lmaydi — cheksiz tsikl. Kun sanog'i
        // esa `excludedDates` dan mustaqil ravishda o'sadi.
        const int maxDaysScanned = 20 * 365;
        var daysScanned = 0;

        while (placed < targetCount)
        {
            if (daysScanned++ >= maxDaysScanned)
                throw new DomainException(
                    "Jadval yarata olmadi — juda ko'p kun bayram/bekor qilingan deb belgilangan. "
                    + "Guruh sanalarini tekshiring.");

            if (sessions.Count >= MaxSessionsPerGroup)
                throw new DomainException(
                    $"Jadval juda uzun ({MaxSessionsPerGroup}+ dars). " +
                    "Boshlanish sanasi va kurs davomiyligini tekshiring.");

            if (weekdays.Contains(day.DayOfWeek) && !excludedDates.Contains(day))
            {
                var start = LocalWallClock.ToUtc(day, group.StartTime, timeZone);

                sessions.Add(new PlannedSession(
                    Index: index,
                    Start: start,
                    End: start.AddMinutes(group.DurationMinutes),
                    Type: group.PlannedSessionType,
                    Title: BuildTitle(group, index),
                    RoomName: LiveSession.GenerateRoomName()));

                index++;
                placed++;
            }

            day = day.AddDays(1);
        }

        return sessions;
    }

    /// <summary>
    /// "Agar bayram bo'lmaganida <paramref name="start"/>—<paramref
    /// name="end"/> oralig'ida nechta <paramref name="weekdays"/> kuni
    /// bo'lardi" — <see cref="Build"/> ning maqsad sonini shu belgilaydi.
    /// </summary>
    private static int CountWeekdayMatches(DateOnly start, DateOnly end, HashSet<DayOfWeek> weekdays)
    {
        var count = 0;
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (weekdays.Contains(day.DayOfWeek))
                count++;
        }
        return count;
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
