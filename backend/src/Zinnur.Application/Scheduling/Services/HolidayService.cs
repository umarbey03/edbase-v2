using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Scheduling.Dtos;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Scheduling.Services;

/// <summary>
/// <see cref="IHolidayService"/> ning amalga oshirilishi.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NEGA SINXRON (fon vazifasi EMAS)
/// ══════════════════════════════════════════════════════════════════════
/// Bayram yiliga bir necha marta e'lon qilinadi, butun ish ICHKI DB yozuvi
/// (tashqi tarmoq yo'q — `Telegram DM` navbati ishlatiladigan
/// `INotificationOutbox` bilan chalkashtirilmasin), va xodim "nechta
/// guruhga tegdi" javobini DARHOL ko'rishi kerak. Guruhlar soni minglabga
/// yetsa bu qarorni qayta ko'rib chiqish kerak bo'ladi — hozircha emas.
///
/// RUXSAT: `GroupCategoryService.EnsureCanManage` bilan AYNI qoida
/// (Academic/Admin) — bayram e'lon qilish guruh jadvalini o'zgartiradi,
/// ya'ni uni o'zgartira oladigan odam guruhni ham boshqara oladigan
/// odam bo'lishi kerak.
/// </summary>
public sealed class HolidayService(
    IApplicationDbContext db,
    IScheduleService schedule,
    IScheduleTimeZoneProvider timeZone,
    TimeProvider clock) : IHolidayService
{
    public async Task<IReadOnlyList<HolidayDto>> ListAsync(long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        // ★ TARTIB PROYEKSIYADAN OLDIN: EF DTO konstruktoriga yasalgan
        // obyektning maydoni bo'yicha `ORDER BY` ni TARJIMA QILA OLMAYDI
        // (`GroupService.ListAsync` dagi bilan AYNI izoh/sabab).
        return await Project(db.Holidays.AsNoTracking().OrderByDescending(h => h.Date))
            .ToListAsync(ct);
    }

    public async Task<HolidayImpactDto> CreateAsync(
        CreateHolidayRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        if (request.StartDate == default)
            throw Invalid(StartDateField, "Boshlanish sanasini kiriting.");

        // Bo'sh (default) tugash sanasi = bitta kunlik bayram — frontend
        // ikkalasini ham teng yuboradi, lekin eski so'rovchilar uchun ham
        // xavfsiz zaxira.
        var endDate = request.EndDate == default ? request.StartDate : request.EndDate;
        if (endDate < request.StartDate)
            throw Invalid(EndDateField, "Tugash sanasi boshlanish sanasidan oldin bo'lishi mumkin emas.");

        var rangeDays = endDate.DayNumber - request.StartDate.DayNumber + 1;
        if (rangeDays > MaxRangeDays)
            throw Invalid(EndDateField, $"Bayram oralig'i {MaxRangeDays} kundan oshmasligi kerak.");

        var label = (request.Label ?? string.Empty).Trim();
        if (label.Length == 0)
            throw Invalid(LabelField, "Bayram nomini kiriting.");

        if (label.Length > Holiday.MaxLabelLength)
            throw Invalid(LabelField, "Bayram nomi juda uzun.");

        var existingDates = await db.Holidays.AsNoTracking()
            .Where(h => h.Date >= request.StartDate && h.Date <= endDate)
            .Select(h => h.Date)
            .ToListAsync(ct);
        var existingSet = existingDates.ToHashSet();

        var zone = timeZone.TimeZone;
        var now = clock.GetUtcNow();
        var reason = "Bayram: " + label;

        var createdHolidays = new List<Holiday>();
        var allAffectedSessions = new List<LiveSession>();
        var allAffectedGroups = new Dictionary<long, Group>();
        var skippedCount = 0;

        // ── HAR KUN UCHUN ALOHIDA `Holiday` QATORI ──
        //
        // Entity darajasida "oraliq" tushunchasi yo'q (unique `Date`), shuning
        // uchun oraliq shu yerda kunlarga YOYILADI. Guruhlar uchun jadval
        // qayta tuzish esa OXIRIDA, guruh bo'yicha BIR MARTA (pastda) —
        // aks holda 10 kunlik bayramda bitta guruh 10 marta qayta tuzilardi.
        for (var date = request.StartDate; date <= endDate; date = date.AddDays(1))
        {
            if (existingSet.Contains(date))
            {
                skippedCount++;
                continue;
            }

            var holiday = new Holiday { Date = date, Label = label, CreatedById = actorId };
            holiday.Validate();
            db.Holidays.Add(holiday);
            createdHolidays.Add(holiday);

            // Mahalliy kun chegaralari -> UTC oralig'i: `ScheduledStart` bazada
            // UTC saqlanadi, ya'ni to'g'ridan-to'g'ri `DateOnly` bilan
            // solishtirib bo'lmaydi (`LiveSessionService.GetCalendarAsync`
            // dagi bilan AYNI naqsh).
            var dayStart = LocalWallClock.StartOfDayUtc(date, zone);
            var dayEnd = LocalWallClock.StartOfDayUtc(date.AddDays(1), zone);

            var affectedSessions = await db.LiveSessions
                .AsTracking()
                .Include(s => s.Group)
                .Where(s => s.Status == SessionStatus.Scheduled
                         && s.ScheduledStart >= dayStart && s.ScheduledStart < dayEnd)
                .ToListAsync(ct);

            foreach (var session in affectedSessions)
            {
                session.Cancel(reason, now);
                allAffectedSessions.Add(session);
                allAffectedGroups[session.Group!.Id] = session.Group!;
            }
        }

        if (createdHolidays.Count == 0)
            throw new ConflictException("Tanlangan sana(lar) allaqachon bayram sifatida belgilangan.");

        // ── HAR TA'SIRLANGAN GURUH UCHUN JADVAL QAYTA TUZILADI (BIR MARTA) ──
        //
        // `ScheduleService.RegenerateAsync` allaqachon `Holidays` jadvalini
        // (shu orqali yangi yozilgan bayramlarni ham) `ExcludedDatesAsync`
        // bilan o'qiydi, ya'ni bekor qilingan darsning o'rniga oxiriga BITTA
        // qo'shimcha dars AVTOMATIK qo'shiladi — alohida "dars qo'shish"
        // kodi shart emas (izoh: `ScheduleGenerator.Build`).
        foreach (var group in allAffectedGroups.Values)
            await schedule.RegenerateAsync(group, ct);

        await db.SaveChangesAsync(ct);

        var holidayIds = createdHolidays.Select(h => h.Id).ToList();
        var dtos = await Project(db.Holidays.AsNoTracking()
                .Where(h => holidayIds.Contains(h.Id))
                .OrderBy(h => h.Date))
            .ToListAsync(ct);

        return new HolidayImpactDto(dtos, skippedCount, allAffectedGroups.Count, allAffectedSessions.Count);
    }

    public async Task DeleteAsync(long id, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var holiday = await db.Holidays.AsTracking().FirstOrDefaultAsync(h => h.Id == id, ct)
            ?? throw new NotFoundException(nameof(Holiday), id);

        db.Holidays.Remove(holiday);
        await db.SaveChangesAsync(ct);
    }

    // ================================================================= ruxsat

    private static void EnsureCanManage(User actor)
    {
        if (actor.Role is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Bayram kalendarini faqat o'quv bo'limi yoki admin boshqaradi.");
    }

    private async Task<User> LoadActorAsync(long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return actor;
    }

    // ================================================================= ichki yordamchi

    private IQueryable<HolidayDto> Project(IQueryable<Holiday> rows) =>
        rows.Select(h => new HolidayDto(
            h.Id,
            h.Date,
            h.Label,
            h.CreatedById,
            db.Users.Where(u => u.Id == h.CreatedById).Select(u => u.FullName).FirstOrDefault(),
            h.CreatedAt));

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private const int MaxRangeDays = 31;
    private const string StartDateField = "startDate";
    private const string EndDateField = "endDate";
    private const string LabelField = "label";
}
