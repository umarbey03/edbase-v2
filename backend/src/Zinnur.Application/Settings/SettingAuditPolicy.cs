namespace Zinnur.Application.Settings;

/// <summary>Audit yozuviga TUSHADIGAN qiymatlar.</summary>
/// <param name="OldValue">Eski qiymat (sir bo'lsa <c>null</c>).</param>
/// <param name="NewValue">Yangi qiymat (sir bo'lsa <c>null</c>).</param>
/// <param name="Note">Izoh — sir uchun "qiymat yozilmadi" tushuntirishi.</param>
public sealed record SettingAuditValues(string? OldValue, string? NewValue, string? Note);

/// <summary>
/// ========================================================================
/// AUDITGA NIMA YOZILISHI QOIDASI
/// ========================================================================
///
/// 🔴 SIR QIYMAT AUDITGA YOZILMAYDI.
///
/// ★ NIMA UCHUN: maskalash FAQAT HTTP javobini himoyalaydi. Agar sir audit
/// jadvaliga ochiq tushsa, uni ko'ra oladigan har kim — hisobot yozuvchi,
/// baza nusxasini olgan odam, qo'llab-quvvatlashga yuborilgan dump —
/// sirni to'liq ko'rardi. Ya'ni maskalash butunlay ma'nosiz bo'lardi:
/// eshikni qulflab, kalitni yonidagi qutiga qo'yish bilan barobar.
///
/// ★ O'ZGARISH FAKTI ESA YOZILADI: kim, qachon va QAYSI kalitni
/// almashtirgani audit izida qoladi. Bu nizo tekshiruvi uchun yetarli —
/// "kim tokenni almashtirdi?" degan savolga javob bor, "qanday token edi?"
/// degan savolga esa javob bo'lmasligi KERAK.
///
/// ★ NIMA UCHUN ALOHIDA SINF: bu qoida bitta <c>if</c> ga o'xshaydi, lekin
/// uning buzilishi jimgina sodir bo'ladi va faqat sir sizib chiqqanda
/// bilinadi. Alohida bo'lgani uchun uni TO'G'RIDAN-TO'G'RI, bazasiz test
/// qilib bo'ladi va yangi audit chaqiruvi qo'shilganda qoidani takrorlash
/// unutilmaydi.
/// </summary>
public static class SettingAuditPolicy
{
    /// <summary>Sir o'zgarganda audit izohiga yoziladigan matn.</summary>
    public const string SecretNote = "Sir qiymat — eski/yangi qiymat auditga yozilmaydi.";

    /// <summary>Audit yozuvi uchun qiymatlarni tayyorlaydi.</summary>
    public static SettingAuditValues For(
        SettingDefinition definition, string? oldValue, string? newValue)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.IsSecret
            ? new SettingAuditValues(null, null, SecretNote)
            : new SettingAuditValues(oldValue, newValue, null);
    }
}
