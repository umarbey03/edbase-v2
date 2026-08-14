using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// TELEGRAM BOG'LANISHINI UZISHNING IZI: KIM, QACHON, KIMNI, QAYSI HISOBDAN
/// ========================================================================
///
/// NIMA UCHUN KERAK: uzish — XAVFSIZLIK amali. U o'quvchini platformadan
/// butunlay chiqarib tashlaydi (o'quvchi faqat Telegram orqali kiradi), va
/// undan keyin BOSHQA Telegram hisobini o'sha profilga bog'lash yo'li
/// ochiladi. Ya'ni bu akkauntni egallab olishning YAGONA qolgan yo'li va u
/// ataylab insayder harakatini talab qiladi ("eski bog'lanishni faqat o'quv
/// bo'limi bekor qiladi" qoidasi). Bunday amal izsiz
/// qolsa, "kim mening hisobimni uzdi?" degan savolga javob bo'lmasdi.
///
/// ========================================================================
/// NIMA UCHUN ALOHIDA JADVAL — <c>PaymentAudit</c> GA QO'SHILMADI
/// ========================================================================
///
/// <see cref="PaymentAudit"/> POLIMORF (<c>Entity</c> + <c>EntityId</c> +
/// satr <c>OldValue</c>/<c>NewValue</c>) va bu unda TO'G'RI: bitta jadval
/// to'lov, balans, chegirma va tarifni birga yozadi, ular esa BIR modulning
/// bir-biriga o'xshash pul o'zgarishlari. Telegram uzish — moliya hodisasi
/// EMAS. Uni o'sha jadvalga tiqish moliya auditini ifloslantirardi:
/// "o'quvchi bo'yicha barcha moliya harakatlari" so'rovi (u <c>StudentId</c>
/// bo'yicha o'qiladi) begona qatorlarni ham tortib chiqarardi.
///
/// Naqsh <see cref="AttendanceAudit"/> ga yaqin va AYNI sabab bilan: hodisa
/// BITTA va turi ANIQ, shuning uchun ustunlar TIPLANGAN
/// (<c>long OldTelegramId</c>, <c>string? OldTelegramUsername</c>) — satrga
/// o'girib saqlash Telegram ID'sini raqam sifatida taqqoslash imkonini
/// yo'qotardi. Falsafa ham AYNI: yozuv asosiy amal bilan BIR tranzaksiyada
/// saqlanadi — amal bekor bo'lsa audit ham qolmaydi, ya'ni bo'lmagan
/// o'zgarish haqida yozuv qolmaydi.
///
/// ★ Jadval nomi hodisaga ANIQ mos: bog'LANISH izini bot oqimi yozadi
/// (u yerda "kim" — o'quvchining o'zi va u logda bor). Bu yerda esa muhimi —
/// XODIM harakati. Kelajakda bog'lanish hodisasi ham kerak bo'lsa, ustun
/// qo'shish arzon migratsiya.
///
/// FAQAT QO'SHILADI va O'QILADI: yozuv yaratilgandan keyin hech qachon
/// yangilanmaydi va o'chirilmaydi.
/// </summary>
public class TelegramUnlinkAudit : BaseEntity
{
    /// <summary>Bog'lanishi uzilgan profil (odatda o'quvchi).</summary>
    public long UserId { get; set; }

    /// <summary>
    /// Uzgan xodim (o'quv bo'limi yoki admin).
    ///
    /// <c>required</c> emas, lekin AMALDA doim to'ladi: uzish endpointi
    /// autentifikatsiyasiz chaqirilmaydi.
    /// </summary>
    public long ActorId { get; set; }

    /// <summary>
    /// Uzgan xodim — YAGONA navigatsiya.
    ///
    /// Sabab: profil drawer'ida "Uzilgan: 10.08.2026, Aziz Karimov" satri
    /// ko'rinadi, ya'ni ISM har o'qishda kerak. O'quvchi esa chaqiruvchida
    /// allaqachon ma'lum — uning navigatsiyasi faqat ortiqcha <c>JOIN</c>
    /// imkoniyati bo'lardi.
    /// </summary>
    public User? Actor { get; set; }

    /// <summary>Uzilgan Telegram hisobining ID'si (tiplangan — satr emas).</summary>
    public long OldTelegramId { get; set; }

    /// <summary>Uzilgan hisobning <c>@username</c> i (bo'lsa).</summary>
    public string? OldTelegramUsername { get; set; }

    /// <summary>
    /// Xodim ko'rsatgan sabab (ixtiyoriy). Nizoda eng qimmat maydon: "raqam
    /// boshqa odamga o'tgan", "ota-onasi so'radi" kabi izoh keyin
    /// tiklanmaydi.
    /// </summary>
    public string? Reason { get; set; }

    public const int MaxReasonLength = 500;
}
