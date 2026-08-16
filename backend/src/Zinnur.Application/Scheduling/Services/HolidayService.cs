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

        if (request.Date == default)
            throw Invalid(DateField, "Bayram sanasini kiriting.");

        var label = (request.Label ?? string.Empty).Trim();
        if (label.Length == 0)
            throw Invalid(LabelField, "Bayram nomini kiriting.");

        if (label.Length > Holiday.MaxLabelLength)
            throw Invalid(LabelField, "Bayram nomi juda uzun.");

        if (await db.Holidays.AsNoTracking().AnyAsync(h => h.Date == request.Date, ct))
            throw new ConflictException("Bu sana allaqachon bayram sifatida belgilangan.");

        var holiday = new Holiday { Date = request.Date, Label = label, CreatedById = actorId };
        holiday.Validate();
        db.Holidays.Add(holiday);

        // ── SHU KUNGA TO'G'RI KELADIGAN BARCHA guruhlarning darsini topamiz ──
        //
        // Mahalliy kun chegaralari -> UTC oralig'i: `ScheduledStart` bazada
        // UTC saqlanadi, ya'ni to'g'ridan-to'g'ri `DateOnly` bilan solishtirib
        // bo'lmaydi (`LiveSessionService.GetCalendarAsync` dagi bilan AYNI
        // naqsh).
        var zone = timeZone.TimeZone;
        var dayStart = LocalWallClock.StartOfDayUtc(request.Date, zone);
        var dayEnd = LocalWallClock.StartOfDayUtc(request.Date.AddDays(1), zone);

        var affectedSessions = await db.LiveSessions
            .AsTracking()
            .Include(s => s.Group)
            .Where(s => s.Status == SessionStatus.Scheduled
                     && s.ScheduledStart >= dayStart && s.ScheduledStart < dayEnd)
            .ToListAsync(ct);

        var now = clock.GetUtcNow();
        var reason = "Bayram: " + label;

        foreach (var session in affectedSessions)
            session.Cancel(reason, now);

        // ── HAR TA'SIRLANGAN GURUH UCHUN JADVAL QAYTA TUZILADI ──
        //
        // `ScheduleService.RegenerateAsync` allaqachon `Holidays` jadvalini
        // (shu orqali yangi yozilgan bayramni ham) `ExcludedDatesAsync` bilan
        // o'qiydi, ya'ni bekor qilingan darsning o'rniga oxiriga BITTA
        // qo'shimcha dars AVTOMATIK qo'shiladi — alohida "dars qo'shish"
        // kodi shart emas (izoh: `ScheduleGenerator.Build`).
        var affectedGroups = affectedSessions
            .Select(s => s.Group!)
            .DistinctBy(g => g.Id)
            .ToList();

        foreach (var group in affectedGroups)
            await schedule.RegenerateAsync(group, ct);

        await db.SaveChangesAsync(ct);

        var dto = await GetDtoAsync(holiday.Id, ct);
        return new HolidayImpactDto(dto, affectedGroups.Count, affectedSessions.Count);
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

    private async Task<HolidayDto> GetDtoAsync(long id, CancellationToken ct) =>
        await Project(db.Holidays.AsNoTracking().Where(h => h.Id == id)).FirstAsync(ct);

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

    private const string DateField = "date";
    private const string LabelField = "label";
}
