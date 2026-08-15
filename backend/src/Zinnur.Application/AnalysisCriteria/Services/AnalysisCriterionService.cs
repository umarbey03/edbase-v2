using Microsoft.EntityFrameworkCore;
using Zinnur.Application.AnalysisCriteria.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.AnalysisCriteria.Services;

/// <summary><inheritdoc cref="IAnalysisCriterionService"/></summary>
public sealed class AnalysisCriterionService(
    IApplicationDbContext db, TimeProvider clock) : IAnalysisCriterionService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AnalysisCriterionDto>> ListAsync(CancellationToken ct = default) =>
        await db.AnalysisCriteria
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .Select(c => Map(c))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<AnalysisCriterionDto> CreateAsync(
        SaveAnalysisCriterionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var criterion = AnalysisCriterion.Create(
            request.Name, request.MaxScore, request.SortOrder, clock.GetUtcNow());

        db.AnalysisCriteria.Add(criterion);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Map(criterion);
    }

    /// <inheritdoc />
    public async Task<AnalysisCriterionDto> UpdateAsync(
        long id, SaveAnalysisCriterionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var criterion = await db.AnalysisCriteria
            .AsTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(AnalysisCriterion), id);

        criterion.Edit(request.Name, request.MaxScore, request.SortOrder, clock.GetUtcNow());
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Map(criterion);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var criterion = await db.AnalysisCriteria
            .AsTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);

        // IDEMPOTENT: allaqachon o'chirilgan mezonga qayta so'rov —
        // ikki marta bosilgan tugma xato ko'rsatmasin (`SessionReviewService.DeleteAsync`
        // dagi AYNI qoida).
        if (criterion is null) return;

        db.AnalysisCriteria.Remove(criterion);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static AnalysisCriterionDto Map(AnalysisCriterion c) =>
        new(c.Id, c.Name, c.MaxScore, c.SortOrder);
}
