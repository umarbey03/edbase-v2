namespace Zinnur.Application.Common.Interfaces;

/// <summary>
/// Foydalanuvchining SESSIYA HOLATI: hisob faolmi va sessiya versiyasi qanday.
///
/// NIMA UCHUN KERAK: kirish tokeni 15 daqiqa yashaydi va imzosi to'g'ri
/// bo'lgani uchun uni "haqiqiy" deb qabul qilish OSON. Lekin shu 15 daqiqa
/// ichida foydalanuvchi chiqib ketgan (`logout`), o'chirilgan yoki paroli
/// tiklangan bo'lishi mumkin. Token o'zi bu haqda hech narsa bilmaydi.
///
/// Shuning uchun har so'rovda token ichidagi <c>ver</c> qiymati SERVERDAGI
/// joriy versiya bilan solishtiriladi. Har so'rovda bazaga borish qimmat
/// (jonli darsda SignalR sekundiga o'nlab chaqiruv qiladi), shuning uchun
/// holat Redis'da qisqa muddat keshlanadi va o'zgarish paytida ANIQ
/// tozalanadi — ya'ni chiqish/o'chirish DARHOL kuchga kiradi, kesh muddati
/// esa faqat oxirgi himoya.
/// </summary>
public interface IAuthStateCache
{
    /// <summary>
    /// Joriy holatni qaytaradi. Foydalanuvchi topilmasa <c>null</c>.
    /// </summary>
    Task<UserAuthState?> GetAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// Keshni tozalaydi — chiqish, o'chirish, parol tiklash va rol
    /// o'zgarishida CHAQIRILISHI SHART.
    /// </summary>
    Task InvalidateAsync(long userId, CancellationToken ct = default);
}

/// <param name="TokenVersion">Sessiya versiyasi (<c>User.TokenVersion</c>).</param>
/// <param name="IsActive">Hisob faolmi.</param>
public sealed record UserAuthState(int TokenVersion, bool IsActive);
