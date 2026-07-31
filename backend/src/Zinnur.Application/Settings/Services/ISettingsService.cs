using Zinnur.Application.Settings.Dtos;

namespace Zinnur.Application.Settings.Services;

/// <summary>
/// ========================================================================
/// SUPER-ADMIN SOZLAMALAR PANELI — use-case'lar
/// ========================================================================
///
/// 🔴 FAQAT <c>Admin</c>. <c>Academic</c> ham ko'rmaydi, ham o'zgartira olmaydi.
///
/// ★ NIMA UCHUN <c>Academic</c> ham EMAS: eski tizimning eng og'ir zaifligi
/// (audit X-4) aynan shundan boshlangan — <c>academic</c> roli admin
/// akkauntini tahrirlay olardi va shu orqali butun tizimni egallash mumkin
/// edi. Sozlamalar undan ham kuchli vosita: bu yerda bloklash chegarasi,
/// tashqi integratsiya kalitlari va (kelajakda) yana ko'p narsa turadi.
/// Shuning uchun darvoza IMKON QADAR TOR.
///
/// ★ ROL BAZADAN QAYTA O'QILADI, JWT claim'idan EMAS. Kirish tokeni 15
/// daqiqa yashaydi — roli pasaytirilgan yoki o'chirilgan xodim eski token
/// bilan yana 15 daqiqa sozlamalarga tega olardi. <c>Application/Users</c>
/// dagi bilan AYNI naqsh.
///
/// ★ YANGILASH SEMANTIKASI — HAR KALIT ALOHIDA RESURS:
/// endpoint <c>PUT /api/v1/settings/{key}</c>, ya'ni PUT butun RESURSNI
/// (bitta sozlamani) almashtiradi. Bu ataylab: agar shartnoma
/// "<c>PUT /api/v1/settings</c> + hamma kalit" bo'lganda, bitta maydonni
/// o'zgartirmoqchi bo'lgan interfeys yuborilmagan kalitlarni jimgina
/// o'chirib yuborardi (PUT = TO'LIQ ALMASHTIRISH). Kalit darajasidagi PUT
/// bu xavfni butunlay yo'q qiladi va idempotent bo'lib qoladi.
/// </summary>
public interface ISettingsService
{
    /// <summary>Guruhlangan to'liq ro'yxat — panel formani shundan quradi.</summary>
    Task<SettingsPageDto> ListAsync(long actorId, CancellationToken ct = default);

    /// <summary>Bitta sozlama. Noma'lum kalit — 404.</summary>
    Task<SettingDto> GetAsync(string key, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Qiymatni o'zgartiradi (faqat <c>Database</c> manbali kalitlar uchun).
    /// Audit izi AYNI tranzaksiyada yoziladi.
    /// </summary>
    Task<SettingDto> UpdateAsync(
        string key, UpdateSettingRequest request, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Bazadagi qatorni o'chiradi — qiymat yana muhitdan yoki registrdagi
    /// standartdan olinadi.
    /// </summary>
    Task<SettingDto> ResetAsync(string key, long actorId, CancellationToken ct = default);
}
