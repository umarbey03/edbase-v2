using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Common.Scope;

/// <summary>
/// ========================================================================
/// BUGUNGI QAMROV: BITTA DEPLOYMENT = BITTA O'QUV MARKAZ
/// ========================================================================
///
/// <see cref="ILearningCenterScope"/> ning YAGONA bugungi amalga oshirilishi.
/// Markaz filtri YO'Q, chunki filtrlaydigan ustun ham yo'q — markaz
/// tushunchasi domenda hali mavjud emas.
///
/// 🔴 SHU SINF — KO'P-MARKAZLI O'ZGARISHNING BUTUN YUZASI. Yangi
///    `LearningCenter` qo'shilganda o'zgaradigan kod SHU FAYLDA
///    (yoki uning yonidagi yangi `TenantCenterScope` da) qoladi;
///    reyting servisiga ham, kontrollerga ham, DTO'larga ham
///    TEGILMAYDI. Interfeys izohida uch qadamli ro'yxat bor.
///
/// ── RUXSAT QOIDASI (YANGI — bugungacha guruhdan tashqari qamrov yo'q edi) ──
///
/// Markaz reytingini MARKAZNING HAR QANDAY FAOL foydalanuvchisi ko'radi:
/// o'quvchi, ustoz, kurator, o'quv bo'limi, admin.
///
/// ★ NIMA UCHUN ROL BO'YICHA CHEKLANMADI: qamrovning O'ZI — himoya.
/// Egasining sharti aynan shu edi: umumiy reyting "jami tizim
/// foydalanuvchilari uchun emas, faqat o'quv markaz uchun". Ya'ni
/// maxfiylik chegarasi ROL emas, MARKAZ. Rolni qo'shimcha chegara qilib
/// qo'yish esa talabni buzardi — o'quvchi markaz reytingini KO'RISHI kerak.
///
/// ★ ROL BAZADAN o'qiladi, tokendagi claim'dan emas (loyihadagi umumiy
/// qoida): kirish tokeni 15 daqiqa yashaydi va endi o'chirilgan hisob
/// eski token bilan markaz jadvalini ochib qo'ymasligi kerak.
/// </summary>
public sealed class SingleCenterScope(IApplicationDbContext db) : ILearningCenterScope
{
    /// <summary>
    /// Bugungi markaz belgisi. <c>"solo"</c> — RAQAM EMAS, ATAYLAB:
    /// ertaga bu qiymat markaz Id'siga (raqamga) aylanadi va eski
    /// <c>"solo"</c> kalitlari yangi <c>"7"</c> kalitlari bilan HECH QACHON
    /// to'qnashmaydi — TTL tugagunicha ular shunchaki e'tibordan chetda
    /// qoladi (<c>ICacheService</c> da prefiks bo'yicha o'chirish yo'q).
    /// </summary>
    private const string SoloCenter = "solo";

    public async Task<LearningCenterAudience> ResolveForViewerAsync(
        long viewerId, CancellationToken ct = default)
    {
        await EnsureViewerBelongsToCenterAsync(viewerId, ct).ConfigureAwait(false);

        // ------------------------------------------------------------ o'quvchilar
        //
        // ★ FAQAT FAOL PROFIL: o'chirilgan hisob reytingda "arvoh qator"
        // bo'lib qolmasin. Guruh reytingi ham aynan shunday ishlaydi
        // (u yerda a'zolik holati `Active` bo'lishi shart).
        //
        // 🔴 KELAJAK: shu `Where` ga `&& u.LearningCenterId == centerId`
        //    qo'shiladi — boshqa hech qayerga emas.
        var students = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Student && u.IsActive)
            .Select(u => new StudentRow(u.Id, u.FullName))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (students.Count == 0)
            return new LearningCenterAudience(SoloCenter, [], []);

        // ------------------------------------------------------------ a'zoliklar
        //
        // ASOSIY GURUHNI ANIQLASH — `LeaderboardService.PrimaryGroupAsync`
        // dagi QOIDANING AYNAN O'ZI (faol a'zolik + faol guruh + kurator
        // guruhi emas + eng erta qo'shilgani). Ikki joyda ikki xil qoida
        // bo'lsa, o'quvchining bosh sahifadagi "mening o'rnim" kartochkasi
        // va markaz jadvalidagi qatori boshqa-boshqa guruhdan hisoblanardi.
        //
        // ★ BITTA SO'ROV, GURUHLAR SONIDAN QAT'I NAZAR. Har guruh uchun
        // alohida so'rov (fan-out) ataylab qilinmadi: 40 guruhli markazda
        // u 40 ta borish-kelish bo'lardi.
        var memberships = await db.GroupMembers.AsNoTracking()
            .Where(m => m.Status == MemberStatus.Active
                     && m.Group!.IsActive
                     && m.Group.Type != GroupType.Curator)
            .OrderBy(m => m.StudentId)
            .ThenBy(m => m.JoinedAt)
            .ThenBy(m => m.Id)
            .Select(m => new MembershipRow(m.StudentId, m.GroupId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var primaryGroupByStudent = new Dictionary<long, long>();
        var groupIds = new HashSet<long>();

        foreach (var row in memberships)
        {
            // Tartib yuqorida qat'iy berilgan — BIRINCHI uchragan a'zolik
            // eng erta qo'shilgani, ya'ni asosiy guruh.
            primaryGroupByStudent.TryAdd(row.StudentId, row.GroupId);
            groupIds.Add(row.GroupId);
        }

        var audience = students.ConvertAll(s => new CenterStudent(
            s.Id,
            s.FullName,
            primaryGroupByStudent.TryGetValue(s.Id, out var groupId) ? groupId : null));

        return new LearningCenterAudience(SoloCenter, audience, [.. groupIds]);
    }

    /// <summary>
    /// Ko'ruvchi markazga tegishlimi.
    ///
    /// 🔴 KELAJAKDA SHU METOD MARKAZ ID'SINI QAYTARADI va `null` markazli
    ///    foydalanuvchi uchun <see cref="ForbiddenException"/> ko'taradi.
    ///    Bugun markaz ustuni yo'q, shuning uchun yagona shart — profil
    ///    FAOLLIGI.
    /// </summary>
    private async Task EnsureViewerBelongsToCenterAsync(long viewerId, CancellationToken ct)
    {
        var viewer = await db.Users.AsNoTracking()
            .Where(u => u.Id == viewerId)
            // Kerakli ustunlar SANAB olinadi — `PasswordHash` bazadan
            // umuman chiqmaydi (loyihadagi umumiy naqsh).
            .Select(u => new ViewerRow(u.Id, u.IsActive))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(User), viewerId);

        if (!viewer.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");
    }

    // ---------------------------------------------------------------- ichki shakllar

    private sealed record ViewerRow(long Id, bool IsActive);

    private sealed record StudentRow(long Id, string FullName);

    private sealed record MembershipRow(long StudentId, long GroupId);
}
