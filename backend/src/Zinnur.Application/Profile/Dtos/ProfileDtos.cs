namespace Zinnur.Application.Profile.Dtos;

/// <summary>
/// O'Z profilini tahrirlash — ism.
/// </summary>
/// <param name="FullName">
/// Yangi to'liq ism. Bo'sh bo'lishi mumkin emas.
///
/// ⚠️ ROL, EMAIL VA `IsActive` BU YERDA YO'Q — VA BU ATAYLAB. Ular
/// XODIM boshqaradigan maydonlar (`UpdateUserRequest`). Foydalanuvchi
/// o'ziga rol tanlay olsa, butun ruxsat tizimi ma'nosini yo'qotardi;
/// email esa hozircha xodim tomonidan beriladigan identifikator.
/// </param>
public sealed record UpdateProfileRequest(string FullName);

/// <summary>
/// Telefon almashtirishning BIRINCHI bosqichi.
/// </summary>
/// <param name="Phone">
/// Yangi raqam. Xom ko'rinishda bo'lishi mumkin — normalizatsiya
/// SERVERDA (`User.NormalizePhone`), mijozda uning nusxasi YO'Q.
/// </param>
public sealed record ChangePhoneRequest(string Phone);

/// <summary>
/// Telefon almashtirishning IKKINCHI bosqichi — Telegramga kelgan kod.
/// </summary>
public sealed record ConfirmPhoneRequest(string Code);

/// <summary>
/// Telefon almashtirish oqimining HOLATI — ekran shu ma'lumot bilan
/// chiziladi.
/// </summary>
/// <param name="Phone">Kutayotgan YANGI raqam (formatlanmagan, E.164).</param>
/// <param name="CodeSent">
/// Bot kontaktni qabul qilib, kodni yuborganmi.
///
/// ★ EKRAN AYNAN SHU BAYROQQA QARAB IKKIGA BO'LINADI: <c>false</c> —
/// "botga raqamni ulashing" ko'rsatmasi, <c>true</c> — kod kiritish
/// maydoni. Ikkalasini bir vaqtda ko'rsatish foydalanuvchini
/// "qaysinisini qilay?" holatiga tushirardi.
/// </param>
/// <param name="BotUsername">
/// Botning <c>@username</c> i — ko'rsatmadagi havola uchun (<c>t.me/…</c>).
/// Sozlanmagan bo'lsa <c>null</c> va ekran havolasiz matn ko'rsatadi.
/// </param>
/// <param name="ExpiresInSeconds">Niyat qancha vaqtdan keyin bekor bo'ladi.</param>
public sealed record PhoneChangeStatusDto(
    string Phone,
    bool CodeSent,
    string? BotUsername,
    int ExpiresInSeconds);

/// <summary>
/// Profil rasmi yuklangandan keyingi javob.
/// </summary>
/// <param name="AvatarUpdatedAt">
/// Kesh buzish uchun vaqt tamg'asi — klient rasm manziliga shu qiymatni
/// qo'shadi (<c>?v=…</c>), aks holda brauzer eski rasmni ko'rsatib
/// turardi (sabab <c>User.AvatarUpdatedAt</c> izohida).
/// </param>
public sealed record AvatarUploadedDto(DateTimeOffset AvatarUpdatedAt);
