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
