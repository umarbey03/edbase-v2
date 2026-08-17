using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Media;
using Zinnur.Application.Profile.Dtos;

namespace Zinnur.Application.Profile.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// O'Z PROFIL RASMI (2026-08-15 da qo'shilgan, 2026-08-17 da qisqartirildi)
/// ════════════════════════════════════════════════════════════════════════
///
/// ⚠️ ISM VA TELEFONNI O'ZI TAHRIRLASH OLIB TASHLANDI (2026-08-17, loyiha
/// egasining qarori): "foydalanuvchi o'z ism familyasi va nomerini edit
/// qilish imkoniga ega bo'lmasligi kerak". Bu ikkala maydonni FAQAT o'quv
/// bo'limi/admin <c>UsersController</c> orqali o'zgartira oladi — xodim
/// o'ziniki bo'lsa ham emas (talab "barcha foydalanuvchilar" uchun).
///
/// Shu bilan birga telefon ALMASHTIRISH oqimi ham (Telegram tasdig'i bilan)
/// butunlay olib tashlandi — sabab va tafsilot
/// <c>TelegramUpdateHandler.HandleContactAsync</c> izohida.
///
/// ── NIMA UCHUN `IUserService` GA QO'SHILMADI ───────────────────────────
///
/// 🔴 `IUserService` — XODIM vositasi: u BOSHQA odamning profilini
/// tahrirlaydi va har metodi `actorId` ning ROLINI tekshiradi (kim kimni
/// o'zgartira oladi). Bu servis esa TESKARI: u faqat CHAQIRUVCHINING
/// O'ZINI o'zgartiradi va rol UMUMAN tekshirilmaydi.
///
/// Ikkalasi bitta sinfga qo'yilsa, har metodda "bu o'zinikimi yoki
/// boshqaningmi?" degan shart paydo bo'lardi va o'sha shartning bitta
/// joyda unutilishi — o'quvchi boshqaning rasmini o'zgartira olishi
/// degani. Bu yerda `userId` HAR DOIM tokendan keladi va u
/// PARAMETRDAN boshqa yo'l bilan kelmaydi.
/// </summary>
public interface IProfileService
{
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
}
