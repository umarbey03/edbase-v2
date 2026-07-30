using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Progress;

/// <summary>
/// Davomat xulosasi: qatnashgan / qoldirgan / jami va foiz.
/// Eski ilovadagi doira aynan shu uch sonni ko'rsatadi.
/// </summary>
/// <param name="Total">O'TILGAN (yakunlangan) darslar soni.</param>
/// <param name="Attended">Qatnashgan darslar soni.</param>
public readonly record struct AttendanceTally(int Total, int Attended)
{
    public static readonly AttendanceTally Empty = new(0, 0);

    /// <summary>Qoldirilgan darslar.</summary>
    public int Missed => Total - Attended;

    /// <summary>
    /// Qatnashish foizi (0..100). Dars o'tilmagan bo'lsa 0 —
    /// "0 tadan 0 ta" holati 100% deb ko'rsatilsa o'quvchi hali
    /// boshlanmagan kursda "mukammal davomat" ko'rardi.
    /// </summary>
    public decimal Percent => LeaderboardScore.Percent(Attended, Total);

    public AttendanceTally Add(bool attended) =>
        new(Total + 1, attended ? Attended + 1 : Attended);
}

/// <summary>
/// Davomat bo'yicha sof hisoblar. Bazasiz — kirish sifatida faqat
/// darslar ro'yxati va ularning holati keladi.
/// </summary>
public static class AttendanceMath
{
    /// <summary>
    /// Ketma-ket qatnashish seriyasi ("streak").
    ///
    /// <paramref name="newestFirst"/> — o'tilgan darslar YANGIDAN ESKIGA
    /// tartibda; element <c>null</c> bo'lsa o'quvchida umuman davomat
    /// yozuvi yo'q (ya'ni qatnashmagan).
    ///
    /// ★ NIMA UCHUN "null = qoldirgan": davomat qatori faqat xonaga
    /// KIRGAN o'quvchi uchun yaratiladi. Eski tizim `outerjoin` bilan
    /// aynan shunday hisoblardi — yozuv yo'qligi "hali baholanmagan"
    /// emas, "kelmagan" degani.
    /// </summary>
    public static int Streak(IEnumerable<AttendanceStatus?> newestFirst)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);

        var streak = 0;

        foreach (var status in newestFirst)
        {
            if (status is not { } value || !IsAttended(value))
                break;

            streak++;
        }

        return streak;
    }

    /// <summary>
    /// Qaysi holat "qatnashgan" hisoblanadi.
    ///
    /// Eski tizim bilan AYNAN bir xil: <c>present</c>, <c>late</c>,
    /// <c>partial</c> — qatnashgan; faqat <c>absent</c> — yo'q.
    /// Ya'ni kechikkan yoki yarim qatnashgan o'quvchi butunlay
    /// kelmagan bilan tenglashtirilmaydi.
    /// </summary>
    public static bool IsAttended(AttendanceStatus status) =>
        status != AttendanceStatus.Absent;
}
