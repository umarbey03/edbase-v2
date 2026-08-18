using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Penalties.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Penalties.Services;

/// <inheritdoc cref="IPenaltyCategoryService"/>
public sealed class PenaltyCategoryService(
    IApplicationDbContext db,
    TimeProvider clock) : IPenaltyCategoryService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PenaltyCategoryDto>> ListAsync(
        bool activeOnly, long actorId, CancellationToken ct = default)
    {
        await EnsureCanViewAsync(actorId, ct);

        var rows = db.PenaltyCategories.AsNoTracking();

        if (activeOnly) rows = rows.Where(c => c.IsActive);

        var items = await rows
            .OrderBy(c => c.Label)
            .Select(c => new
            {
                c.Id,
                c.Label,
                c.Amount,
                c.PerUnit,
                c.UnitLabel,
                c.IsActive,
                c.SystemKey,
                // ★ SANOQ SO'ROV ICHIDA: o'chirish tugmasi bosilgunga
                //   qadar administrator "bu tarif 14 ta jarimada
                //   ishlatilgan" degan ogohlantirishni KO'RIB tursin.
                UsageCount = db.Penalties.Count(p => p.CategoryId == c.Id),
            })
            .ToListAsync(ct);

        return items.ConvertAll(c => new PenaltyCategoryDto(
            c.Id, c.Label, c.Amount, c.PerUnit, c.UnitLabel, c.IsActive,
            !string.IsNullOrEmpty(c.SystemKey), c.SystemKey, c.UsageCount));
    }

    /// <inheritdoc />
    public async Task<PenaltyCategoryDto> CreateAsync(
        SavePenaltyCategoryRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureCanManageAsync(actorId, ct);

        await EnsureLabelFreeAsync(request.Label, excludeId: null, ct);

        var category = new PenaltyCategory { IsActive = request.IsActive };
        category.Apply(request.Label, request.Amount, request.PerUnit, request.UnitLabel);

        db.PenaltyCategories.Add(category);
        await db.SaveChangesAsync(ct);

        return await GetDtoAsync(category.Id, ct);
    }

    /// <inheritdoc />
    public async Task<PenaltyCategoryDto> UpdateAsync(
        long id, SavePenaltyCategoryRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureCanManageAsync(actorId, ct);

        var category = await db.PenaltyCategories.AsTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(PenaltyCategory), id);

        await EnsureLabelFreeAsync(request.Label, excludeId: id, ct);

        category.Apply(request.Label, request.Amount, request.PerUnit, request.UnitLabel);

        // 🔴 TIZIM TARIFI DOIM FAOL: avtomatik jarima kodi uni kalit
        //    bo'yicha izlaydi. Arxivlashga ruxsat bersak, "nega
        //    kechikish jarimasi yozilmayapti?" degan jimgina nosozlik
        //    paydo bo'lardi. To'xtatish yo'li — summani `0` qilish.
        category.IsActive = category.IsSystem || request.IsActive;
        category.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(ct);

        return await GetDtoAsync(id, ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long id, long actorId, CancellationToken ct = default)
    {
        await EnsureCanManageAsync(actorId, ct);

        var category = await db.PenaltyCategories.AsTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(PenaltyCategory), id);

        if (category.IsSystem)
            throw new ConflictException("Tizim tarifini o'chirib bo'lmaydi. To'xtatish uchun summasini 0 qiling.");

        var used = await db.Penalties.AnyAsync(p => p.CategoryId == id, ct);

        if (used)
        {
            // ★ O'CHIRISH EMAS, ARXIVLASH: yozilgan jarimalar shu
            //   tarifga havola qiladi. Qator yo'qolsa, o'tgan oyning
            //   hisoboti "nomsiz" qatorlarga to'lib ketardi.
            category.IsActive = false;
            category.UpdatedAt = clock.GetUtcNow();
        }
        else
        {
            db.PenaltyCategories.Remove(category);
        }

        await db.SaveChangesAsync(ct);
    }

    // ================================================================= yordamchi

    private async Task<PenaltyCategoryDto> GetDtoAsync(long id, CancellationToken ct)
    {
        var c = await db.PenaltyCategories.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.Label,
                x.Amount,
                x.PerUnit,
                x.UnitLabel,
                x.IsActive,
                x.SystemKey,
                UsageCount = db.Penalties.Count(p => p.CategoryId == x.Id),
            })
            .FirstAsync(ct);

        return new PenaltyCategoryDto(
            c.Id, c.Label, c.Amount, c.PerUnit, c.UnitLabel, c.IsActive,
            !string.IsNullOrEmpty(c.SystemKey), c.SystemKey, c.UsageCount);
    }

    /// <summary>
    /// Nom takrorlanmasin — KATTA/KICHIK HARF FARQISIZ.
    ///
    /// Bazadagi unikal indeks aynan mos kelishni tekshiradi; "Kechikish"
    /// va "kechikish" esa operator uchun BIR XIL nom va ro'yxatda ikki
    /// marta chiqsa qaysi birini tanlashi noaniq bo'lardi.
    /// </summary>
    private async Task EnsureLabelFreeAsync(string? label, long? excludeId, CancellationToken ct)
    {
        var trimmed = (label ?? string.Empty).Trim();

        if (trimmed.Length == 0) return; // Domen o'zi rad etadi.

        // `Like` — joker belgisiz, ya'ni AYNAN teng. Loyihada matn
        // solishtiruvi shu tarzda yoziladi (`==` bo'lsa tahlilchi
        // xotirada solishtirishni taklif qilardi, bu esa SQL'ga
        // tarjima qilinmasdi).
        var lowered = trimmed.ToLowerInvariant()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

#pragma warning disable CA1304, CA1311
        var exists = await db.PenaltyCategories
            .AnyAsync(c => EF.Functions.Like(c.Label.ToLower(), lowered)
                && (excludeId == null || c.Id != excludeId), ct);
#pragma warning restore CA1304, CA1311

        if (exists)
            throw new ConflictException("Bunday nomli tarif allaqachon mavjud.");
    }

    private async Task EnsureCanViewAsync(long actorId, CancellationToken ct)
    {
        if (await RoleOfAsync(actorId, ct) is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Jarima tariflarini faqat o'quv bo'limi va administrator ko'radi.");
    }

    /// <summary>
    /// Tariflarni O'QUV BO'LIMI HAM boshqaradi (loyiha egasi qarori,
    /// 2026-08-18) — katalog "Sozlamalar" sahifasining bo'limi, va u
    /// sahifa allaqachon o'quv bo'limiga ochiq.
    ///
    /// ★ NEGA JARIMANI TASDIQLASHDAN FARQLI: tarif — QOIDA, jarima esa
    /// AYNI ODAMDAN ushlab qolinadigan PUL. Tarifni o'zgartirish hech
    /// kimning oyligiga darhol tegmaydi (yozilgan jarimalarda summa
    /// muzlatilgan), shuning uchun bu yerda "kim yozgan bo'lsa, u
    /// tasdiqlamasin" cheklovi kerak emas.
    /// </summary>
    private async Task EnsureCanManageAsync(long actorId, CancellationToken ct)
    {
        if (await RoleOfAsync(actorId, ct) is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("Jarima tariflarini o'quv bo'limi va administrator o'zgartiradi.");
    }

    private async Task<UserRole> RoleOfAsync(long actorId, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(ct);

        return role ?? throw new NotFoundException(nameof(User), actorId);
    }
}
