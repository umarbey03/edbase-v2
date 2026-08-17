namespace Zinnur.Application.Students.Dtos;

/// <summary>
/// "MENING GURUHIM" OYNASI (2026-08-17) — bosh sahifadagi karta/tugma
/// bosilganda ochiladigan modal. Telegram "chat info" ekraniga o'xshash:
/// guruh nomi, ustoz/kurator ismi va guruhdoshlar ro'yxati.
///
/// ⚠️ FAQAT ISM-FAMILIYA — telefon, email yoki Telegram username BU
/// YERDA YO'Q. O'quvchilar bir-birining kontaktini ko'rmaydi (bu
/// o'quv bo'limi/kurator vositasi emas, shunchaki "kim bilan
/// o'qiyapman" degan savolga javob).
/// </summary>
public sealed record ClassroomMemberDto(long Id, string FullName);

/// <summary>Bitta guruh — o'quvchi bir nechta guruhda bo'lishi mumkin.</summary>
/// <param name="TeacherName">Ustoz biriktirilmagan bo'lsa <c>null</c>.</param>
/// <param name="CuratorName">
/// Kurator — to'g'ridan-to'g'ri (<c>Group.AssistantId</c>) yoki bog'langan
/// kurator guruhi (<c>Group.CuratorGroupId</c>) orqali. Ikkalasi ham
/// bo'lmasa <c>null</c>.
/// </param>
/// <param name="Classmates">Guruhdoshlar (o'zi HISOBGA OLINMAYDI).</param>
public sealed record ClassroomGroupDto(
    long GroupId,
    string GroupName,
    string? TeacherName,
    string? CuratorName,
    IReadOnlyList<ClassroomMemberDto> Classmates);

/// <summary>
/// <c>GET /api/v1/students/me/classroom</c> javobi.
/// </summary>
/// <param name="SupportContact">
/// Muammo/fikr-taklif uchun bog'lanish (`general.support_contact`
/// sozlamasidan). Sozlanmagan bo'lsa <c>null</c> — ekran qatorni
/// umuman ko'rsatmaydi.
/// </param>
public sealed record ClassroomDto(
    IReadOnlyList<ClassroomGroupDto> Groups,
    string? SupportContact);
