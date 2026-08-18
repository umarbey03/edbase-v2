using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Attrition.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Attrition.Services;

/// <inheritdoc cref="IAttritionReasonService"/>
public sealed class AttritionReasonService(
    IApplicationDbContext db,
    TimeProvider clock) : IAttritionReasonService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AttritionReasonDto>> ListAsync(
        bool activeOnly, long actorId, CancellationToken ct = default)
    {
        await EnsureCanManageAsync(actorId, ct);

        var rows = db.AttritionReasons.AsNoTracking();

        if (activeOnly) rows = rows.Where(r => r.IsActive);

        return await rows
            .OrderBy(r => r.Label)
            .Select(r => new AttritionReasonDto(
                r.Id,
                r.Label,
                r.IsActive,
                db.GroupMembershipEvents.Count(e => e.ReasonId == r.Id)))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AttritionReasonDto> CreateAsync(
        SaveAttritionReasonRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureCanManageAsync(actorId, ct);
        await EnsureLabelFreeAsync(request.Label, excludeId: null, ct);

        var reason = new AttritionReason { IsActive = request.IsActive };
        reason.Apply(request.Label);

        db.AttritionReasons.Add(reason);
        await db.SaveChangesAsync(ct);

        return await GetDtoAsync(reason.Id, ct);
    }

    /// <inheritdoc />
    public async Task<AttritionReasonDto> UpdateAsync(
        long id, SaveAttritionReasonRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureCanManageAsync(actorId, ct);

        var reason = await db.AttritionReasons.AsTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException(nameof(AttritionReason), id);

        await EnsureLabelFreeAsync(request.Label, excludeId: id, ct);

        reason.Apply(request.Label);
        reason.IsActive = request.IsActive;
        reason.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(ct);

        return await GetDtoAsync(id, ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long id, long actorId, CancellationToken ct = default)
    {
        await EnsureCanManageAsync(actorId, ct);

        var reason = await db.AttritionReasons.AsTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException(nameof(AttritionReason), id);

        var used = await db.GroupMembershipEvents.AnyAsync(e => e.ReasonId == id, ct);

        if (used)
        {
            // ★ O'CHIRISH EMAS, ARXIVLASH: hodisa jurnali FAQAT QO'SHILADI
            //   va bu qatorga havola qiladi. Yo'qolsa, o'tgan oyning
            //   foiz hisoboti "nomsiz" ulushga aylanardi.
            reason.IsActive = false;
            reason.UpdatedAt = clock.GetUtcNow();
        }
        else
        {
            db.AttritionReasons.Remove(reason);
        }

        await db.SaveChangesAsync(ct);
    }

    // ================================================================= yordamchi

    private async Task<AttritionReasonDto> GetDtoAsync(long id, CancellationToken ct) =>
        await db.AttritionReasons.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new AttritionReasonDto(
                r.Id,
                r.Label,
                r.IsActive,
                db.GroupMembershipEvents.Count(e => e.ReasonId == r.Id)))
            .FirstAsync(ct);

    /// <summary>
    /// Nom takrorlanmasin — KATTA/KICHIK HARF FARQISIZ (bazadagi unikal
    /// indeks aynan mos kelishni tekshiradi, "Moliyaviy" va "moliyaviy"
    /// esa operator uchun bitta sabab).
    /// </summary>
    private async Task EnsureLabelFreeAsync(string? label, long? excludeId, CancellationToken ct)
    {
        var trimmed = (label ?? string.Empty).Trim();

        if (trimmed.Length == 0) return; // Domen o'zi rad etadi.

        // `Like` — joker belgisiz, ya'ni AYNAN teng (loyihadagi AYNI naqsh:
        // `==` bo'lsa tahlilchi xotirada solishtirishni taklif qilardi).
        var lowered = trimmed.ToLowerInvariant()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

#pragma warning disable CA1304, CA1311
        var exists = await db.AttritionReasons
            .AnyAsync(r => EF.Functions.Like(r.Label.ToLower(), lowered)
                && (excludeId == null || r.Id != excludeId), ct);
#pragma warning restore CA1304, CA1311

        if (exists)
            throw new ConflictException("Bunday nomli sabab allaqachon mavjud.");
    }

    private async Task EnsureCanManageAsync(long actorId, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => (UserRole?)u.Role)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (role is not (UserRole.Admin or UserRole.Academic))
            throw new ForbiddenException("To'kilish sabablarini o'quv bo'limi va administrator boshqaradi.");
    }
}
