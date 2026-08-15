using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Media;
using Zinnur.Application.Profile.Dtos;

namespace Zinnur.Application.Profile.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// O'Z PROFILINI TAHRIRLASH (2026-08-15, loyiha egasining talabi)
/// ════════════════════════════════════════════════════════════════════════
///
/// *"Profil oynasida tahrirlash tugmasi ham bo'lsin... har qanday userlar
/// o'z profiliga rasm joylash imkoniyati bo'lsin, ismini o'zgartirishi
/// mumkin bo'lsin, nomerini alishtirish imkoniyati ham bo'lsin — lekin
/// bunda ham registerdagi kabi telegram orqali tasdiqlash majburiy."*
///
/// ── NIMA UCHUN `IUserService` GA QO'SHILMADI ───────────────────────────
///
/// 🔴 `IUserService` — XODIM vositasi: u BOSHQA odamning profilini
/// tahrirlaydi va har metodi `actorId` ning ROLINI tekshiradi (kim kimni
/// o'zgartira oladi). Bu servis esa TESKARI: u faqat CHAQIRUVCHINING
/// O'ZINI o'zgartiradi va rol UMUMAN tekshirilmaydi — "har qanday user"
/// degani aynan shu.
///
/// Ikkalasi bitta sinfga qo'yilsa, har metodda "bu o'zinikimi yoki
/// boshqaningmi?" degan shart paydo bo'lardi va o'sha shartning bitta
/// joyda unutilishi — o'quvchi boshqaning ismini o'zgartira olishi
/// degani. Bu yerda `userId` HAR DOIM tokendan keladi va u
/// PARAMETRDAN boshqa yo'l bilan kelmaydi.
///
/// ── UCHTA MAYDON, UCHTA HAR XIL XAVF DARAJASI ──────────────────────────
///
///   • ISM     — erkin matn, tasdiqsiz. Xatosi arzon va qaytariladi.
///   • RASM    — fayl, turi MAZMUNDAN tekshiriladi, hajmi cheklangan.
///   • TELEFON — 🔴 KIRISH KALITI. U tasdiqsiz o'zgarsa, hisobni
///                o'g'irlashning eng qisqa yo'li ochilardi: begona odam
///                bir zumda raqamni o'ziniki qilib, keyin "kirish kodi"
///                bilan hisobga egalik qilardi. Shuning uchun u IKKI
///                BOSQICHLI va TELEGRAM orqali tasdiqlanadi.
/// </summary>
public interface IProfileService
{
    /// <summary>
    /// Ismni o'zgartiradi.
    /// </summary>
    /// <returns>Yangilangan profil — klient `auth.user` ni shu bilan almashtiradi.</returns>
    Task<UserDto> UpdateNameAsync(
        long userId, UpdateProfileRequest request, CancellationToken ct = default);

    /// <summary>
    /// Profil rasmini yuklaydi (eskisi ALMASHTIRILADI va ombordan
    /// o'chiriladi).
    ///
    /// ★ TUR MAZMUNDAN ANIQLANADI (`MediaSignatures`), klient
    /// sarlavhasiga ISHONILMAYDI — vazifa biriktirmalari bilan AYNI
    /// qoida. Faqat RASM qabul qilinadi.
    /// </summary>
    Task<AvatarUploadedDto> UploadAvatarAsync(
        long userId, LessonAssetUpload upload, CancellationToken ct = default);

    /// <summary>Profil rasmini olib tashlaydi (ekranda yana ism harfi chiziladi).</summary>
    Task RemoveAvatarAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// Rasmni OQIM bilan beradi.
    ///
    /// ⚠️ RUXSAT ATAYLAB KENG: tizimga kirgan HAR KIM istalgan
    /// foydalanuvchining rasmini ko'ra oladi. Sabab: avatar ro'yxatlarda,
    /// chatda, davomat jadvalida va reytingda chiziladi — ya'ni u
    /// allaqachon ism bilan bir qatorda turadigan ochiq ma'lumot. Har
    /// ro'yxat uchun alohida ruxsat qoidasi yozish o'nlab joyda takror
    /// mantiq bo'lardi va ulardan biri albatta noto'g'ri bo'lardi.
    ///
    /// 🔴 LEKIN AUTENTIFIKATSIYA SHART: manzil ochiq qoldirilsa,
    /// foydalanuvchi Id'sini ketma-ket sanab, butun bazadagi rasmlarni
    /// yuklab olish mumkin bo'lardi.
    /// </summary>
    /// <returns>Rasm yo'q bo'lsa <c>null</c> (klient ism harfini chizadi).</returns>
    Task<LessonAssetDownload?> OpenAvatarAsync(long targetUserId, CancellationToken ct = default);

    /// <summary>
    /// TELEFON ALMASHTIRISH — 1-BOSQICH: niyatni qayd etadi.
    ///
    /// Kod SHU YERDA YUBORILMAYDI: yangi raqamga bog'langan Telegram
    /// hisobi hali noma'lum (sabab <see cref="IPhoneChangeStore"/>
    /// izohida). Javob foydalanuvchiga NIMA QILISHNI aytadi — botga
    /// yangi raqamdan «Raqamni ulashish» yuborish.
    /// </summary>
    Task<PhoneChangeStatusDto> RequestPhoneChangeAsync(
        long userId, ChangePhoneRequest request, CancellationToken ct = default);

    /// <summary>
    /// Kutayotgan almashtirish holati (yo'q bo'lsa <c>null</c>).
    ///
    /// Klient buni QISQA oraliqlarda so'raydi: foydalanuvchi Telegramda
    /// tugmani bosgani bilan ilova o'z-o'zidan bilmaydi — bu yagona
    /// "kod keldimi?" signali.
    /// </summary>
    Task<PhoneChangeStatusDto?> GetPhoneChangeAsync(long userId, CancellationToken ct = default);

    /// <summary>Kutayotgan almashtirishni bekor qiladi.</summary>
    Task CancelPhoneChangeAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// TELEFON ALMASHTIRISH — 2-BOSQICH: kod tekshiriladi va raqam
    /// almashadi.
    ///
    /// 🔴 PROFIL AYNI PAYTDA YANGI TELEGRAM HISOBIGA BOG'LANADI —
    /// sabab <see cref="PendingPhoneChange.TelegramId"/> izohida.
    /// </summary>
    Task<UserDto> ConfirmPhoneChangeAsync(
        long userId, ConfirmPhoneRequest request, CancellationToken ct = default);
}
