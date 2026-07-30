using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Messaging.Services;

/// <inheritdoc cref="ICuratorDirectory"/>
public sealed class CuratorDirectory(IApplicationDbContext db) : ICuratorDirectory
{
    /// <inheritdoc />
    public async Task<User?> ResolveCuratorAsync(long studentId, CancellationToken ct = default)
    {
        // BITTA so'rov: o'quvchining faol guruhlaridan kurator nomzodlari.
        //
        // Har guruh IKKI nomzod beradi va tartib MUHIM (eski tizimdagi
        // bilan bir xil):
        //   • birinchi navbatda guruhning O'Z kuratori (`AssistantId`);
        //   • u bo'lmasa — bog'langan kurator guruhining kuratori.
        //
        // Eski kod buni guruhlar bo'ylab siklda, har guruh uchun 1..2 ta
        // `db.get(User, ...)` chaqirig'i bilan qilardi — ya'ni klassik N+1.
        var candidates = await db.GroupMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId
                     && m.Status == MemberStatus.Active
                     && m.Group!.IsActive
                     && m.Group.Type == GroupType.Group)
            .OrderBy(m => m.JoinedAt)
            .ThenBy(m => m.Id)
            .Select(m => new CuratorCandidate(
                m.Group!.AssistantId,
                m.Group.CuratorGroup!.AssistantId))
            .ToListAsync(ct);

        var ordered = new List<long>(candidates.Count * 2);

        foreach (var candidate in candidates)
        {
            if (candidate.Direct is { } direct) ordered.Add(direct);
            if (candidate.ViaCuratorGroup is { } linked) ordered.Add(linked);
        }

        if (ordered.Count == 0) return null;

        var users = await db.Users.AsNoTracking()
            .Where(u => ordered.Contains(u.Id) && u.IsActive)
            .ToListAsync(ct);

        // Nomzodlar TARTIBI saqlanadi: birinchi FAOL nomzod g'olib.
        // (Bazadan kelgan tartib emas — u tasodifiy bo'lishi mumkin.)
        foreach (var id in ordered)
        {
            var match = users.Find(u => u.Id == id);
            if (match is not null) return match;
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> ScopeGroupIdsAsync(
        long staffId, CancellationToken ct = default)
    {
        // (1) Xodim bevosita kurator qilib biriktirilgan USTOZ guruhlari.
        // (2) Xodimning kurator guruhlariga BOG'LANGAN ustoz guruhlari.
        //
        // Ikkinchi shart uchun "mening kurator guruhlarim" ro'yxati kerak.
        // Uni ichki so'rov sifatida yozamiz — bitta borish-kelish yetadi.
        var myCuratorGroupIds = db.Groups.AsNoTracking()
            .Where(g => g.AssistantId == staffId)
            .Select(g => g.Id);

        return await db.Groups.AsNoTracking()
            .Where(g => g.Type == GroupType.Group
                     && g.IsActive
                     && (g.AssistantId == staffId
                         || (g.CuratorGroupId != null
                             && myCuratorGroupIds.Contains(g.CuratorGroupId.Value))))
            .Select(g => g.Id)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> StudentIdsAsync(
        long staffId, CancellationToken ct = default)
    {
        var groupIds = await ScopeGroupIdsAsync(staffId, ct);

        if (groupIds.Count == 0) return [];

        return await db.GroupMembers.AsNoTracking()
            .Where(m => groupIds.Contains(m.GroupId) && m.Status == MemberStatus.Active)
            .Select(m => m.StudentId)
            .Distinct()
            .ToListAsync(ct);
    }

    private sealed record CuratorCandidate(long? Direct, long? ViaCuratorGroup);
}
