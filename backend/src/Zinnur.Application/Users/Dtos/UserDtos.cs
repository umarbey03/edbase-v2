using Zinnur.Domain.Enums;

namespace Zinnur.Application.Users.Dtos;

/// <summary>
/// CRM ro'yxati va kartochkasi uchun foydalanuvchi.
/// (<c>Auth.Dtos.UserDto</c> — kirgan foydalanuvchining O'ZI uchun qisqa shakl;
/// bu esa o'quv bo'limi ko'radigan to'liq shakl.)
/// </summary>
/// <param name="Email">
/// 🔴 <c>null</c> — MAJBURIY ustun bo'lsa ham (bazada <c>NOT NULL</c>):
/// so'rovchi USTOZ va kontakt serverda KESILGAN (talab R27,
/// <c>StudentAudience.Teacher</c>). Boshqa hech qanday holatda bo'sh
/// bo'lmaydi. <paramref name="Phone"/>, <paramref name="TelegramId"/> va
/// <paramref name="TelegramUsername"/> ham AYNI qoida bilan kesiladi.
/// </param>
/// <param name="TelegramUsername">
/// <c>@</c> BELGISIZ. Faqat ko'rsatish uchun — shaxs
/// <paramref name="TelegramId"/> bo'yicha aniqlanadi (sabab
/// <c>User.TelegramUsername</c> izohida).
/// </param>
public sealed record UserDetailsDto(
    long Id,
    string FullName,
    string? Email,
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
///
/// ★ 2026-08-13 dan bu filtr KIRISHGA TAYYORLIK hisoboti bo'lib qoldi:
/// <c>telegramLinked=false</c> — bu odamlar tizimga KIRA OLMAYDI.
/// Rol bilan birga ishlaydi: <c>?role=Teacher&amp;telegramLinked=false</c>.
/// </param>
/// <param name="PhoneMissing">
/// 🔴 <c>true</c> — <c>PhoneNormalized IS NULL</c> bo'lganlar.
///
/// ★ NIMA UCHUN <see cref="TelegramLinked"/> YETARLI EMASDI: eski
/// tizimdan ko'chirish dublikat raqamli foydalanuvchilarga
/// <c>PhoneNormalized = NULL</c> qoldirgan, LEKIN <c>Phone</c> ustuni
/// to'ldirilgan. Ya'ni CRM'da ularning raqami KO'RINIB TURADI va xodim
/// "hammasi joyida" deb o'ylaydi — bot va kirish oqimi esa ularni
/// hech qachon topa olmaydi (ikkalasi ham AYNAN <c>PhoneNormalized</c>
/// bo'yicha izlaydi).
///
/// Bu filtr — o'sha ko'rinmas guruhni topishning yagona yo'li.
/// Tuzatish oddiy: profilni ochib, raqamni qaytadan saqlash
/// (<c>SetPhone</c> normalizatsiyani qayta hisoblaydi).
/// </param>
/// <param name="Page">Sahifa (1 dan).</param>
/// <param name="PageSize">Sahifa hajmi (1..100, default 25).</param>
public sealed record UserListQuery(
    string? Search = null,
    UserRole? Role = null,
    bool? IsActive = null,
    long? GroupId = null,
    bool? TelegramLinked = null,
    bool? PhoneMissing = null,
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

/// <param name="Phone">
/// 🔴 XODIM ROLLARI UCHUN MAJBURIY (<c>Student</c> dan tashqari hammasi).
///
/// 2026-08-13 dan tizimga kirish FAQAT telefon orqali bo'ladi. Telefonsiz
/// yaratilgan xodim CRM'da mutlaqo normal ko'rinadi, lekin HECH QACHON
/// kira olmaydi va buni faqat u birinchi marta kirishga urinib ko'rganda —
/// odatda birinchi ish kuni ertalab — bilib qolamiz.
///
/// ★ O'QUVCHI UCHUN IXTIYORIY QOLDIRILDI: o'quvchilar ko'pincha ommaviy
/// (CSV) qo'shiladi va raqami keyinroq aniqlanadi. Ular baribir kira
/// olmaydi, lekin bu HOLAT ko'rinib turadi — `GET /users?phoneMissing=true`.
/// Xodimda esa bunday kutish oynasi yo'q: u ishga chiqqan kuni kira
/// olishi kerak.
/// </param>
/// <param name="Password">
/// ⚠️ MAYDON OLIB TASHLANDI (2026-08-13). Parol bilan kirish yo'q, ya'ni
/// uni qabul qilish "kirish ma'lumoti berdim" degan yolg'on taassurot
/// qoldirardi. <c>PasswordHash</c> ustuni bazada qoldi va server uni
/// hech kimga ma'lum bo'lmagan tasodifiy qiymat bilan to'ldiradi.
/// </param>
public sealed record CreateUserRequest(
    string FullName,
    string Email,
    UserRole Role,
    string? Phone = null,
    bool IsActive = true);

/// <param name="Role">
/// <c>null</c> bo'lsa rol O'ZGARMAYDI. Rol o'zgarsa barcha sessiyalar bekor qilinadi.
/// </param>
/// <param name="Phone">
/// 🔴 XODIM ROLLARI UCHUN MAJBURIY — sabab <see cref="CreateUserRequest"/> da.
///
/// ★ TEKSHIRUV YANGI ROL bo'yicha: o'quvchini ustozga aylantirayotgan
/// so'rov ham telefon talab qiladi, aks holda "ko'tarilgan" xodim
/// darhol tizimdan tushib qolardi.
/// </param>
public sealed record UpdateUserRequest(
    string FullName,
    string Email,
    string? Phone = null,
    UserRole? Role = null);

/// <summary>
/// Yaratilgan foydalanuvchi.
///
/// ⚠️ <c>TemporaryPassword</c> maydoni OLIB TASHLANDI (2026-08-13).
///
/// ★ NIMA UCHUN JAVOB BARIBIR O'RAM (wrapper) BO'LIB QOLDI, to'g'ridan-
/// to'g'ri <see cref="UserDetailsDto"/> qaytarilmadi: shakl o'zgarsa
/// mavjud klientlar va o'nlab integratsion test AYNI paytda buzilardi,
/// foyda esa faqat "bitta qavat kamroq JSON". Kelajakda bu yerga
/// "botga taklif havolasi" kabi maydonlar qo'shilishi ehtimoli ham bor.
/// </summary>
public sealed record CreateUserResponse(UserDetailsDto User);

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
