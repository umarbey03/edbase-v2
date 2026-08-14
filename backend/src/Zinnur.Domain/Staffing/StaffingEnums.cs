namespace Zinnur.Domain.Staffing;

/// <summary>
/// Guruhning IKKI xodim o'rindig'idan qaysi biri mas'ul (R33 + R40).
/// </summary>
/// <remarks>
/// 🔴 TARTIB MUHIM: qiymatlar bazaga <c>int</c> sifatida yoziladi. Yangi
/// qiymat FAQAT oxiriga qo'shiladi, mavjud raqamlar hech qachon
/// o'zgartirilmaydi.
///
/// ★ <see cref="Both"/> NEGA 0: nol — bazaning tabiiy standarti, va
/// baholashda AYNAN u bugungi xatti-harakat (ustoz ham, kurator ham
/// baholaydi). Shu tanlov tufayli migratsiya mavjud qatorlarni
/// o'zgartirmaydi va baholash bit-to-bit o'zgarishsiz qoladi.
///
/// ★ <see cref="UserRole"/> QAYTA ISHLATILMADI (u yerda ham
/// <c>Teacher</c>/<c>Assistant</c> bor): u FOYDALANUVCHINING roli va
/// ichida <c>Student</c>, <c>Admin</c> ham bor. O'sha enum ishlatilsa
/// "vazifani <c>Student</c> tekshiradi" degan yozuv bazada BEMALOL
/// saqlanardi va uni har o'qish joyida qo'lda rad etish kerak bo'lardi.
/// Bu yerda esa MUMKIN BO'LGAN qiymatlarning O'ZI uchta.
/// </remarks>
public enum GroupStaffRole
{
    /// <summary>Ikkalasi ham — ustoz va kurator (baholashning bugungi holati).</summary>
    Both = 0,

    /// <summary>Faqat ustoz o'rindig'i.</summary>
    Teacher = 1,

    /// <summary>Faqat kurator (yordamchi) o'rindig'i — savollarning bugungi holati.</summary>
    Assistant = 2,
}

/// <summary>
/// Mas'uliyat QAYSI ISH uchun so'ralyapti.
/// </summary>
/// <remarks>
/// ★ BAZAGA YOZILMAYDI — bu faqat so'rov parametri. Har ish uchun
/// guruhda O'Z ustuni bor (<c>Group.AssignmentGraderRole</c>,
/// <c>Group.QuestionResponderRole</c>), bu enum esa qaysi ustunni o'qishni
/// va zaxira yo'l ishlashini belgilaydi.
/// </remarks>
public enum StaffDuty
{
    /// <summary>
    /// «Bu o'quvchi umuman mening qamrovimdami» — O'RINDIQNI AJRATMAYDI.
    ///
    /// Javob faylini ochish, vazifalar ro'yxatini ko'rish shu darajada
    /// qoladi: R33 "kim TEKSHIRADI" ni so'radi, "kim KO'RADI" ni emas.
    /// Bu bugungi ifodaning aynan o'zi va u ATAYLAB dinamik emas.
    /// </summary>
    Access = 0,

    /// <summary>Topshirilgan ishni baholash va qayta ochish (R33).</summary>
    Grading = 1,

    /// <summary>O'quvchining shaxsiy savoliga javob berish (R40).</summary>
    Questions = 2,
}
