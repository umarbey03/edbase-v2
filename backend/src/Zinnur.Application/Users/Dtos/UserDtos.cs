using Zinnur.Domain.Enums;

namespace Zinnur.Application.Users.Dtos;

/// <summary>
/// CRM ro'yxati va kartochkasi uchun foydalanuvchi.
/// (<c>Auth.Dtos.UserDto</c> — kirgan foydalanuvchining O'ZI uchun qisqa shakl;
/// bu esa o'quv bo'limi ko'radigan to'liq shakl.)
/// </summary>
/// <param name="TelegramUsername">
/// <c>@</c> BELGISIZ. Faqat ko'rsatish uchun — shaxs
/// <paramref name="TelegramId"/> bo'yicha aniqlanadi (sabab
/// <c>User.TelegramUsername</c> izohida).
/// </param>
public sealed record UserDetailsDto(
    long Id,
    string FullName,
    string Email,
    string? Phone,
    long? TelegramId,
    string? TelegramUsername,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Ro'yxat filtri. Barcha maydonlar ixtiyoriy.</summary>
/// <param name="Search">F.I.Sh., email yoki telefon bo'yicha qism-satr (pg_trgm GIN indeksi).</param>
/// <param name="Role">Rol bo'yicha filtr.</param>
/// <param name="IsActive">Faollik bo'yicha filtr.</param>
/// <param name="GroupId">
/// Shu guruhda <c>Active</c> a'zo bo'lganlar.
///
/// ★ <c>Stopped</c>/<c>Moved</c> ATAYLAB KIRMAYDI: "guruh bo'yicha filtr"
/// chiqarilgan yoki boshqa guruhga ko'chirilgan o'quvchini ko'rsatsa, xodim
/// uni hali shu guruhda o'qiyapti deb o'ylardi va guruh ro'yxati amalda
/// noto'g'ri bo'lardi. <c>Paused</c> ham kirmaydi: pauza vaqtinchalik, lekin
/// "hozir kim o'qiyapti" savoliga javob AYNAN <c>Active</c>.
/// Kerak bo'lsa keyin alohida <c>memberStatus</c> parametri qo'shiladi.
/// </param>
/// <param name="TelegramLinked">
/// <c>true</c> — Telegram bog'langanlar (<c>TelegramId != null</c>),
/// <c>false</c> — bog'lanmaganlar, <c>null</c> — filtr qo'llanmaydi.
/// </param>
/// <param name="Page">Sahifa (1 dan).</param>
/// <param name="PageSize">Sahifa hajmi (1..100, default 25).</param>
public sealed record UserListQuery(
    string? Search = null,
    UserRole? Role = null,
    bool? IsActive = null,
    long? GroupId = null,
    bool? TelegramLinked = null,
    int Page = 1,
    int PageSize = 25);

// ---------------------------------------------------------------- Telegram uzish

/// <summary>Telegram bog'lanishini uzish so'rovi.</summary>
/// <param name="Reason">
/// Ixtiyoriy sabab — audit iziga yoziladi ("raqam boshqa odamga o'tgan",
/// "ota-onasi so'radi"). Keyin tiklanmaydigan ma'lumot, shuning uchun
/// so'raladi, lekin MAJBURIY emas: majburiy qilinsa xodim shoshib "test"
/// deb yozib qo'yardi va maydon qiymatini yo'qotardi.
/// </param>
public sealed record TelegramUnlinkRequest(string? Reason = null);

/// <summary>
/// Uzishdan keyingi holat. Ikkala maydon ham DOIM <c>null</c> — shakl profil
/// javobidagi <c>telegram</c> bloki bilan bir xil bo'lib qolsin, ya'ni
/// frontend javobni to'g'ridan-to'g'ri holatga yozib qo'ya oladi.
/// </summary>
public sealed record TelegramUnlinkResponse(long? TelegramId, string? TelegramUsername);

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
