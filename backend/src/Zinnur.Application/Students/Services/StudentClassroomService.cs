using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Application.Students.Dtos;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Students.Services;

/// <summary>
/// <see cref="IStudentClassroomService"/> amalga oshirilishi. HTTP haqida
/// HECH NARSA bilmaydi.
///
/// ★ FAQAT `MemberStatus.Active` A'ZOLIK HISOBGA OLINADI — `Paused`,
/// `Stopped`, `Moved` emas. Sabab `UserService.ApplyGroupFilter` dagi bilan
/// AYNI: chiqarilgan yoki ko'chirilgan o'quvchi "hozir birga o'qiyapman"
/// ro'yxatida ko'rinmasligi kerak.
///
/// ★ KURATOR GURUHI BU YERDA A'ZOLIK MANBAI EMAS (<c>Group.IsCuratorGroup</c>):
/// o'quvchi kurator guruhiga to'g'ridan-to'g'ri a'zo bo'lmaydi (sabab
/// <c>Group.CuratorGroupId</c> izohida). Shu sababli
/// <c>db.GroupMembers</c> so'rovi tabiiy ravishda FAQAT ustoz guruhlarini
/// qaytaradi — qo'shimcha filtr shart emas.
/// </summary>
public sealed class StudentClassroomService(
    IApplicationDbContext db, IRuntimeSettings runtimeSettings) : IStudentClassroomService
{
    /// <inheritdoc />
    public async Task<ClassroomDto> GetAsync(long studentId, CancellationToken ct = default)
    {
        // ★ `Type != Curator` — DEMO/ESKI YOZUVLARGA QARSHI HIMOYA.
        //
        // Naqsh bo'yicha o'quvchi kurator guruhiga TO'G'RIDAN-TO'G'RI a'zo
        // bo'lmasligi kerak (`Group.CuratorGroupId` izohi), lekin amalda
        // bunday qatorlar topildi (jonli tekshiruvda: bitta o'quvchi HAM
        // ustoz guruhida, HAM kurator guruhida a'zo sifatida chiqdi).
        // Filtrsiz bu "Kurator guruhi — X" nomli, ustozsiz, chalkash
        // ikkinchi kartochka sifatida ko'rinardi — modal FAQAT haqiqiy
        // o'qish guruhini ko'rsatishi kerak, kurator esa shu guruh
        // ICHIDA (pastda) chiqadi.
        var groups = await db.GroupMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId
                     && m.Status == MemberStatus.Active
                     && m.Group!.Type != GroupType.Curator)
            .Select(m => new
            {
                m.Group!.Id,
                m.Group.Name,
                m.Group.TeacherId,
                m.Group.AssistantId,
                m.Group.CuratorGroupId,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (groups.Count == 0)
            return new ClassroomDto([], SupportContact());

        var groupIds = groups.Select(g => g.Id).ToList();

        // ── Bog'langan kurator guruhlarining o'z kuratori (Group.AssistantId) ──
        //
        // ★ IKKINCHI SO'ROV, LEKIN NOYOB `CuratorGroupId`lar bo'yicha — odatda
        //   1-2 ta, ko'p emas (kurator guruhi bir nechta ustoz guruhini
        //   birlashtiradi, ya'ni ID'lar takrorlanadi).
        var curatorGroupIds = groups
            .Where(g => g.CuratorGroupId is not null)
            .Select(g => g.CuratorGroupId!.Value)
            .Distinct()
            .ToList();

        var curatorGroupAssistants = curatorGroupIds.Count == 0
            ? new Dictionary<long, long?>()
            : await db.Groups.AsNoTracking()
                .Where(g => curatorGroupIds.Contains(g.Id))
                .Select(g => new { g.Id, g.AssistantId })
                .ToDictionaryAsync(g => g.Id, g => g.AssistantId, ct)
                .ConfigureAwait(false);

        // ── Kerakli xodim va guruhdosh Id'larini BITTA lug'atga yig'amiz ──
        var staffIds = groups
            .SelectMany(g => new long?[]
            {
                g.TeacherId,
                g.AssistantId,
                g.CuratorGroupId is { } cgId && curatorGroupAssistants.TryGetValue(cgId, out var a)
                    ? a
                    : null,
            })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        // ── Guruhdoshlar — o'z a'zoligi bilan BIR so'rovda ──
        var classmateRows = await db.GroupMembers.AsNoTracking()
            .Where(m => groupIds.Contains(m.GroupId)
                     && m.Status == MemberStatus.Active
                     && m.StudentId != studentId)
            .Select(m => new { m.GroupId, m.StudentId, m.Student!.FullName })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var names = await db.Users.AsNoTracking()
            .Where(u => staffIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct)
            .ConfigureAwait(false);

        string? StaffName(long? id) => id is { } value && names.TryGetValue(value, out var name) ? name : null;

        var result = groups
            .Select(g =>
            {
                var curatorId = g.AssistantId
                    ?? (g.CuratorGroupId is { } cgId && curatorGroupAssistants.TryGetValue(cgId, out var a)
                        ? a
                        : null);

                var classmates = classmateRows
                    .Where(m => m.GroupId == g.Id)
                    .Select(m => new ClassroomMemberDto(m.StudentId, m.FullName))
                    .OrderBy(m => m.FullName, StringComparer.Ordinal)
                    .ToList();

                return new ClassroomGroupDto(
                    g.Id, g.Name, StaffName(g.TeacherId), StaffName(curatorId), classmates);
            })
            .ToList();

        return new ClassroomDto(result, SupportContact());
    }

    /// <summary>
    /// Bog'lanish kontakti — `general.support_contact` sozlamasidan.
    /// Bo'sh bo'lsa <c>null</c>: ekran qatorni umuman ko'rsatmaydi.
    /// </summary>
    private string? SupportContact()
    {
        var value = runtimeSettings.Current.Value(SettingsRegistry.Keys.SupportContact)?.Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }
}
