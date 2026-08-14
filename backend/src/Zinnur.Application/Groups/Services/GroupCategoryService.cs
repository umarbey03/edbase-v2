using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Groups.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Groups.Services;

/// <summary>
/// ========================================================================
/// GURUH KATEGORIYALARI LUG'ATI (R21b) — CRUD
/// ========================================================================
///
/// HTTP haqida HECH NARSA bilmaydi — faqat Application/Domain xatolarini
/// ko'taradi (loyihadagi umumiy naqsh).
///
/// ── RUXSAT: O'QISH KENG, YOZISH TOR ────────────────────────────────────
///
/// O'QIY OLADI: ustoz, kurator, o'quv bo'limi, admin. Sabab amaliy — bu
/// lug'at guruhlar ro'yxatidagi VA chatlar ro'yxatidagi FILTR tanlagichini
/// to'ldiradi (R21b + R38), ya'ni ustoz uni ko'ra olmasa o'z ekranidagi
/// filtrni ishlata olmasdi.
///
/// YOZA OLADI: faqat o'quv bo'limi va admin — <c>GroupService.EnsureCanManage</c>
/// bilan AYNAN bir xil qoida. Ustoz markazning yo'nalishlar ro'yxatini
/// o'zgartirmaydi.
///
/// O'QUVCHI umuman kira olmaydi: uning ekranlarida kategoriya filtri yo'q
/// (sabab `GroupChatThreadList` izohida) va lug'atni ochish "bizda qanday
/// yo'nalishlar bor" degan ichki ma'lumotni tarqatardi.
/// </summary>
public sealed class GroupCategoryService(IApplicationDbContext db) : IGroupCategoryService
{
    // ================================================================= o'qish

    public async Task<IReadOnlyList<GroupCategoryDto>> ListAsync(
        GroupCategoryListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanBrowse(actor);

        var rows = db.GroupCategories.AsNoTracking();

        if (query.IsActive is { } isActive)
            rows = rows.Where(c => c.IsActive == isActive);

        return await Project(rows.OrderBy(c => c.Position).ThenBy(c => c.Id)).ToListAsync(ct);
    }

    // ================================================================= yozish

    public async Task<GroupCategoryDto> CreateAsync(
        CreateGroupCategoryRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var name = RequireName(request.Name);
        await EnsureNameFreeAsync(name, exceptId: null, ct);

        var category = new GroupCategory
        {
            Name = name,
            IsActive = request.IsActive,

            // ★ MAX + 1, Count EMAS. `CourseService.NextPositionAsync` bilan
            // AYNI sabab: tartib zich bo'lmagan ma'lumotda (qo'lda kiritilgan
            // seed, o'chirilgan qator) `Count` MAVJUD raqamga tushib qolardi
            // va ikki kategoriya bir joyda turardi.
            Position = await NextPositionAsync(ct),
        };

        category.Validate();

        db.GroupCategories.Add(category);
        await SaveWithUniqueGuardAsync(ct);

        return await GetDtoAsync(category.Id, ct);
    }

    public async Task<GroupCategoryDto> UpdateAsync(
        long id, UpdateGroupCategoryRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var category = await db.GroupCategories.AsTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(GroupCategory), id);

        var name = RequireName(request.Name);
        await EnsureNameFreeAsync(name, exceptId: id, ct);

        category.Name = name;

        // `Position` ATAYLAB tegilmaydi — `UpdateCourseRequest` bilan AYNI
        // kelishuv: tartib alohida amalning ishi, tahrirlash formasi uni
        // jimgina o'zgartirmasin.
        category.IsActive = request.IsActive;

        category.Validate();

        await SaveWithUniqueGuardAsync(ct);

        return await GetDtoAsync(id, ct);
    }

    public async Task DeleteAsync(long id, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var category = await db.GroupCategories.AsTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(GroupCategory), id);

        // ========================================================
        // 🔴 BIRIKTIRILGAN GURUH BO'LSA — 409
        // ========================================================
        //
        // FK `ON DELETE SET NULL`, ya'ni bu yerda to'sib qo'yilmasa
        // o'chirish MUVAFFAQIYATLI tugaydi va o'nlab guruh jimgina
        // yorliqsiz qoladi. Foydalanuvchi buni faqat keyin, filtr bo'sh
        // natija berganda sezardi — va nima bo'lganini bilmasdi.
        //
        // ★ SetNull'ning O'ZI baribir to'g'ri tanlov: u KUTILMAGAN
        // yo'llardan (bevosita SQL, kelajakdagi ommaviy amal) kelgan
        // o'chirishda guruhni saqlab qoladi. Bu tekshiruv esa ODATIY
        // yo'lni tushunarli qiladi. `CourseService.DeleteAsync` da AYNI
        // juftlik bor va matni ham shu uslubda.
        var groupCount = await db.Groups.AsNoTracking().CountAsync(g => g.CategoryId == id, ct);

        if (groupCount > 0)
        {
            throw new ConflictException(
                "Bu kategoriyaga " + groupCount.ToString(CultureInfo.InvariantCulture)
                + " ta guruh biriktirilgan — o'chirib bo'lmaydi: ular yorliqsiz qolardi. "
                + "Yangi guruhlarga taklif qilinmasligi uchun kategoriyani ARXIVLANG "
                + "(faollikni o'chiring) — mavjud guruhlarda u ko'rinib turaveradi.");
        }

        db.GroupCategories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    // ================================================================= ruxsat

    /// <summary>Lug'atni umuman ko'ra oladimi (o'quvchi ko'ra olmaydi).</summary>
    private static void EnsureCanBrowse(User actor)
    {
        if (actor.Role is UserRole.Student)
            throw new ForbiddenException("Guruh kategoriyalariga ruxsatingiz yo'q.");
    }

    /// <summary>
    /// <c>GroupService.EnsureCanManage</c> ning AYNAN nusxasi va bu ataylab:
    /// kategoriya guruhning maydoni, ya'ni uni o'zgartira oladigan odam
    /// guruhni ham o'zgartira oladigan odam bo'lishi kerak. Ikki xil qoida
    /// bo'lsa ustoz yorliqni tahrirlab, guruhni tahrirlay olmasdi.
    /// </summary>
    private static void EnsureCanManage(User actor)
    {
        if (actor.Role is not (UserRole.Admin or UserRole.Academic))
        {
            throw new ForbiddenException(
                "Guruh kategoriyalarini faqat o'quv bo'limi xodimi yoki "
                + "administrator o'zgartira oladi.");
        }
    }

    // ================================================================= ichki yordamchi

    private async Task<User> LoadActorAsync(long actorId, CancellationToken ct)
    {
        // Rol TOKEN'dan emas, BAZADAN (loyihadagi umumiy qoida): kirish
        // tokeni 15 daqiqa yashaydi va u vaqt ichida rol pasaytirilgan
        // bo'lishi mumkin.
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return actor;
    }

    private static string RequireName(string? name)
    {
        var value = name?.Trim();

        if (string.IsNullOrEmpty(value))
            throw Invalid(NameField, "Kategoriya nomi kiritilishi shart.");

        if (value.Length > GroupCategory.MaxNameLength)
            throw Invalid(NameField, "Kategoriya nomi juda uzun.");

        return value;
    }

    /// <summary>
    /// Nom BAND emasligini tekshiradi — REGISTR FARQLAMASDAN.
    ///
    /// ★ NIMA UCHUN AYNAN REGISTRSIZ: "IELTS" va "ielts" — bitta yo'nalish.
    /// Bazadagi unikal indeks ularni IKKI xil qator deb qabul qiladi
    /// (Postgres'da tenglik registrga sezgir), ya'ni tanlagichda ikkita
    /// bir xil ko'ringan band paydo bo'lardi va guruhlar ular orasida
    /// bo'linib ketardi — filtr esa doim yarmini ko'rsatardi.
    ///
    /// ⚠️ TEKSHIRUV BILAN YOZUV ORASIDA poyga bor: shuning uchun bu FAQAT
    /// tushunarli xato uchun, oxirgi himoya esa unikal indeks
    /// (<c>SaveWithUniqueGuardAsync</c>).
    /// </summary>
    private async Task EnsureNameFreeAsync(string name, long? exceptId, CancellationToken ct)
    {
        // `ToLower()` .NET satrida ISHLAMAYDI — u ifoda daraxti ichida va EF
        // uni Postgres'ning `lower()` ga aylantiradi. `ToLowerInvariant()` ni
        // EF tarjima QILA OLMAYDI, shuning uchun globalizatsiya analizatori
        // shu blokda ataylab o'chirilgan (`GroupService.ApplySearch` bilan
        // AYNI naqsh).
        //
        // ⚠️ CA1862 HAM O'CHIRILGAN va bu MAJBURIY: analizator
        // `string.Equals(..., StringComparison.OrdinalIgnoreCase)` ni taklif
        // qiladi, lekin EF Core uni SQL'ga TARJIMA QILA OLMAYDI — so'rov
        // jimgina mijoz tomonida bajarilishga o'tardi (yoki tarjima xatosi
        // bilan yiqilardi), ya'ni butun jadval har tekshiruvda xotiraga
        // tortilardi.
#pragma warning disable CA1304, CA1311, CA1862
        var lowered = name.ToLowerInvariant();

        var taken = await db.GroupCategories.AsNoTracking()
            .AnyAsync(c => c.Id != exceptId && c.Name.ToLower() == lowered, ct);
#pragma warning restore CA1304, CA1311, CA1862

        if (taken)
            throw new ConflictException("Bunday nomli kategoriya allaqachon mavjud.");
    }

    /// <summary>Oxirgi tartib raqamidan keyingisi (MAX + 1, bo'sh jadvalda 0).</summary>
    private async Task<int> NextPositionAsync(CancellationToken ct)
    {
        var max = await db.GroupCategories.AsNoTracking()
            .Select(c => (int?)c.Position)
            .MaxAsync(ct);

        return max is { } value ? value + 1 : 0;
    }

    /// <summary>
    /// Unikal indeks buzilishini tushunarli 409 ga aylantiradi. Tekshiruv
    /// bilan yozuv orasida boshqa so'rov ulgurib qolishi mumkin — indeks
    /// oxirgi (va ishonchli) himoya.
    /// </summary>
    private async Task SaveWithUniqueGuardAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            throw new ConflictException("Bunday nomli kategoriya allaqachon mavjud.", ex);
        }
    }

    private async Task<GroupCategoryDto> GetDtoAsync(long id, CancellationToken ct) =>
        await Project(db.GroupCategories.AsNoTracking().Where(c => c.Id == id))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(GroupCategory), id);

    /// <summary>
    /// Kategoriya -> DTO. Guruhlar soni BAZADA sanaladi — aks holda ro'yxatning
    /// har qatori uchun alohida so'rov ketardi (N+1).
    /// </summary>
    private IQueryable<GroupCategoryDto> Project(IQueryable<GroupCategory> rows) =>
        rows.Select(c => new GroupCategoryDto(
            c.Id,
            c.Name,
            c.Position,
            c.IsActive,
            db.Groups.Count(g => g.CategoryId == c.Id),
            c.CreatedAt,
            c.UpdatedAt));

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    /// <summary>
    /// `problem.errors` kaliti — JSON maydon nomi bilan AYNAN bir xil
    /// (camelCase), aks holda frontend xatoni maydon yoniga qo'ya olmasdi.
    /// </summary>
    private const string NameField = "name";
}
