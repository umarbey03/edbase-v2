using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Jobs;

/// <summary>
/// Fon vazifasi KIMNING nomidan ishlaydi.
///
/// ── NIMA UCHUN UMUMAN AKTYOR KERAK ─────────────────────────────────────
///
/// Fon vazifalari MAVJUD use-case'larni chaqiradi (yangi mantiq yozmaydi):
/// <c>ILiveSessionService.EndAsync</c> va <c>IPaymentService.OpenPeriodAsync</c>.
/// Ikkalasi ham ruxsatni SERVIS ICHIDA tekshiradi va har o'zgarishga audit
/// izi qoldiradi — ya'ni "kim qildi" degan savolga javob bo'lishi SHART.
/// Tekshiruvni chetlab o'tadigan "ichki" yo'l ochish qoidani ikki nusxaga
/// bo'lardi va vaqt o'tib ular ajralib ketardi.
///
/// ── QANDAY TANLANADI ───────────────────────────────────────────────────
///
/// Eng KICHIK Id'li FAOL <c>Admin</c>, u yo'q bo'lsa <c>Academic</c>.
/// Ikkala rol ham moliya amallariga (<c>EnsureCanManage</c>) va dars
/// hostligiga (<c>IsHost</c>) huquqli, ya'ni qo'shimcha imtiyoz
/// BERILMAYDI — vazifa xodim qo'lda qila oladigan ishdan ortig'ini qila
/// olmaydi.
///
/// ⚠️ CHEKLOV, OCHIQ AYTILGAN: auditda o'zgarish o'sha HAQIQIY odamning
/// nomiga yoziladi. To'g'rirog'i alohida "tizim" foydalanuvchisi bo'lardi,
/// lekin u yangi seed yozuvini yoki <c>ActorId</c> ni null qiladigan model
/// o'zgarishini talab qiladi — ikkalasi ham migratsiya masalasi va bu
/// bosqichning vazifasi emas (izoh hisobotda ham qayd etilgan).
/// Audit yozuvining <c>Note</c> maydonida amal fon vazifasidan kelgani
/// ko'rinadi (masalan "Oy ochilgandan keyin").
///
/// ⚠️ AKTYOR TOPILMASA vazifa YIQILMAYDI — jimgina o'tkazib yuboriladi va
/// ogohlantirish logga tushadi. Bo'sh baza (birinchi ko'tarilish, seed hali
/// tugamagan) tufayli fon xizmati xato bilan to'lib ketmasin.
/// </summary>
internal static class JobActor
{
    public static async Task<long?> ResolveAsync(
        IApplicationDbContext db, CancellationToken ct = default) =>
        await db.Users.AsNoTracking()
            .Where(u => u.IsActive
                     && (u.Role == UserRole.Admin || u.Role == UserRole.Academic))

            // Admin birinchi, so'ng eng eski hisob — tanlov BARQAROR bo'lsin:
            // har yurishda boshqa xodim auditga tushib qolmasin.
            .OrderBy(u => u.Role == UserRole.Admin ? 0 : 1)
            .ThenBy(u => u.Id)
            .Select(u => (long?)u.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
}
