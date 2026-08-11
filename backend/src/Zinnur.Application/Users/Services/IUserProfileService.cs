using Zinnur.Application.Users.Dtos;

namespace Zinnur.Application.Users.Services;

/// <summary>
/// ========================================================================
/// O'QUVCHI PROFILI — YAGONA AGREGAT (<c>GET /users/{id}/profile</c>)
/// ========================================================================
///
/// NIMA UCHUN ALOHIDA SERVIS (<c>IUserService</c> ga qo'shilmadi):
/// <c>UserService</c> — YOZISH servisi (yaratish, tahrirlash, parol,
/// import) va uning ruxsat qoidasi "kim kimni BOSHQARADI" degan savolga
/// javob beradi. Bu esa faqat O'QISH va butunlay boshqa savol: "kim kimning
/// ma'lumotini QANCHA ko'radi". Ikkisini bitta sinfga qo'shish 800 qatorli
/// servisni yana ikki barobar kattalashtirardi va ikki xil ruxsat qoidasini
/// yonma-yon qo'yib, ularni aralashtirib yuborish xavfini tug'dirardi.
///
/// 🔴 RUXSAT SERVIS ICHIDA (<c>StudentAccess</c>): controller darajasidagi
/// rol atributi bu yerda YETARLI EMAS, chunki "o'z guruhidagi o'quvchi" va
/// "o'zi" shartlarini atribut bilan ifodalash mumkin emas.
/// </summary>
public interface IUserProfileService
{
    /// <summary>
    /// Profilning butun mazmuni: shaxsiy ma'lumot, Telegram, guruhlar,
    /// moliya, o'quv natijalari va izohlar.
    ///
    /// Bloklar so'rovchining roliga qarab KESILADI — batafsil
    /// <see cref="UserProfileDto"/> izohida.
    /// </summary>
    Task<UserProfileDto> GetAsync(long userId, long actorId, CancellationToken ct = default);
}
