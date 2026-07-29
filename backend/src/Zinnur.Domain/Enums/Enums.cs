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
