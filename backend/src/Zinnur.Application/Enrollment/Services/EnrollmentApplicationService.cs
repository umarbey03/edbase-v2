using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Enrollment.Dtos;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.Enrollment.Services;

/// <inheritdoc cref="IEnrollmentApplicationService"/>
public sealed class EnrollmentApplicationService(
    IApplicationDbContext db,
    ICacheService cache,
    TimeProvider clock,
    ILogger<EnrollmentApplicationService> logger) : IEnrollmentApplicationService
{
    /// <summary>
    /// Bitta raqamdan ikki ariza orasidagi eng qisqa oraliq.
    ///
    /// ★ NIMA UCHUN HTTP RATE-LIMIT YETARLI EMAS: u IP bo'yicha bo'linadi
    /// va reverse-proxy ortida HAMMA bitta bo'limga tushadi
    /// (<c>Program.cs</c> dagi ogohlantirish). Ya'ni bitta maktabdan
    /// kelgan uchinchi ariza to'silardi, IP almashtirgan bot esa
    /// bemalol o'tardi. Bu esa RAQAM bo'yicha va IP'ga umuman bog'liq
    /// emas (<c>PhoneLoginCodeStore.ResendCooldown</c> bilan AYNI
    /// falsafada).
    /// </summary>
    private static readonly TimeSpan SubmitCooldown = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Bitta raqamdan sutkada qabul qilinadigan ariza soni.
    ///
    /// 🔴 BUSIZ 10 daqiqalik oyna hujumni faqat SEKINLASHTIRARDI: sutkada
    /// 144 ta soxta ariza — bu o'quv bo'limi paneli ishlatib bo'lmaydigan
    /// holatga kelishi, ya'ni hujumchi maqsadiga erishishi.
    /// </summary>
    private const int MaxSubmitsPerDay = 3;

    /// <summary>Ro'yxatning eng katta sahifasi (bitta so'rov bazani bosmasin).</summary>
    private const int MaxPageSize = 100;

    /// <summary>Qidiruv uchun eng kam belgi soni.</summary>
    private const int MinSearchLength = 2;

    /// <inheritdoc />
    public async Task SubmitAsync(
        CreateEnrollmentApplicationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fullName = (request.FullName ?? string.Empty).Trim();
        var normalized = User.NormalizePhone(request.Phone);

        /*
          ★ TEKSHIRUV SHU YERDA HAM BOR (entity ichida ham bor):
            servis `ValidationException` (400) tashlaydi, `Apply` esa
            `DomainException` (409). Forma uchun to'g'ri javob — 400, va
            u maydon nomini ham beradi. Entity'dagi tekshiruv esa
            himoyaning ikkinchi qatlami bo'lib qoladi: kelajakda boshqa
            chaqiruvchi paydo bo'lsa, u yaroqsiz qatorni bazaga yoza
            olmaydi.
        */
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (fullName.Length < 2)
            errors["fullName"] = ["Ism va familiyangizni kiriting."];

        if (normalized is null)
            errors["phone"] = ["Telefon raqamini to'g'ri kiriting."];

        if (errors.Count > 0)
            throw new ValidationException(errors);

        // ══════════════════════════════════════════════════════════════
        // ★ KVOTA — RAQAM BO'YICHA VA U BAZAGA YOZISHDAN OLDIN.
        //
        // Tartib ataylab shunday: kvota tekshiruvi bazaga umuman
        // bormaydi, ya'ni toshqin holatida jadval qulflanmaydi.
        // ══════════════════════════════════════════════════════════════
        var slug = Slug(normalized!);

        // ATOMAR: `INCR` + birinchi oshirishda `PEXPIRE` (Lua skript).
        // Natija 1 bo'lsa — oyna AYNAN shu chaqiruvda ochildi.
        var recent = await cache
            .IncrementAsync(CooldownKey(slug), SubmitCooldown, ct)
            .ConfigureAwait(false);

        if (recent > 1)
        {
            throw new TooManyRequestsException(
                "Arizangiz yaqinda qabul qilindi. O'quv bo'limi tez orada bog'lanadi.",
                (int)SubmitCooldown.TotalSeconds);
        }

        var daily = await cache
            .IncrementAsync(DailyKey(slug), TimeSpan.FromDays(1), ct)
            .ConfigureAwait(false);

        if (daily > MaxSubmitsPerDay)
        {
            throw new TooManyRequestsException(
                "Bugun juda ko'p ariza yuborildi. Iltimos, ertaga urinib ko'ring "
                + "yoki markazga qo'ng'iroq qiling.",
                (int)TimeSpan.FromDays(1).TotalSeconds);
        }

        var application = new EnrollmentApplication { CreatedAt = clock.GetUtcNow() };
        application.Apply(fullName, request.Phone, request.Course, request.Note);

        db.EnrollmentApplications.Add(application);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // 🔴 ISM VA TELEFON LOGGA YOZILMAYDI: log Sentry'ga va konteyner
        //    oqimiga ketadi, u yerda esa shaxsiy ma'lumot kerak emas va
        //    uni keyin o'chirib bo'lmaydi.
        EnrollmentLog.Submitted(logger, application.Id);
    }

    /// <inheritdoc />
    public async Task<PagedResult<EnrollmentApplicationDto>> ListAsync(
        EnrollmentApplicationListParams request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var rows = db.EnrollmentApplications.AsNoTracking();

        if (request.Status is { } status)
            rows = rows.Where(a => a.Status == status);

        rows = ApplySearch(rows, request.Search);

        var total = await rows.CountAsync(ct).ConfigureAwait(false);

        // Eng yangisi yuqorida — indeks (`Status`, `CreatedAt`) aynan shu
        // so'rov uchun qo'yilgan. `Id` — barqaror ikkinchi tartib: bir
        // sekundda kelgan ikki ariza sahifalar orasida sakramasin.
        var items = await rows
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new EnrollmentApplicationDto(
                a.Id,
                a.FullName,
                a.Phone,
                a.Course,
                a.Note,
                a.Status,
                a.CreatedAt,
                a.HandledAt,
                a.HandledBy != null ? a.HandledBy.FullName : null,
                a.Comment))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<EnrollmentApplicationDto>(items, page, pageSize, total);
    }

    /// <inheritdoc />
    public async Task<EnrollmentApplicationDto> UpdateAsync(
        long id, UpdateEnrollmentApplicationRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await db.EnrollmentApplications
            .AsTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            .ConfigureAwait(false);

        if (application is null)
            throw new NotFoundException("Ariza", id);

        application.Handle(request.Status, request.Comment, actorId, clock.GetUtcNow());

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        EnrollmentLog.Handled(logger, application.Id, application.Status.ToString());

        // Ismni ALOHIDA so'raymiz: `HandledBy` navigatsiyasi yuklanmagan
        // (yozuv `actorId` bo'yicha qo'yildi) va uni `Include` bilan
        // tortish butun `User` qatorini keraksiz olib kelardi.
        var handledByName = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return new EnrollmentApplicationDto(
            application.Id,
            application.FullName,
            application.Phone,
            application.Course,
            application.Note,
            application.Status,
            application.CreatedAt,
            application.HandledAt,
            handledByName,
            application.Comment);
    }

    // ================================================================ ichki

    /// <summary>
    /// Ism yoki telefon bo'yicha qidiruv.
    /// </summary>
    /// <remarks>
    /// ★ ARIZALAR SONI KAM (kuniga o'nlab), shuning uchun bu yerda
    /// `UserService` dagi kabi trigramma indeksi va 3 belgilik chegara
    /// KERAK EMAS: 2 belgidan boshlab qidirsa ham Postgres jadvalni
    /// bir marta o'qib chiqadi, xolos.
    /// </remarks>
    private static IQueryable<EnrollmentApplication> ApplySearch(
        IQueryable<EnrollmentApplication> rows, string? search)
    {
        var trimmed = (search ?? string.Empty).Trim();

        if (trimmed.Length < MinSearchLength)
            return rows;

        var term = "%" + Escape(trimmed.ToLowerInvariant()) + "%";
        var digits = new string([.. trimmed.Where(char.IsAsciiDigit)]);

        // DIQQAT: `a.FullName.ToLower()` .NET satri ustida ISHLAMAYDI — u
        // ifoda daraxti ichida va EF uni Postgres'ning `lower()` iga
        // aylantiradi. `ToLowerInvariant()` ni EF tarjima QILA OLMAYDI,
        // shuning uchun globalizatsiya analizatori shu blokda ataylab
        // o'chirilgan (`UserService` dagi AYNI holat).
#pragma warning disable CA1304, CA1311
        if (digits.Length == 0)
            return rows.Where(a => EF.Functions.Like(a.FullName.ToLower(), term));

        var phoneTerm = "%" + digits + "%";

        return rows.Where(a =>
            EF.Functions.Like(a.FullName.ToLower(), term)
            || EF.Functions.Like(a.PhoneNormalized, phoneTerm));
#pragma warning restore CA1304, CA1311
    }

    /// <summary>LIKE metabelgilarini zararsizlantiradi (aks holda '%' butun jadvalni tortadi).</summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>
    /// Telefon raqamidan KALIT BO'LAGI.
    ///
    /// 🔴 Raqamning O'ZI kalitga tushmaydi: Redis kaliti loglarda,
    /// <c>SCAN</c> chiqishida va monitoring panellarida ochiq ko'rinadi
    /// (<c>PhoneLoginCodeStore.Slug</c> bilan AYNI sabab).
    /// </summary>
    private static string Slug(string phoneNormalized)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(phoneNormalized));
        return Convert.ToHexString(digest)[..16];
    }

    private static string CooldownKey(string slug) =>
        string.Create(CultureInfo.InvariantCulture, $"ariza:cooldown:{slug}");

    private static string DailyKey(string slug) =>
        string.Create(CultureInfo.InvariantCulture, $"ariza:daily:{slug}");
}

/// <summary>
/// Manba-generatsiyali loglar (CA1848).
///
/// 🔴 ISM VA TELEFON RAQAMI LOGGA YOZILMAYDI — faqat yozuv <c>Id</c> si.
/// Qo'llab-quvvatlash undan arizani panelda topa oladi.
/// </summary>
internal static partial class EnrollmentLog
{
    [LoggerMessage(
        EventId = 6340,
        Level = LogLevel.Information,
        Message = "Kursga ariza qabul qilindi. id={ApplicationId}")]
    internal static partial void Submitted(ILogger logger, long applicationId);

    [LoggerMessage(
        EventId = 6341,
        Level = LogLevel.Information,
        Message = "Ariza holati o'zgardi. id={ApplicationId} holat={Status}")]
    internal static partial void Handled(ILogger logger, long applicationId, string status);
}
