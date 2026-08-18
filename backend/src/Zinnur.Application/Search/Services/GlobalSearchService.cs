using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Search.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Search.Services;

/// <inheritdoc cref="IGlobalSearchService"/>
public sealed class GlobalSearchService(IApplicationDbContext db) : IGlobalSearchService
{
    /// <summary>
    /// Shu uzunlikdan qisqa so'rov bajarilMAYDI.
    ///
    /// ★ SABAB: bitta harf deyarli har yozuvga mos keladi va foydali
    /// natija bermaydi, lekin bazaga to'liq skan yuklaydi. Trigram
    /// indeksi ham qisqa naqshda samarasiz.
    /// </summary>
    public const int MinQueryLength = 2;

    private const int MaxLimit = 20;

    /// <summary>
    /// Bazadan limitdan NECHA BAROBAR ko'p olinadi.
    ///
    /// Saralash XOTIRADA bajariladi (moslik darajasi bo'yicha), shuning
    /// uchun "eng yaxshi 5 ta" ni tanlash uchun 5 tadan ko'proq nomzod
    /// kerak. Aks holda bazaning alifbo tartibi g'olibni belgilardi.
    /// </summary>
    private const int CandidateFactor = 4;

    /// <inheritdoc />
    public async Task<GlobalSearchResultDto> SearchAsync(
        GlobalSearchQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var raw = (query.Q ?? string.Empty).Trim();
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);

        if (raw.Length < MinQueryLength)
            return new GlobalSearchResultDto(raw, null, []);

        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (role is UserRole.Student)
            throw new ForbiddenException("Global qidiruv xodimlar uchun.");

        var lowered = raw.ToLowerInvariant();
        var pattern = "%" + EscapeLike(lowered) + "%";
        var take = limit * CandidateFactor;

        // Ustoz/kurator FAQAT o'z guruhlarini ko'radi (sabab interfeys izohida).
        var restricted = role is UserRole.Teacher or UserRole.Assistant;

        var ownGroupIds = restricted
            ? await db.Groups.AsNoTracking()
                .Where(g => g.TeacherId == actorId || g.AssistantId == actorId)
                .Select(g => g.Id)
                .ToListAsync(ct)
            : [];

        var groups = new List<SearchGroupDto>();

        void Add(SearchGroupDto group)
        {
            // Bo'sh va xatosiz bo'lim ko'rsatilmaydi — natijasiz sarlavhalar
            // ro'yxatni shovqin bilan to'ldirardi.
            if (group.Items.Count > 0 || group.Error is not null) groups.Add(group);
        }

        if (Wants(query.Type, "users"))
            Add(await SafeAsync("users", "Foydalanuvchilar",
                ct2 => SearchUsersAsync(pattern, lowered, take, limit, restricted, ownGroupIds, ct2), ct));

        if (Wants(query.Type, "groups"))
            Add(await SafeAsync("groups", "Guruhlar",
                ct2 => SearchGroupsAsync(pattern, lowered, take, limit, restricted, ownGroupIds, ct2), ct));

        // Kurs/test/vazifa — KONTENT, ya'ni guruhga bog'liq emas va
        // barcha xodimlarga ochiq (ro'yxat ekranlaridagi AYNI qoida).
        if (Wants(query.Type, "courses"))
            Add(await SafeAsync("courses", "Kurslar",
                ct2 => SearchCoursesAsync(pattern, lowered, take, limit, ct2), ct));

        if (Wants(query.Type, "tests"))
            Add(await SafeAsync("tests", "Testlar",
                ct2 => SearchTestsAsync(pattern, lowered, take, limit, ct2), ct));

        if (Wants(query.Type, "assignments"))
            Add(await SafeAsync("assignments", "Uy vazifalari",
                ct2 => SearchAssignmentsAsync(pattern, lowered, take, limit, ct2), ct));

        // ★ ENG MOS NATIJA — BARCHA TURLAR BO'YLAB: guruhlangan ro'yxatda
        //   aniq mos kelgan ism pastda qolib ketishi mumkin (sabab DTO
        //   izohida). Enter bosilganda aynan shu ochiladi.
        var topHit = groups
            .SelectMany(g => g.Items)
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Title.Length)
            .FirstOrDefault();

        return new GlobalSearchResultDto(raw, topHit, groups);
    }

    // ================================================================= turlar

    private async Task<(List<SearchHitDto> Items, int Total)> SearchUsersAsync(
        string pattern, string lowered, int take, int limit,
        bool restricted, List<long> ownGroupIds, CancellationToken ct)
    {
        var rows = db.Users.AsNoTracking().Where(u => u.IsActive);

        if (restricted)
        {
            // Ustoz o'z guruhidagi O'QUVCHILARNI topa oladi, boshqa
            // xodimlar yoki begona o'quvchilarni EMAS.
            rows = rows.Where(u => u.Role == UserRole.Student
                && db.GroupMembers.Any(m => m.StudentId == u.Id && ownGroupIds.Contains(m.GroupId)));
        }

#pragma warning disable CA1304, CA1311
        rows = rows.Where(u =>
            EF.Functions.Like(u.FullName.ToLower(), pattern)
            || (u.Phone != null && EF.Functions.Like(u.Phone, pattern)));
#pragma warning restore CA1304, CA1311

        var total = await rows.CountAsync(ct);

        var found = await rows
            .OrderBy(u => u.FullName)
            .Take(take)
            .Select(u => new { u.Id, u.FullName, u.Phone, u.Role })
            .ToListAsync(ct);

        var items = found
            .Select(u => new SearchHitDto(
                "users", u.Id, u.FullName, u.Phone, RoleLabel(u.Role), Score(u.FullName, lowered)))
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Title, StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        return (items, total);
    }

    private async Task<(List<SearchHitDto> Items, int Total)> SearchGroupsAsync(
        string pattern, string lowered, int take, int limit,
        bool restricted, List<long> ownGroupIds, CancellationToken ct)
    {
        var rows = db.Groups.AsNoTracking();

        if (restricted) rows = rows.Where(g => ownGroupIds.Contains(g.Id));

#pragma warning disable CA1304, CA1311
        rows = rows.Where(g => EF.Functions.Like(g.Name.ToLower(), pattern));
#pragma warning restore CA1304, CA1311

        var total = await rows.CountAsync(ct);

        var found = await rows
            .OrderBy(g => g.Name)
            .Take(take)
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.IsActive,
                TeacherName = g.TeacherId == null
                    ? null
                    : db.Users.Where(u => u.Id == g.TeacherId).Select(u => u.FullName).FirstOrDefault(),
                // ★ KURATOR GURUHI HISOBGA OLINADI (2026-08-18) — ikki
                //   shox `GroupMembershipScope` dagi AYNI qoida (bu yerda
                //   `g.Id` USTUN bo'lgani uchun qo'lda yozilgan). Ilgari
                //   guruhlar ro'yxati "22 o'quvchi" der, qidiruv esa AYNI
                //   guruh uchun "0 o'quvchi" derdi.
                Members = db.GroupMembers.Count(m =>
                    (m.GroupId == g.Id || m.Group!.CuratorGroupId == g.Id)
                    && m.Status == MemberStatus.Active),
            })
            .ToListAsync(ct);

        var items = found
            .Select(g => new SearchHitDto(
                "groups",
                g.Id,
                g.Name,
                g.TeacherName,
                g.IsActive ? $"{g.Members} o'quvchi" : "Arxiv",
                Score(g.Name, lowered)))
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Title, StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        return (items, total);
    }

    private async Task<(List<SearchHitDto> Items, int Total)> SearchCoursesAsync(
        string pattern, string lowered, int take, int limit, CancellationToken ct)
    {
#pragma warning disable CA1304, CA1311
        var rows = db.Courses.AsNoTracking()
            .Where(c => EF.Functions.Like(c.Name.ToLower(), pattern));
#pragma warning restore CA1304, CA1311

        var total = await rows.CountAsync(ct);

        var found = await rows
            .OrderBy(c => c.Name)
            .Take(take)
            .Select(c => new { c.Id, c.Name, c.IsActive })
            .ToListAsync(ct);

        var items = found
            .Select(c => new SearchHitDto(
                "courses", c.Id, c.Name, null, c.IsActive ? null : "Nofaol", Score(c.Name, lowered)))
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Title, StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        return (items, total);
    }

    private async Task<(List<SearchHitDto> Items, int Total)> SearchTestsAsync(
        string pattern, string lowered, int take, int limit, CancellationToken ct)
    {
#pragma warning disable CA1304, CA1311
        var rows = db.Tests.AsNoTracking()
            .Where(t => EF.Functions.Like(t.Title.ToLower(), pattern));
#pragma warning restore CA1304, CA1311

        var total = await rows.CountAsync(ct);

        var found = await rows
            .OrderBy(t => t.Title)
            .Take(take)
            .Select(t => new { t.Id, t.Title })
            .ToListAsync(ct);

        var items = found
            .Select(t => new SearchHitDto("tests", t.Id, t.Title, null, null, Score(t.Title, lowered)))
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Title, StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        return (items, total);
    }

    private async Task<(List<SearchHitDto> Items, int Total)> SearchAssignmentsAsync(
        string pattern, string lowered, int take, int limit, CancellationToken ct)
    {
#pragma warning disable CA1304, CA1311
        var rows = db.Assignments.AsNoTracking()
            .Where(a => EF.Functions.Like(a.Title.ToLower(), pattern));
#pragma warning restore CA1304, CA1311

        var total = await rows.CountAsync(ct);

        var found = await rows
            .OrderByDescending(a => a.Id)
            .Take(take)
            .Select(a => new
            {
                a.Id,
                a.Title,
                GroupName = a.GroupId == null
                    ? null
                    : db.Groups.Where(g => g.Id == a.GroupId).Select(g => g.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = found
            .Select(a => new SearchHitDto(
                "assignments", a.Id, a.Title, a.GroupName, null, Score(a.Title, lowered)))
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Title, StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        return (items, total);
    }

    // ================================================================= yordamchi

    /// <summary>
    /// Bitta turni XATODAN AJRATIB bajaradi.
    ///
    /// ★ NEGA `catch` KENG: qidiruv YORDAMCHI vosita. Bitta turdagi
    /// nosozlik (indeks yo'q, ruxsat, vaqt tugashi) butun oynani
    /// o'chirib qo'ymasligi kerak — foydalanuvchi qolgan natijalarni
    /// baribir ko'radi va nima ishlamaganini biladi.
    /// </summary>
    private static async Task<SearchGroupDto> SafeAsync(
        string type,
        string label,
        Func<CancellationToken, Task<(List<SearchHitDto> Items, int Total)>> run,
        CancellationToken ct)
    {
        try
        {
            var (items, total) = await run(ct);

            return new SearchGroupDto(type, label, items, total, null);
        }
        catch (OperationCanceledException)
        {
            // Foydalanuvchi yozishda davom etdi — bu xato EMAS.
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return new SearchGroupDto(type, label, [], 0, ex.Message);
        }
    }

    private static bool Wants(string? filter, string type) =>
        string.IsNullOrWhiteSpace(filter)
        || string.Equals(filter, type, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Moslik og'irligi: AYNAN teng &gt; boshlanishiga mos &gt; so'z
    /// boshiga mos &gt; ichida bor.
    ///
    /// ★ NEGA SQL'DA EMAS: bu tartib to'rt shartli va uni har turdagi
    /// so'rovga yozish kerak bo'lardi. Nomzodlar soni kichik (limitdan
    /// bir necha barobar), shuning uchun xotirada hisoblash arzon va
    /// qoida BITTA joyda qoladi.
    /// </summary>
    private static int Score(string value, string lowered)
    {
        var text = value.ToLowerInvariant();

        if (string.Equals(text, lowered, StringComparison.Ordinal)) return 100;
        if (text.StartsWith(lowered, StringComparison.Ordinal)) return 80;

        // "Ali" so'rovi "Sardor Aliyev" da familiya boshiga mos keladi —
        // bu shunchaki matn ichida uchrashidan ancha kuchliroq signal.
        if (text.Contains(" " + lowered, StringComparison.Ordinal)) return 60;

        return 30;
    }

    private static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Student => "O'quvchi",
        UserRole.Teacher => "Ustoz",
        UserRole.Assistant => "Kurator",
        UserRole.Academic => "O'quv bo'limi",
        UserRole.Admin => "Administrator",
        _ => role.ToString(),
    };

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);
}
