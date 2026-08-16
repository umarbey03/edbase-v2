using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Scheduling.Dtos;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Scheduling;

namespace Zinnur.Application.Scheduling.Services;

/// <summary>
/// Dars jadvali use-case'lari.
///
/// Jadval MATEMATIKASI bu yerda EMAS — u <see cref="ScheduleGenerator"/> da
/// (sof, bazasiz funksiya). Bu servis faqat uchta ishni bajaradi:
///  1) generatordan rejani so'raydi,
///  2) mavjud darslardan NIMANI SAQLASHNI hal qiladi,
///  3) farqni <c>DbContext</c> ga yozadi (saqlash chaqiruvchida).
/// </summary>
public sealed class ScheduleService(
    IApplicationDbContext db,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock,
    ILogger<ScheduleService> logger) : IScheduleService
{
    // ================================================================= yaratish

    public async Task<int> GenerateForNewGroupAsync(Group group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Yangi guruhda dars bo'lishi mumkin emas — lekin bu metod xato
        // ishlatilsa jimgina DUBLIKAT jadval yaratib qo'ymasligi kerak.
        if (group.Id != 0 && await db.LiveSessions.AnyAsync(s => s.GroupId == group.Id, ct))
        {
            throw new InvalidOperationException(
                "Guruhda allaqachon darslar bor — GenerateForNewGroupAsync faqat yangi guruh uchun. "
                + "Mavjud jadvalni o'zgartirish uchun RegenerateAsync ishlatiladi.");
        }

        var excludedDates = await ExcludedDatesAsync(group.Id, ct);
        var planned = ScheduleGenerator.Build(group, timeZone.TimeZone, excludedDates);

        foreach (var session in planned)
            Attach(group, session);

        ScheduleLog.Generated(logger, group.Name, planned.Count);
        return planned.Count;
    }

    // ================================================================= qayta tuzish

    /// <summary>
    /// ========================================================================
    /// JADVALNI QAYTA TUZISHNING YAGONA QOIDASI
    /// ========================================================================
    ///
    /// ESKI TIZIM BUGI (nima uchun bu metod shunday yozilgan): eski panelda
    /// jadval qayta tuzilganda guruhning BARCHA darslari o'chirilib, noldan
    /// yaratilardi. Natijalari:
    ///   • o'tgan darslarning davomati va chat tarixi kaskad bilan YO'Q BO'LARDI;
    ///   • jonli turgan dars o'chib, ustoz xonadan uchib chiqardi;
    ///   • dars Id va LiveKit xona nomlari o'zgarib, tarqatilgan havolalar
    ///     ishlamay qolardi;
    ///   • dars tartib raqami noldan sanalib, xona nomi to'qnashardi (B-4).
    ///
    /// ENDIGI QOIDA:
    ///   1) O'CHIRILADI  — FAQAT kelajakdagi VA holati <c>Scheduled</c> darslar.
    ///   2) SAQLANADI    — o'tgan hamma dars + <c>Live</c>/<c>Ended</c>/
    ///                     <c>Cancelled</c> holatidagi hamma dars.
    ///   3) YARATILADI   — faqat HOZIRDAN KEYINGI sanalar va saqlanib qolgan
    ///                     darsning vaqtiga TO'QNASHMAGANLARI.
    ///   4) RAQAMLASH    — saqlangan darslardan KEYIN davom etadi (1 dan
    ///                     boshlanmaydi), aks holda sarlavhalar takrorlanardi.
    /// </summary>
    public async Task<ScheduleChangeSummary> RegenerateAsync(
        Group group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        var now = clock.GetUtcNow();

        var existing = await db.LiveSessions
            .AsTracking()
            .Where(s => s.GroupId == group.Id)
            .ToListAsync(ct);

        // ---- 1-2) Ikkiga ajratamiz: o'chiriladigan va saqlanadigan.
        var removable = new List<LiveSession>();
        var preserved = new List<LiveSession>();

        foreach (var session in existing)
            (IsReplaceable(session, now) ? removable : preserved).Add(session);

        db.LiveSessions.RemoveRange(removable);

        // ---- 4) Raqamlash: yangi darslar saqlangan darslardan KEYIN davom etadi.
        //
        // `ScheduleGenerator.Build` rejani HAR DOIM kurs boshidan quradi, ya'ni
        // reja ichida o'tib ketgan sanalar ham bor va biz ularni yozmaymiz.
        // Shuning uchun `startingIndex` ni shunday tanlaymiz: reja ichidagi
        // BIRINCHI KELAJAK dars aynan `preserved.Count + 1` raqamini olsin.
        // Generatorni ikki marta chaqirish arzon — u sof funksiya, bazaga
        // yoki tarmoqqa tegmaydi.
        var excludedDates = await ExcludedDatesAsync(group.Id, ct);

        var probe = ScheduleGenerator.Build(group, timeZone.TimeZone, excludedDates);
        var alreadyPast = probe.Count(p => p.Start <= now);

        var planned = ScheduleGenerator.Build(
            group, timeZone.TimeZone, excludedDates, startingIndex: preserved.Count + 1 - alreadyPast);

        // ---- 3) Faqat kelajak va to'qnashmaganlar.
        //
        // TO'QNASHUV nima uchun tekshiriladi: bekor qilingan (yoki allaqachon
        // o'tkazilgan) dars SAQLANADI. Agar yangi reja aynan o'sha vaqtga
        // tushsa, bir vaqtda ikkita dars paydo bo'lardi va o'quvchi qaysiga
        // kirishini bilmasdi.
        var taken = preserved.Select(s => s.ScheduledStart).ToHashSet();
        var created = 0;

        foreach (var session in planned)
        {
            if (session.Start <= now) continue;
            if (!taken.Add(session.Start)) continue;

            Attach(group, session);
            created++;
        }

        ScheduleLog.Regenerated(logger, group.Name, created, removable.Count, preserved.Count);

        return new ScheduleChangeSummary(
            ScheduleTouched: true,
            Regenerated: true,
            Created: created,
            Deleted: removable.Count,
            Preserved: preserved.Count,
            HostsUpdated: 0,
            TitlesUpdated: 0,
            Reason: RegeneratedReason);
    }

    // ================================================================= o'rnida tahrirlash

    public async Task<int> RetargetHostAsync(Group group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        var host = group.HostId;
        var changed = 0;

        foreach (var session in await LoadFutureScheduledAsync(group.Id, ct))
        {
            if (session.HostId == host) continue;

            session.HostId = host;
            changed++;
        }

        ScheduleLog.HostsRetargeted(logger, group.Name, changed);
        return changed;
    }

    public async Task<int> RenameFutureSessionsAsync(Group group, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        var sessions = await LoadFutureScheduledAsync(group.Id, ct);

        if (sessions.Count == 0) return 0;

        // Sarlavha SHAKLI ("ATF-1 — 12-dars") shu yerda QAYTA YOZILMAYDI —
        // uni `ScheduleGenerator` quradi (DRY: shakl bitta joyda).
        // Rejani mavjud darslarga AYNAN `ScheduledStart` bo'yicha moslashtirib,
        // faqat sarlavhani ko'chiramiz — dars Id'lari va xona nomlari qoladi.
        var excludedDates = await ExcludedDatesAsync(group.Id, ct);

        var titles = ScheduleGenerator
            .Build(group, timeZone.TimeZone, excludedDates)
            .ToDictionary(p => p.Start, p => p.Title);

        var changed = 0;

        foreach (var session in sessions)
        {
            if (!titles.TryGetValue(session.ScheduledStart, out var title)) continue;
            if (string.Equals(session.Title, title, StringComparison.Ordinal)) continue;

            session.Title = title;
            changed++;
        }

        ScheduleLog.TitlesRenamed(logger, group.Name, changed);
        return changed;
    }

    // ================================================================= o'qish

    public async Task<IReadOnlyList<ScheduledSessionDto>> ListAsync(
        long groupId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken ct = default)
    {
        var rows = db.LiveSessions.AsNoTracking().Where(s => s.GroupId == groupId);

        // Oraliq KESISHISH bo'yicha filtrlanadi (boshlanish nuqtasi bo'yicha
        // emas): oraliq o'rtasida davom etayotgan dars ham ko'rinsin.
        if (fromUtc is { } start)
            rows = rows.Where(s => s.ScheduledEnd >= start);

        if (toUtc is { } end)
            rows = rows.Where(s => s.ScheduledStart <= end);

        return await rows
            .OrderBy(s => s.ScheduledStart)
            .ThenBy(s => s.Id)
            // Chegara: bitta guruhning jadvali generator chegarasidan
            // uzun bo'lishi mumkin emas, ya'ni bu kesish ma'lumot yo'qotmaydi.
            .Take(ScheduleGenerator.MaxSessionsPerGroup)
            .Select(s => new ScheduledSessionDto(
                s.Id,
                s.GroupId,
                s.Title,
                s.Type,
                s.Status,
                s.ScheduledStart,
                s.ScheduledEnd,
                s.ActualStart,
                s.ActualEnd,
                s.HostId,
                db.Users.Where(u => u.Id == s.HostId).Select(u => u.FullName).FirstOrDefault(),
                s.RoomName))
            .ToListAsync(ct);
    }

    // ================================================================= ichki yordamchi

    /// <summary>
    /// Dars qayta tuzishda O'CHIRILISHI mumkinmi.
    ///
    /// Ikkala shart ham MAJBURIY:
    ///   • <c>Scheduled</c> — <c>Live</c>/<c>Ended</c> darsda davomat va chat bor,
    ///     <c>Cancelled</c> esa ataylab bekor qilingan (qaytarib tiklanmasin);
    ///   • kelajakda — o'tgan dars o'tgan, uni "qayta rejalashtirish" ma'nosiz.
    /// </summary>
    private static bool IsReplaceable(LiveSession session, DateTimeOffset now) =>
        session.Status == SessionStatus.Scheduled && session.ScheduledStart > now;

    /// <summary>
    /// ★ BAYRAM KALENDARI (2026-08-16) — <c>ScheduleGenerator.Build</c> ga
    /// beriladigan "chiqarib tashlanadigan sanalar" to'plami: UMUMIY
    /// bayramlar (<c>Holidays</c>, butun platforma) + SHU guruhning
    /// allaqachon bekor qilingan darslari sanalari. Ikkalasi bitta to'plamga
    /// birlashtirilishining sababi: manbasidan qat'i nazar, bekor qilingan
    /// sana IKKINCHI MARTA rejalashtirilmasligi kerak — generator ikkalasini
    /// bir xil "bu kunga tegma" ko'rsatmasi deb qabul qiladi.
    /// </summary>
    private async Task<IReadOnlySet<DateOnly>> ExcludedDatesAsync(long groupId, CancellationToken ct)
    {
        var zone = timeZone.TimeZone;

        var holidayDates = await db.Holidays.AsNoTracking()
            .Select(h => h.Date)
            .ToListAsync(ct);

        var cancelledStarts = await db.LiveSessions.AsNoTracking()
            .Where(s => s.GroupId == groupId && s.Status == SessionStatus.Cancelled)
            .Select(s => s.ScheduledStart)
            .ToListAsync(ct);

        var result = new HashSet<DateOnly>(holidayDates);
        foreach (var start in cancelledStarts)
            result.Add(LocalWallClock.LocalDate(start, zone));

        return result;
    }

    private async Task<List<LiveSession>> LoadFutureScheduledAsync(long groupId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        return await db.LiveSessions
            .AsTracking()
            .Where(s => s.GroupId == groupId
                     && s.Status == SessionStatus.Scheduled
                     && s.ScheduledStart > now)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Rejalashtirilgan darsni <c>DbContext</c> ga qo'shadi.
    ///
    /// NIMA UCHUN NAVIGATSIYA (<c>session.Group = group</c>) VA FAQAT
    /// <c>GroupId</c> EMAS: yangi guruhda <c>Id</c> hali 0 (baza bermagan).
    /// Navigatsiya bilan bog'lansa EF avval guruhni, keyin darslarni yozadi
    /// va FK ni o'zi to'ldiradi — hammasi BITTA <c>SaveChanges</c>, ya'ni
    /// bitta tranzaksiya. Aks holda guruh yozilib, jadval yozilmay qolishi
    /// mumkin bo'lardi.
    /// </summary>
    private void Attach(Group group, PlannedSession planned)
    {
        var session = planned.ToEntity(group.Id, group.HostId);
        session.Group = group;

        db.LiveSessions.Add(session);
    }

    private const string RegeneratedReason =
        "Jadval qoidasi o'zgardi — kelajakdagi rejalashtirilgan darslar qayta tuzildi. "
        + "O'tgan, jonli, yakunlangan va bekor qilingan darslar saqlandi.";
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848).
///
/// Jadval amallari AUDIT uchun logga tushadi: "kelajakdagi darslarim qayerga
/// ketdi?" degan savolga javob logdan topilishi kerak.
/// </summary>
internal static partial class ScheduleLog
{
    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Information,
        Message = "Jadval tuzildi: guruh='{GroupName}', darslar={Count}")]
    internal static partial void Generated(ILogger logger, string groupName, int count);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Warning,
        Message = "Jadval QAYTA TUZILDI: guruh='{GroupName}', yangi={Created}, "
                + "o'chirilgan={Deleted}, saqlangan={Preserved}")]
    internal static partial void Regenerated(
        ILogger logger, string groupName, int created, int deleted, int preserved);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Information,
        Message = "Dars hosti yangilandi (jadvalga tegilmadi): guruh='{GroupName}', darslar={Count}")]
    internal static partial void HostsRetargeted(ILogger logger, string groupName, int count);

    [LoggerMessage(
        EventId = 5003,
        Level = LogLevel.Information,
        Message = "Dars sarlavhalari yangilandi (jadvalga tegilmadi): guruh='{GroupName}', darslar={Count}")]
    internal static partial void TitlesRenamed(ILogger logger, string groupName, int count);
}
