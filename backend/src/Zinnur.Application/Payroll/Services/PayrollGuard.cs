using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Payroll.Services;

/// <summary>
/// Oylik modulining YAGONA ruxsat darvozasi.
///
/// ★ NIMA UCHUN ALOHIDA: tekshiruv ikkita servisda kerak
/// (<see cref="PayrollService"/> va <see cref="PayrollRuleService"/>).
/// Ikki nusxa vaqt o'tib bir-biridan uzilardi — masalan biriga "profil
/// faol emas" sharti qo'shilib, ikkinchisiga qo'shilmay qolardi.
///
/// ★ ROL TOKEN'DAN EMAS, BAZADAN — <c>PaymentService.LoadActorAsync</c>
/// bilan AYNI sabab: eski token bilan pasaytirilgan rol ishlamasin.
///
/// ★ FAQAT ADMIN (Academic ham EMAS): xodimlar haqi va stavkalar — markazning
/// eng nozik ichki ma'lumoti. Bu <c>PaymentService</c> dan ATAYLAB farq
/// qiladi (u yerda o'quv bo'limi ham kiradi).
/// </summary>
internal static class PayrollGuard
{
    public static async Task EnsureAdminAsync(
        IApplicationDbContext db, long actorId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        if (actor.Role != UserRole.Admin)
        {
            throw new ForbiddenException(
                "Oylik hisoblash paneliga faqat administrator kira oladi.");
        }
    }

    /// <summary>Maydon xatosini bir xil shaklda ko'taradi (barcha oylik servislari uchun).</summary>
    public static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
