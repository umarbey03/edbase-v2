namespace Zinnur.Application.Auth.Dtos;

/// <summary>
/// Bir martalik kod SO'RASH: <c>POST /api/v1/auth/phone/request-code</c>.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN FAQAT TELEFON — PAROL MAYDONI YO'Q (2026-08-13 qarori)
///
/// Email va parol bilan kirish BUTUNLAY olib tashlandi (talab R26).
/// Endi platformaga kirishning ikki eshigi bor va ikkalasi ham TELEGRAM
/// egaligiga tayanadi:
///   1) Mini App — o'quvchi, <c>initData</c> imzosi bilan (o'zgarmadi);
///   2) telefon + bir martalik kod — HAR QANDAY rol, HAR QANDAY brauzer.
///
/// Ikkinchisi xodimlar uchun QURILDI: ular ish stolida ishlaydi, Mini App
/// qobig'i esa o'quvchi shaklida, Telegram Login Widget esa umuman
/// yozilmagan. "Faqat telefon orqali" degan talabni Mini App yolg'iz
/// bajara olmasdi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Phone">
/// Foydalanuvchi kiritgan XOM ko'rinish. Normalizatsiya SERVERDA,
/// <c>User.NormalizePhone</c> bilan — ya'ni <c>PhoneNormalized</c>
/// ustunini to'ldiradigan AYNI metod bilan.
/// </param>
public sealed record PhoneCodeRequest(string Phone);

/// <summary>
/// Kod so'ralganidagi javob.
///
/// 🔴 JAVOB HAR DOIM BIR XIL — raqam bazada bor yoki yo'qligidan QAT'I
/// NAZAR. Aks holda bu endpoint "bu raqam tizimda bormi?" degan savolga
/// javob beradigan ochiq qidiruv vositasiga aylanardi (hisob sanash /
/// user enumeration): hujumchi raqamlarni ketma-ket sinab, markazning
/// butun mijozlar bazasini tiklab olardi.
/// </summary>
/// <param name="ExpiresInSeconds">
/// Kod qancha vaqt yaroqli. Bu SIR emas — u registrda qat'iy belgilangan
/// konstanta, ya'ni javobga qo'shilishi hech nima oshkor qilmaydi, lekin
/// interfeys taymer ko'rsata oladi.
/// </param>
/// <param name="ResendAfterSeconds">
/// Keyingi kodni qachondan so'rash mumkin. Ham SIR emas (qat'iy konstanta),
/// ham amaliy: bunsiz foydalanuvchi tugmani bosaverib 429 olardi va
/// sababini bilmasdi.
/// </param>
public sealed record PhoneCodeResponse(int ExpiresInSeconds, int ResendAfterSeconds);

/// <summary>Kodni tasdiqlash: <c>POST /api/v1/auth/phone/verify</c>.</summary>
/// <param name="Phone">Kod so'ralgan AYNI raqam (xom ko'rinishda bo'lishi mumkin).</param>
/// <param name="Code">Telegram orqali kelgan 6 xonali kod.</param>
public sealed record PhoneVerifyRequest(string Phone, string Code);

public sealed record RefreshRequest(string RefreshToken);

/// <summary>
/// KIRGAN foydalanuvchining O'ZI haqidagi qisqa shakl
/// (<c>GET /api/v1/auth/me</c> va <c>AuthResponse</c>).
///
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 BU DTO O'Z-O'ZIGA CHEKLANGAN (self-scoped) — SHU MUHIM
///
/// Uni to'ldiradigan yagona joy — <c>AuthService.GetCurrentAsync</c>, va u
/// tokendagi `sub` ni oladi. Ya'ni bu yerdan HECH QACHON boshqa odamning
/// ma'lumoti chiqmaydi: "kimning profili" degan parametr umuman yo'q.
/// ════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Phone">
/// 2026-08-14 da qo'shildi (talab R8 — video ustidagi suv belgisi).
///
/// 🔴 SUV BELGISI UCHUN RAQAM FAQAT SHU YERDAN OLINADI. Guruh doirasidagi
/// shakllardan (<c>GroupMemberDto</c>, davomat varag'i qatori, qatnashuvchi
/// DTO'si) OLINMASIN: ular USTOZGA ham ochiq va R27 aynan o'sha yo'lni
/// yopadi. Suv belgisini o'sha yo'ldan yig'ish yopilgan teshikni qayta
/// ochardi.
///
/// <c>null</c> — raqam kiritilmagan (bunday foydalanuvchilar BOR: ular
/// Telegram'ni ham ulay olmaydi). Interfeys bunda ism + id ga tushadi,
/// o'ylab topilgan raqam CHIZILMAYDI.
/// </param>
/// <param name="AvatarUpdatedAt">
/// Profil rasmi oxirgi marta qachon almashtirilgani. <c>null</c> — rasm
/// YO'Q, interfeys ism harfini chizadi.
///
/// ★ RASM MANZILI EMAS, VAQT TAMG'ASI: manzil har doim bir xil
/// (<c>/api/v1/profile/avatar/{id}</c>) va uni DTO'ga solish har javobga
/// takroriy satr qo'shardi. Klient manzilni Id'dan yasaydi, bu qiymatni
/// esa <c>?v=</c> parametri sifatida qo'shadi — shusiz brauzer yangi
/// rasmni ko'rsatmasdi (sabab <c>User.AvatarUpdatedAt</c> izohida).
/// </param>
public sealed record UserDto(
    long Id,
    string FullName,
    string Email,
    string? Phone,
    string Role,
    DateTimeOffset? AvatarUpdatedAt = null);

public sealed record AuthResponse(string AccessToken, string RefreshToken, UserDto User);
