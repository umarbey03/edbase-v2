using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Payments;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Users;

/// <summary>
/// So'rovchi o'quvchi ma'lumotini QANCHA ko'rishi mumkin.
/// Tartib MUHIM EMAS (bazaga yozilmaydi) — bu faqat ish vaqtidagi qaror.
/// </summary>
internal enum StudentAudience
{
    /// <summary>O'quv bo'limi yoki admin — hamma blok ko'rinadi.</summary>
    Manage,

    /// <summary>
    /// O'z guruhidagi ustoz yoki kurator — moliya bloki UMUMAN yuborilmaydi.
    /// </summary>
    Staff,

    /// <summary>
    /// O'quvchining o'zi — ichki izohlar va to'lov jurnali yuborilmaydi.
    /// </summary>
    Self,
}

/// <summary>
/// ========================================================================
/// "KIM QAYSI O'QUVCHINI KO'RA OLADI" — YAGONA QOIDA
/// ========================================================================
///
/// Profil agregati (<c>GET /users/{id}/profile</c>) ham, izohlar CRUD'i ham
/// AYNAN shu joydan o'tadi. Qoida BITTA joyda bo'lgani uchun yangi endpoint
/// qo'shilganda uni takrorlash unutilmaydi — eski tizim zaifligi X-4 aynan
/// takrorlangan (va yarim joyda chala qolgan) tekshiruvdan kelib chiqqan edi.
///
/// ★ ROL BAZADAN o'qiladi, TOKEN'dagi claim'dan emas: kirish tokeni 15
/// daqiqa yashaydi, ya'ni endi o'chirilgan yoki roli pasaytirilgan xodim
/// eski token bilan o'quvchi profilini ochib qo'ymasligi kerak.
///
/// ★ TEKSHIRUV TARTIBI: avval RUXSAT, keyin MAVJUDLIK. Aks holda begona
/// odam <c>404</c> va <c>403</c> ni taqqoslab, bazada qaysi Id'lar borligini
/// aniqlab olardi.
/// </summary>
internal static class StudentAccess
{
    /// <summary>
    /// Ruxsatni aniqlaydi va nishon profilni qaytaradi.
    /// </summary>
    /// <exception cref="ForbiddenException">Ruxsat yo'q.</exception>
    /// <exception cref="NotFoundException">Nishon profil yo'q (faqat ruxsat bo'lganda).</exception>
    internal static async Task<(StudentSubject Student, StudentAudience Audience)> AuthorizeAsync(
        IApplicationDbContext db, long actorId, long studentId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var actor = await LoadActorAsync(db, actorId, ct).ConfigureAwait(false);

        switch (actor.Role)
        {
            case UserRole.Admin:
            case UserRole.Academic:
                return (await LoadSubjectAsync(db, studentId, ct).ConfigureAwait(false),
                        StudentAudience.Manage);

            case UserRole.Student:
                // O'quvchi FAQAT o'zini ko'radi. Mavjudlik tekshiruvi
                // kerak emas — u allaqachon kirgan, ya'ni profili bor.
                if (actor.Id != studentId)
                {
                    throw new ForbiddenException(
                        "Siz faqat O'Z profilingizni ko'ra olasiz.");
                }

                return (await LoadSubjectAsync(db, studentId, ct).ConfigureAwait(false),
                        StudentAudience.Self);

            case UserRole.Teacher:
            case UserRole.Assistant:
                if (!await SharesGroupAsync(db, actor.Id, studentId, ct).ConfigureAwait(false))
                {
                    throw new ForbiddenException(
                        "Bu o'quvchi sizning guruhlaringizda yo'q. Ustoz va kurator "
                        + "faqat o'z guruhidagi o'quvchining profilini ko'radi.");
                }

                return (await LoadSubjectAsync(db, studentId, ct).ConfigureAwait(false),
                        StudentAudience.Staff);

            default:
                // Yangi rol qo'shilsa TAQIQ bilan boshlanadi: "unutilgan rol"
                // jimgina hamma narsani ko'rib qolmasligi kerak.
                throw new ForbiddenException("Bu ma'lumotga ruxsatingiz yo'q.");
        }
    }

    /// <summary>
    /// Amalni bajaruvchi — faqat qaror uchun kerakli uchta ustun
    /// (<c>PasswordHash</c> bazadan UMUMAN olinmaydi).
    /// </summary>
    private static async Task<ActorInfo> LoadActorAsync(
        IApplicationDbContext db, long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => new ActorInfo(u.Id, u.Role, u.IsActive))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return actor;
    }

    /// <summary>
    /// Xodim va o'quvchi UMUMIY guruhda bo'lganmi — BITTA so'rovda.
    ///
    /// Bog'lanish uch yo'l bilan tuziladi va uchalasi teng kuchda
    /// (<c>ICuratorDirectory</c> dagi qoidaning aynan o'zi, unga qo'shimcha
    /// ravishda ustoz yo'li):
    ///   1) xodim guruhning USTOZI (<c>TeacherId</c>);
    ///   2) xodim guruhga bevosita KURATOR qilib biriktirilgan (<c>AssistantId</c>);
    ///   3) guruh xodimning KURATOR GURUHIGA bog'langan (<c>CuratorGroupId</c>).
    ///
    /// ★ A'ZOLIK HOLATI TEKSHIRILMAYDI (<c>Stopped</c>/<c>Moved</c> ham
    /// yaraydi) va guruh ARXIVLANGAN bo'lsa ham yaraydi. Sabab: ustoz o'zi
    /// o'qitgan o'quvchining tarixini va o'zi yozgan izohlarni ko'rishi
    /// kerak. Guruhdan chiqarilgandan keyin izoh birdan "begona" bo'lib
    /// qolsa, ustoz o'z yozuvini tahrirlay ham olmasdi.
    ///
    /// ⚠️ Bu "hamma narsani ko'radi" degani EMAS: <see cref="StudentAudience.Staff"/>
    /// da moliya bloki baribir yuborilmaydi.
    /// </summary>
    private static async Task<bool> SharesGroupAsync(
        IApplicationDbContext db, long staffId, long studentId, CancellationToken ct)
    {
        // "Mening kurator guruhlarim" — ICHKI so'rov sifatida qoldiriladi,
        // shunda bazaga borish-kelish BITTA bo'ladi.
        var myCuratorGroupIds = db.Groups.AsNoTracking()
            .Where(g => g.AssistantId == staffId)
            .Select(g => g.Id);

        return await db.GroupMembers.AsNoTracking()
            .AnyAsync(m => m.StudentId == studentId
                        && (m.Group!.TeacherId == staffId
                            || m.Group.AssistantId == staffId
                            || (m.Group.CuratorGroupId != null
                                && myCuratorGroupIds.Contains(m.Group.CuratorGroupId.Value))),
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<StudentSubject> LoadSubjectAsync(
        IApplicationDbContext db, long studentId, CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Id == studentId)
            // Kerakli ustunlar SANAB o'qiladi — `PasswordHash` bazadan
            // umuman chiqmaydi (`UserService.ListAsync` bilan ayni naqsh).
            // `PaymentExempt` — SOYA ustun, `EF.Property` bilan olinadi.
            .Select(u => new StudentSubject(
                u.Id,
                u.FullName,
                u.Email,
                u.Phone,
                u.TelegramId,
                u.TelegramUsername,
                u.TelegramLinkedAt,
                u.Role,
                u.IsActive,
                u.CreatedAt,
                u.UpdatedAt,
                EF.Property<bool>(u, PaymentFields.Exempt)))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
        ?? throw new NotFoundException(nameof(User), studentId);
}

/// <summary>Qaror uchun zarur minimal ma'lumot.</summary>
internal sealed record ActorInfo(long Id, UserRole Role, bool IsActive);

/// <summary>
/// Profil egasining ustunlari (parol hash'isiz).
/// <c>PaymentExempt</c> — bloklash qamrovini hisoblash uchun.
/// </summary>
internal sealed record StudentSubject(
    long Id,
    string FullName,
    string Email,
    string? Phone,
    long? TelegramId,
    string? TelegramUsername,
    DateTimeOffset? TelegramLinkedAt,
    UserRole Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool PaymentExempt);
