namespace Zinnur.Application.StudentNotes.Dtos;

/// <summary>
/// Xodimning o'quvchi haqidagi ichki izohi.
///
/// 🔴 Bu shakl O'QUVCHIGA HECH QACHON YUBORILMAYDI — <c>Student</c> roli
/// izohlar endpointidan 403 oladi va profil agregatida <c>notes</c> bloki
/// <c>null</c> bo'ladi (sabab <c>StudentNote</c> entity izohida).
/// </summary>
/// <param name="CanEdit">
/// SO'ROVCHI shu izohni tahrirlay/o'chira oladimi. Frontend tugmani shu
/// maydon bo'yicha ko'rsatadi, LEKIN u faqat KO'RINISH uchun: haqiqiy
/// tekshiruv serverda, har <c>PUT</c>/<c>DELETE</c> da qaytadan bajariladi.
/// </param>
public sealed record StudentNoteDto(
    long Id,
    long StudentId,
    string Body,
    long AuthorId,
    string AuthorName,
    long? GroupId,
    string? GroupName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool CanEdit);

/// <summary>Yangi izoh.</summary>
/// <param name="GroupId">
/// Ixtiyoriy kontekst: "qaysi guruhdagi xatti-harakati haqida". Berilsa
/// o'quvchining SHU guruhda a'zoligi tekshiriladi — begona guruh Id'si
/// yozib qo'yilsa izoh ro'yxatida chalg'ituvchi nom ko'rinardi.
/// </param>
public sealed record CreateStudentNoteRequest(string Body, long? GroupId = null);

/// <summary>
/// Izohni tahrirlash.
///
/// ★ Faqat MATN o'zgaradi: guruh konteksti va muallif o'zgarmaydi (sabab
/// <c>StudentNote.Edit</c> izohida). Shu sababli bu <c>PUT</c> uchun
/// "to'liq almashtirish" tuzog'i (6-bo'lim, 1-tuzoq) xavf tug'dirmaydi —
/// almashtiriladigan yagona maydon shu.
/// </summary>
public sealed record UpdateStudentNoteRequest(string Body);
