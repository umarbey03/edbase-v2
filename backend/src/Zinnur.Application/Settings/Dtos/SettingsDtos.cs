namespace Zinnur.Application.Settings.Dtos;

/// <summary>
/// Maydonni chizish uchun QOIDALAR. Panel formani AYNAN shu ma'lumotdan
/// quradi — kodga qarab emas.
///
/// ★ NIMA UCHUN CHEKLOVLAR JAVOBDA: interfeysni boshqa agent yozadi. Agar
/// chegara faqat serverda bo'lsa, u yerda takroran qo'lda yozilardi va
/// birinchi o'zgarishdayoq ikkalasi bir-biriga mos kelmay qolardi
/// (foydalanuvchi "saqlash" bosgach 400 olardi va sababini tushunmasdi).
/// </summary>
/// <param name="Choices">Tanlov turi uchun ruxsat etilgan qiymatlar (aks holda bo'sh).</param>
/// <param name="Minimum">Son uchun eng kichik qiymat (bo'lmasa <c>null</c>).</param>
/// <param name="Maximum">Son uchun eng katta qiymat.</param>
/// <param name="MaxLength">Matn uzunligi chegarasi.</param>
/// <param name="Format">Qo'shimcha format talabi (manzil, vaqt zonasi).</param>
public sealed record SettingConstraintsDto(
    IReadOnlyList<string> Choices,
    decimal? Minimum,
    decimal? Maximum,
    int MaxLength,
    SettingFormat Format);

/// <summary>
/// BITTA sozlama — panelning bitta maydoni.
///
/// 🔴 SIR UCHUN QAT'IY QOIDA: <see cref="Value"/> va <see cref="DefaultValue"/>
/// HAR DOIM <c>null</c>. Sirning yagona ko'rinishi — <see cref="MaskedValue"/>
/// (oxirgi 4 belgi) va <see cref="IsSet"/> bayrog'i.
/// </summary>
/// <param name="Key">Ommaviy identifikator; yangilash URL'ida shu ishlatiladi.</param>
/// <param name="Group">Guruh (panel bo'limi).</param>
/// <param name="GroupName">Guruhning o'zbekcha nomi.</param>
/// <param name="Name">Maydon nomi (o'zbekcha).</param>
/// <param name="Description">Sozlama nima uchun kerakligi.</param>
/// <param name="Kind">Maydon turi — panel shunga qarab element tanlaydi.</param>
/// <param name="IsSecret">Sirmi (maskalangan maydon, "ko'rsatish" tugmasi YO'Q).</param>
/// <param name="IsEditable">Paneldan o'zgartirsa bo'ladimi.</param>
/// <param name="ReadOnlyReason">
/// O'zgartirib bo'lmasa — NIMA UCHUN. Panel bu matnni maydon yonida
/// ko'rsatadi, aks holda foydalanuvchi "nega o'chirilgan?" deb so'rardi.
/// </param>
/// <param name="Origin">Joriy qiymat qayerdan keldi: <c>Database|Environment|Default</c>.</param>
/// <param name="IsSet">Qiymat umuman bormi.</param>
/// <param name="Value">Joriy qiymat (SIR bo'lsa <c>null</c>).</param>
/// <param name="MaskedValue">Sirning maskalangan ko'rinishi (sir bo'lmasa <c>null</c>).</param>
/// <param name="DefaultValue">Standart qiymat (SIR bo'lsa <c>null</c>).</param>
/// <param name="Constraints">Validatsiya qoidalari.</param>
/// <param name="UpdatedAt">Oxirgi o'zgartirilgan vaqt (faqat bazadagi qiymat uchun).</param>
/// <param name="UpdatedById">Oxirgi o'zgartirgan xodim.</param>
public sealed record SettingDto(
    string Key,
    SettingGroup Group,
    string GroupName,
    string Name,
    string Description,
    SettingValueKind Kind,
    bool IsSecret,
    bool IsEditable,
    string? ReadOnlyReason,
    SettingOrigin Origin,
    bool IsSet,
    string? Value,
    string? MaskedValue,
    string? DefaultValue,
    SettingConstraintsDto Constraints,
    DateTimeOffset? UpdatedAt,
    long? UpdatedById);

/// <summary>Bitta bo'lim: sarlavha + maydonlar.</summary>
public sealed record SettingGroupDto(
    SettingGroup Group,
    string Name,
    string Description,
    IReadOnlyList<SettingDto> Items);

/// <summary>Panelning butun sahifasi — guruhlangan ro'yxat.</summary>
/// <param name="Groups">Bo'limlar, registrdagi tartibda.</param>
public sealed record SettingsPageDto(IReadOnlyList<SettingGroupDto> Groups);

/// <summary>
/// Bitta sozlamani yangilash so'rovi.
///
/// ★ NIMA UCHUN QIYMAT SATR (son yoki mantiqiy emas): saqlash ham satr
/// (<c>AppSettings.Value</c>), tur esa registrda. Agar so'rov turlashtirilgan
/// bo'lsa, har yangi tur uchun yangi DTO va yangi endpoint kerak bo'lardi.
/// Satr bilan shartnoma BITTA bo'lib qoladi, tekshiruv esa registr qoidasi
/// bo'yicha serverda bajariladi.
/// Mantiqiy uchun <c>"true"</c>/<c>"false"</c>, son uchun <c>"15"</c>.
/// </summary>
public sealed record UpdateSettingRequest(string Value);
