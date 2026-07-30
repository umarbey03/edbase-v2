namespace Zinnur.Application.Progress.Dtos;

/// <summary>
/// Davomat hisobi: eski ilovadagi doira aynan shu uch sonni ko'rsatadi.
/// </summary>
/// <param name="Total">O'TILGAN (yakunlangan) darslar soni.</param>
/// <param name="Attended">Qatnashgan (present/late/partial).</param>
/// <param name="Missed">Qoldirgan (absent yoki umuman kirmagan).</param>
/// <param name="Percent">Qatnashish foizi 0..100. Dars o'tilmagan bo'lsa 0.</param>
public sealed record AttendanceBucketDto(
    int Total,
    int Attended,
    int Missed,
    decimal Percent);

/// <summary>
/// O'quvchining davomat xulosasi.
/// </summary>
/// <param name="GroupIds">Qaysi guruh(lar) hisobga olindi.</param>
/// <param name="From">Oraliq boshi (mahalliy sana). <c>null</c> — butun tarix.</param>
/// <param name="To">Oraliq oxiri (mahalliy sana, KIRADI). <c>null</c> — bugungacha.</param>
/// <param name="Overall">Barcha darslar (ustoz + kurator).</param>
/// <param name="Teacher">Faqat USTOZ darslari — reyting davomat foizi shundan olinadi.</param>
/// <param name="Assistant">Faqat KURATOR darslari.</param>
/// <param name="Streak">
/// Ketma-ket qatnashish seriyasi: eng oxirgi o'tilgan darsdan orqaga qarab
/// uzluksiz qatnashganlar soni. Birinchi qoldirilgan darsda uziladi.
/// </param>
public sealed record AttendanceSummaryDto(
    IReadOnlyList<long> GroupIds,
    DateOnly? From,
    DateOnly? To,
    AttendanceBucketDto Overall,
    AttendanceBucketDto Teacher,
    AttendanceBucketDto Assistant,
    int Streak);
