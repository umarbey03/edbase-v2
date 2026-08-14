using Zinnur.Application.Common.Models;
using Zinnur.Application.Users.Dtos;

namespace Zinnur.Application.Users.Services;

/// <summary>
/// Foydalanuvchilarni boshqarish (o'quv bo'limi / admin paneli).
///
/// HAR BIR metod <paramref name="actorId"/> ni oladi — ruxsat tekshiruvi
/// SHU YERDA, controller'da emas. Controller faqat <c>[Authorize(Roles=...)]</c>
/// darvozasini ushlaydi; kim kimni tahrirlay olishini servis hal qiladi.
/// </summary>
public interface IUserService
{
    Task<PagedResult<UserDetailsDto>> ListAsync(
        UserListQuery query, long actorId, CancellationToken ct = default);

    Task<UserDetailsDto> GetAsync(long id, long actorId, CancellationToken ct = default);

    Task<CreateUserResponse> CreateAsync(
        CreateUserRequest request, long actorId, CancellationToken ct = default);

    Task<UserDetailsDto> UpdateAsync(
        long id, UpdateUserRequest request, long actorId, CancellationToken ct = default);

    /// <summary>Profilni o'chiradi va barcha sessiyalarini darhol bekor qiladi.</summary>
    Task<UserDetailsDto> SetActiveAsync(
        long id, bool isActive, long actorId, CancellationToken ct = default);

    // ⚠️ `ResetPasswordAsync` OLIB TASHLANDI (2026-08-13): parol bilan
    //    kirish yo'q, ya'ni "vaqtinchalik parol" hech qayerda ishlamasdi.
    //    Sessiyani bekor qilish uchun `SetActiveAsync(false)` yoki
    //    `UnlinkTelegramAsync` (batafsil `UserService` dagi izohda).

    /// <summary>
    /// Telegram bog'lanishini UZADI (o'quv bo'limi qarori).
    ///
    /// 🔴 Uzilgandan keyin o'quvchi platformaga KIRA OLMAYDI: barcha
    /// sessiyalari bekor qilinadi (<c>User.UnlinkTelegram</c> ichida
    /// <c>TokenVersion</c> oshiriladi) va sessiya holati keshi tozalanadi.
    ///
    /// Amal audit iziga tushadi (<c>TelegramUnlinkAudit</c>): kim, qachon,
    /// qaysi hisobni va nima sababdan uzgan.
    ///
    /// Bog'lanmagan profilda <c>409</c> (Domain qoidasi).
    /// </summary>
    Task<TelegramUnlinkResponse> UnlinkTelegramAsync(
        long id, TelegramUnlinkRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// CSV import: <c>full_name,phone,email,role</c>.
    /// Paketlab yoziladi, xato qatorlar butun importni to'xtatmaydi.
    /// </summary>
    Task<UserImportResponse> ImportCsvAsync(
        Stream csv, long actorId, CancellationToken ct = default);
}
