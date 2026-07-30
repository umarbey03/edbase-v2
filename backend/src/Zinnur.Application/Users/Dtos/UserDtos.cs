using Zinnur.Domain.Enums;

namespace Zinnur.Application.Users.Dtos;

/// <summary>
/// CRM ro'yxati va kartochkasi uchun foydalanuvchi.
/// (<c>Auth.Dtos.UserDto</c> — kirgan foydalanuvchining O'ZI uchun qisqa shakl;
/// bu esa o'quv bo'limi ko'radigan to'liq shakl.)
/// </summary>
public sealed record UserDetailsDto(
    long Id,
    string FullName,
    string Email,
    string? Phone,
    long? TelegramId,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Ro'yxat filtri. Barcha maydonlar ixtiyoriy.</summary>
/// <param name="Search">F.I.Sh., email yoki telefon bo'yicha qism-satr (pg_trgm GIN indeksi).</param>
/// <param name="Role">Rol bo'yicha filtr.</param>
/// <param name="IsActive">Faollik bo'yicha filtr.</param>
/// <param name="Page">Sahifa (1 dan).</param>
/// <param name="PageSize">Sahifa hajmi (1..100, default 25).</param>
public sealed record UserListQuery(
    string? Search = null,
    UserRole? Role = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 25);

/// <param name="Password">
/// Bo'sh bo'lsa server kuchli vaqtinchalik parol generatsiya qiladi va uni
/// javobda BIR MARTA qaytaradi (bazada faqat hash saqlanadi).
/// </param>
public sealed record CreateUserRequest(
    string FullName,
    string Email,
    UserRole Role,
    string? Phone = null,
    string? Password = null,
    bool IsActive = true);

/// <param name="Role">
/// <c>null</c> bo'lsa rol O'ZGARMAYDI. Rol o'zgarsa barcha sessiyalar bekor qilinadi.
/// </param>
public sealed record UpdateUserRequest(
    string FullName,
    string Email,
    string? Phone = null,
    UserRole? Role = null);

/// <summary>Yaratilgan foydalanuvchi + (agar generatsiya qilingan bo'lsa) boshlang'ich parol.</summary>
/// <param name="TemporaryPassword">FAQAT shu javobda ko'rinadi — hech qayerda saqlanmaydi.</param>
public sealed record CreateUserResponse(
    UserDetailsDto User,
    string? TemporaryPassword);

/// <summary>Parol tiklash natijasi.</summary>
/// <param name="TemporaryPassword">
/// FAQAT shu javobda ko'rinadi. Bazada faqat BCrypt hash saqlanadi, shuning
/// uchun uni qayta ko'rsatishning iloji yo'q — yo'qolsa qaytadan tiklanadi.
/// </param>
public sealed record ResetPasswordResponse(
    long UserId,
    string TemporaryPassword);

/// <summary>CSV importdagi bitta qatorning xatosi.</summary>
/// <param name="Line">Fayldagi qator raqami (1 = sarlavha, shuning uchun ma'lumot 2 dan boshlanadi).</param>
/// <param name="Reason">Nima uchun qabul qilinmagani.</param>
public sealed record UserImportIssue(int Line, string Reason);

/// <summary>CSV import hisoboti.</summary>
/// <param name="TotalRows">Fayldagi ma'lumot qatorlari soni.</param>
/// <param name="Created">Muvaffaqiyatli yaratilganlar.</param>
/// <param name="Failed">Rad etilgan qatorlar.</param>
/// <param name="Issues">Har bir rad etilgan qator uchun sabab (qator raqami bilan).</param>
public sealed record UserImportResponse(
    int TotalRows,
    int Created,
    int Failed,
    IReadOnlyList<UserImportIssue> Issues);
