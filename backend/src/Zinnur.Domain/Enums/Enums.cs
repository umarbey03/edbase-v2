namespace Zinnur.Domain.Enums;

/// <summary>Foydalanuvchi roli. Bazada int sifatida saqlanadi.</summary>
/// <remarks>
/// Tartib MUHIM: qiymatlar bazaga yozilgan. Yangi rol FAQAT oxiriga qo'shiladi,
/// mavjud raqamlar hech qachon o'zgartirilmaydi.
/// </remarks>
public enum UserRole
{
    Student = 0,
    Teacher = 1,
    Assistant = 2,
    Academic = 3,
    Admin = 4,
}

/// <summary>Dars turi: ustoz darsi yoki yordamchi (kurator) darsi.</summary>
public enum SessionType
{
    Teacher = 0,
    Assistant = 1,
}

/// <summary>Jonli dars holati.</summary>
public enum SessionStatus
{
    Scheduled = 0,
    Live = 1,
    Ended = 2,
    Cancelled = 3,
}

/// <summary>Davomat holati.</summary>
public enum AttendanceStatus
{
    Absent = 0,
    Present = 1,
    Late = 2,
    Partial = 3,
}

/// <summary>Guruhdagi a'zolik holati.</summary>
public enum MemberStatus
{
    Active = 0,
    Paused = 1,
    Stopped = 2,
    Moved = 3,
}

/// <summary>Guruh turi — jadval qoidalari shunga bog'liq.</summary>
public enum GroupType
{
    /// <summary>Oddiy ustoz guruhi. Haftada ANIQ 2 kun dars.</summary>
    Group = 0,

    /// <summary>Yakka o'quvchi. Haftada 1..7 kun.</summary>
    Individual = 1,

    /// <summary>
    /// Kurator (yordamchi) guruhi. Haftada 1..7 kun (odatda 3).
    /// O'quvchilari BOG'LANGAN ustoz guruhlaridan keladi
    /// (<c>Group.CuratorGroupId</c>), o'zida to'g'ridan-to'g'ri a'zo bo'lmaydi.
    /// </summary>
    Curator = 2,
}

/// <summary>Uy vazifasiga topshirilgan javob holati.</summary>
public enum SubmissionStatus
{
    Submitted = 0,
    Graded = 1,
}

/// <summary>Test urinishi holati.</summary>
public enum AttemptStatus
{
    InProgress = 0,
    Submitted = 1,
}

/// <summary>Test turi: dars testi yoki umumiy musobaqa.</summary>
public enum TestKind
{
    /// <summary>Aniq bir kurs darsiga bog'langan (sur'at nazoratiga kiradi).</summary>
    Lesson = 0,

    /// <summary>Musobaqa testi — kursdan mustaqil, hammaga ochiq.</summary>
    Competition = 1,
}

/// <summary>Javob formati — o'quv bo'limi qaysi ko'rinishda javob qabul qilishini belgilaydi.</summary>
[Flags]
public enum AnswerFormats
{
    None = 0,
    Text = 1,
    Image = 2,
    Audio = 4,
}

/// <summary>Yuklangan fayl turi.</summary>
public enum AttachmentKind
{
    Image = 0,
    Audio = 1,
    Document = 2,
}

/// <summary>
/// Kurs darsining TURI.
/// </summary>
/// <remarks>
/// Tartib MUHIM: qiymatlar bazaga <c>int</c> sifatida yoziladi. Yangi qiymat
/// FAQAT oxiriga qo'shiladi.
///
/// ★ NIMA UCHUN BAYROQ (<c>bool IsExam</c>) EMAS: bugun ikki tur bor, lekin
/// "takrorlash", "loyiha ishi" kabi turlar qo'shilishi ehtimoli yuqori.
/// <c>bool</c> bo'lganda uchinchi tur ikkinchi <c>bool</c> ni talab qilardi
/// va (false,false)/(true,true) kabi MA'NOSIZ kombinatsiyalar paydo bo'lardi.
///
/// ★ TUR ASSET TURINI BELGILAYDI (<see cref="LessonAssetKind"/>):
/// <c>Normal</c> darsda faqat video, <c>Exam</c> darsda faqat rasm bo'ladi.
/// Invariant <see cref="Zinnur.Domain.Entities.ModuleLesson"/> da.
/// </remarks>
public enum LessonKind
{
    /// <summary>Odatiy video dars (bir yoki bir nechta video qism).</summary>
    Normal = 0,

    /// <summary>Imtihon darsi — video o'rniga rasm(lar) beriladi.</summary>
    Exam = 1,
}

/// <summary>
/// Darsga biriktirilgan media faylning turi.
/// </summary>
/// <remarks>
/// Tartib MUHIM: qiymatlar bazaga <c>int</c> sifatida yoziladi. Yangi qiymat
/// FAQAT oxiriga qo'shiladi.
///
/// ★ NIMA UCHUN <see cref="AttachmentKind"/> QAYTA ISHLATILMADI: u o'quvchi
/// YUBORADIGAN javob faylini tasvirlaydi (rasm/ovoz/hujjat) va unda
/// <c>Video</c> qiymati YO'Q — dars kontenti uchun esa video ASOSIY tur.
/// Mavjud enumga <c>Video</c> qo'shilsa, u o'quvchi javobida ham
/// "ruxsat etilgan" ko'rinib qolardi va har tekshiruv joyida qo'lda
/// istisno yozish kerak bo'lardi.
/// </remarks>
public enum LessonAssetKind
{
    /// <summary>Video qism (odatiy dars).</summary>
    Video = 0,

    /// <summary>Rasm (imtihon darsi topshiriqlari).</summary>
    Image = 1,
}

// ============================================================================
// MOLIYA (FAZA 4)
// ============================================================================

/// <summary>
/// Oylik to'lov yozuvining holati.
///
/// <c>Partial</c> ATAYLAB alohida: eski tizimda qisman to'lov ham "paid"
/// bo'lib qolardi va markaz jimgina pul yo'qotardi (100 000 so'm 540 000 lik
/// oyni yopardi).
/// </summary>
public enum PaymentStatus
{
    /// <summary>Yozuv ochilgan, hali to'lov tushmagan.</summary>
    Due = 0,

    /// <summary>Bir qismi to'langan — qolgani hamon qarz.</summary>
    Partial = 1,

    /// <summary>To'liq to'langan.</summary>
    Paid = 2,

    /// <summary>Kechirilgan (pul olinmagan, lekin qarz ham emas).</summary>
    Waived = 3,
}

/// <summary>Chegirma turi.</summary>
public enum DiscountKind
{
    /// <summary>Foizda (0..100).</summary>
    Percent = 0,

    /// <summary>Qat'iy summada (so'm).</summary>
    Amount = 1,
}

/// <summary>Moliya jurnalidagi yozuv turi.</summary>
public enum PaymentTransactionKind
{
    /// <summary>Naqd/karta orqali tushgan to'lov.</summary>
    Payment = 0,

    /// <summary>Qaytarilgan pul.</summary>
    Refund = 1,

    /// <summary>Kechirim (pul tushmagan).</summary>
    Waiver = 2,

    /// <summary>Balansdagi ortiqcha puldan yopilgan qarz.</summary>
    BalanceUse = 3,

    /// <summary>
    /// Allaqachon hisoblangan darsning yechilgan summasi bekor qilinib,
    /// balansga qaytarildi (dars keyinchalik bepul/sababli deb belgilandi).
    /// <see cref="Refund"/> DAN FARQ QILADI: bu yerda kassaga pul TUSHMAGAN
    /// va HALI ham tushmaydi — bu shunchaki "billing" tuzatishi.
    /// </summary>
    LessonReversal = 4,
}

/// <summary>
/// Bir dars ulushi NEGA yechilmagani (2026-08-16). <c>LessonCharge.
/// SkipReason</c> da — <c>null</c> "to'liq yechilgan" degani.
/// </summary>
public enum LessonChargeSkipReason
{
    /// <summary>O'quvchi shu darsga individual "sababli" deb belgilangan.</summary>
    Excused = 0,

    /// <summary>Butun dars "bepul" deb belgilangan — barcha o'quvchiga baravar.</summary>
    Free = 1,
}

/// <summary>
/// Qarzdorlik uchun bloklash qamrovi. Ierarxik: keyingisi oldingisini
/// O'Z ICHIGA OLADI (<c>Platform</c> hamma narsani yopadi).
/// </summary>
public enum PaymentBlockScope
{
    /// <summary>Bloklash o'chiq.</summary>
    None = 0,

    /// <summary>Faqat video darslar (eng avval yopiladi).</summary>
    Video = 1,

    /// <summary>Video + jonli darsga kirish.</summary>
    Live = 2,

    /// <summary>Butun platforma.</summary>
    Platform = 3,
}

/// <summary>
/// To'lov usuli.
///
/// ATAYLAB IKKITA (qaror, 2026-07-30): markaz amalda faqat naqd va karta
/// qabul qiladi. Erkin satr bo'lganda `"naqd"`, `"cash"`, `"CASH"` uchalasi
/// ham yozilib, kunlik kassa hisoboti usul bo'yicha BO'LINMAY qolardi —
/// eski tizimda aynan shunday edi. Yangi usul qo'shilsa (Click, Payme) shu
/// yerga qo'shiladi va hisobot avtomatik ajratadi.
/// </summary>
public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
}
