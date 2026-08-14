using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Staffing;

namespace Zinnur.Application.Messaging.Services;

/// <inheritdoc cref="ICuratorDirectory"/>
public sealed class CuratorDirectory(IApplicationDbContext db) : ICuratorDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> ResolveRespondersAsync(
        long studentId, CancellationToken ct = default)
    {
        // BITTA so'rov: o'quvchining faol guruhlaridan nomzodlar.
        //
        // Har guruh TO'RT nomzod beradi (ikki o'rindiq × ikki yo'l) va
        // ulardan qaysilari mas'ul ekanini guruhning O'Z sozlamasi
        // (`QuestionResponderRole`) hal qiladi.
        //
        // ★ SARALASH ILOVADA, SQL DA EMAS: tartib qoidasi
        // `StaffResponsibility.Responsible` da va u BAHOLASH bilan bitta
        // manba. SQL'ga ko'chirilsa ikkinchi nusxa paydo bo'lardi.
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
            .Select(m => new GroupSeatsRow(
                m.Group!.QuestionResponderRole,
                m.Group.TeacherId,
                m.Group.AssistantId,
                m.Group.CuratorGroup!.TeacherId,
                m.Group.CuratorGroup.AssistantId))
            .ToListAsync(ct);

        var ordered = new List<long>(candidates.Count * 2);

        foreach (var candidate in candidates)
        {
            var seats = new StaffResponsibility.StaffSeats(
                candidate.TeacherId,
                candidate.AssistantId,
                candidate.CuratorGroupTeacherId,
                candidate.CuratorGroupAssistantId);

            foreach (var id in StaffResponsibility.Responsible(
                         seats, candidate.Role, StaffDuty.Questions))
            {
                // TAKROR BO'LMASIN: bir odam ikki guruhda ham mas'ul
                // bo'lishi mumkin, suhbat esa BITTA (kalit — juftlik).
                if (!ordered.Contains(id)) ordered.Add(id);
            }
        }

        if (ordered.Count == 0) return [];

        var users = await db.Users.AsNoTracking()
            .Where(u => ordered.Contains(u.Id) && u.IsActive)
            .ToListAsync(ct);

        // Nomzodlar TARTIBI saqlanadi: mas'uliyat tartibi ro'yxat tartibi
        // bo'lib qoladi. (Bazadan kelgan tartib emas — u tasodifiy.)
        var result = new List<User>(ordered.Count);

        foreach (var id in ordered)
        {
            var match = users.Find(u => u.Id == id);
            if (match is not null) result.Add(match);
        }

        return result;
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
        //
        // ⚠️ R40 DA TEGILMADI — sabab interfeys izohida (guruh chati).
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
        // 🔴 RUXSATNING MANBAI. Qoida `StaffResponsibility` da — AYNI
        // ifodani baholash servisi ham o'qiydi, faqat boshqa `StaffDuty`
        // bilan. Bu yerda qo'lda OR yozilsa ikkalasi vaqt o'tib
        // ajralib ketardi.
        var groupIds = db.Groups.AsNoTracking()
            .Where(g => g.Type == GroupType.Group && g.IsActive)
            .Where(StaffResponsibility.Predicate(staffId, StaffDuty.Questions))
            .Select(g => g.Id);

        return await db.GroupMembers.AsNoTracking()
            .Where(m => groupIds.Contains(m.GroupId) && m.Status == MemberStatus.Active)
            .Select(m => m.StudentId)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>Guruhning to'rt o'rindiq nomzodi + shu guruhning tanlovi.</summary>
    private sealed record GroupSeatsRow(
        GroupStaffRole Role,
        long? TeacherId,
        long? AssistantId,
        long? CuratorGroupTeacherId,
        long? CuratorGroupAssistantId);
}
