using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// DAVOMATNI QO'LDA TUZATISHNING IZI: KIM, QACHON, NIMADAN-NIMAGA
/// ========================================================================
///
/// NIMA UCHUN KERAK: davomat foizi reytingga, ogohlantirishlarga va
/// ota-onaga beriladigan hisobotga kiradi. "Nega bu o'quvchida shu dars
/// 'kelgan' bo'lib turibdi?" degan savol albatta chiqadi — eski tizimda
/// unga javob YO'Q edi, chunki qo'lda o'zgartirish qatorning ustiga
/// yozilib, avvalgi qiymat izsiz yo'qolardi.
///
/// NIMA UCHUN ALOHIDA JADVAL (qatorning o'zida "kim o'zgartirdi" ustuni
/// EMAS): bitta katak bir necha marta tuzatilishi mumkin (ustoz "kelmagan"
/// qo'yadi, keyin o'quvchi hujjat keltiradi va o'quv bo'limi "kelgan"ga
/// o'zgartiradi). Ustunlar bo'lsa faqat OXIRGISI qolardi va aynan
/// o'rtadagi qadam — nizoning sababi — yo'qolardi.
///
/// NIMA UCHUN <c>PaymentAudit</c> DAN NUSXA EMAS: u POLIMORF
/// (<c>Entity</c> + <c>EntityId</c> + satr <c>OldValue</c>/<c>NewValue</c>),
/// chunki bitta jadval to'lov, balans, chegirma va tarifni birga yozadi.
/// Bu yerda esa o'zgaradigan narsa BITTA va turi ANIQ — davomat holati.
/// Uni satrga o'girib saqlash "Present" ni "present" dan ajrata olmaydigan,
/// enum o'zgarsa jimgina buziladigan yozuv hosil qilardi. Falsafa esa
/// AYNI: yozuv asosiy amal bilan BIR tranzaksiyada saqlanadi — amal bekor
/// bo'lsa audit ham qolmaydi.
/// </summary>
public class AttendanceAudit : BaseEntity
{
    /// <summary>Tuzatilgan davomat qatori.</summary>
    public long AttendanceId { get; set; }

    /// <summary>
    /// Tuzatilgan qator — navigatsiya SHART.
    ///
    /// Sabab: o'quvchi xonaga umuman kirmagan bo'lsa, davomat qatori
    /// AYNI amalda yaratiladi va uning `Id` si `SaveChanges` gacha 0
    /// bo'ladi. `AttendanceId` ni qo'lda yozib bo'lmaydi — navigatsiya
    /// berilsa EF ikkala qatorni BIR `SaveChanges` ichida to'g'ri
    /// tartibda yozadi va FK'ni o'zi to'ldiradi. Aks holda auditni
    /// ikkinchi `SaveChanges` da yozishga to'g'ri kelardi va u yiqilsa
    /// IZSIZ o'zgargan qator qolardi.
    /// </summary>
    public Attendance? Attendance { get; set; }

    /// <summary>
    /// Dars va o'quvchi ATAYLAB takrorlangan (ular
    /// <see cref="Attendance"/> da ham bor).
    ///
    /// Sabab: "shu darsda kim nimani o'zgartirdi" va "shu o'quvchi bo'yicha
    /// nima bo'lgan" — audit tekshiruvidagi ikki asosiy savol. Ular
    /// <c>JOIN</c> siz, indeks bo'yicha to'g'ridan-to'g'ri javob olishi
    /// kerak. Bu denormalizatsiya xavfsiz: audit qatori YARATILGANDAN
    /// KEYIN hech qachon o'zgarmaydi, ya'ni nusxa asl bilan ajralib keta
    /// olmaydi.
    /// </summary>
    public long SessionId { get; set; }

    public long StudentId { get; set; }

    /// <summary>Tuzatgan xodim (ustoz, kurator yoki o'quv bo'limi).</summary>
    public long ActorId { get; set; }

    /// <summary>
    /// Tuzatgan xodim — YAGONA navigatsiya (dars va o'quvchi navigatsiyasiz).
    ///
    /// Sabab: davomat varag'ida har qatorning ostida "tuzatdi: Aziz
    /// Karimov" yozuvi chiqadi, ya'ni ISM har o'qishda kerak. Dars va
    /// o'quvchi esa chaqiruvchida allaqachon ma'lum — ular uchun
    /// navigatsiya faqat ortiqcha `JOIN` imkoniyati bo'lardi.
    /// </summary>
    public User? Actor { get; set; }

    /// <summary>
    /// Oldingi baho. <c>null</c> — qator SHU tuzatishda YARATILGAN
    /// (o'quvchi xonaga umuman kirmagan edi, ya'ni davomat yozuvi yo'q edi).
    /// </summary>
    public AttendanceStatus? OldStatus { get; set; }

    public AttendanceStatus NewStatus { get; set; }

    /// <summary>
    /// Oldingi qiymat AVTOMATIK o'lchovdanmi yoki avvalgi QO'LDA
    /// tuzatishdanmi. "Ustoz platformaning bahosini tuzatdi" bilan
    /// "o'quv bo'limi ustozning qarorini bekor qildi" — bu ikki boshqa
    /// hodisa, va nizoda aynan farqi muhim.
    /// </summary>
    public bool OldIsManual { get; set; }

    public string? OldReason { get; set; }

    public string? NewReason { get; set; }

    /// <summary>
    /// "Sababli" bayrog'i o'zgarishi (2026-08-16) — <see cref="Status"/>
    /// bilan AYNI qatorda, alohida jadval EMAS: bitta tuzatish ikkalasini
    /// ham o'zgartirishi mumkin (masalan `UpdateAsync` orqali holat, keyin
    /// `SetExcusedAsync` orqali bayroq) va ularning har biri o'z audit
    /// qatorini oladi — bu ustunlar shunchaki O'SHA qatorda tegilmagan
    /// tomonni "o'zgarishsiz" deb ko'rsatadi (eski qiymat = yangi qiymat).
    /// </summary>
    public bool OldIsExcused { get; set; }

    public bool NewIsExcused { get; set; }
}
