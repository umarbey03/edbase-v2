namespace Zinnur.Application.Students.Services;

/// <summary>
/// "O'quvchi nechta darsni HAQIQATAN o'tagan" — sinov (probniy) davrini
/// aniqlashning YAGONA manbai (2026-08-18).
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN ALOHIDA PORT: bu qoidani IKKI joy o'qiydi —
///   • <c>GroupService</c> (a'zolik hodisasi yozilayotganda suratga oladi);
///   • <c>StudentStatsService</c> ("Foydalanuvchilar" paneli kartalari).
/// Ikki nusxada yozilsa, panel bir raqamni, to'kilishlar hisoboti esa
/// boshqasini ko'rsatardi.
///
/// ══════════════════════════════════════════════════════════════════════
/// 🔴 NIMA UCHUN DAVOMAT, `JoinedAt` EMAS (2026-08-18 da to'g'irlandi)
///
/// Ilgari hisob "a'zolikka qo'shilgandan keyingi yakunlangan darslar"
/// edi. Uning ikkita jiddiy nuqsoni bor edi:
///
///   1) <c>GroupMember.JoinedAt</c> o'quvchi guruhga QAYTA qo'shilganda
///      yoki boshqa guruhga ko'chirilganda BUGUNGI sanaga tushadi —
///      ya'ni 4 ta darsda qatnashgan o'quvchi jimgina "1 dars" ga
///      aylanardi va abadiy "probniy" bo'lib qolardi;
///   2) u dars O'TILGANINI bildirardi, o'quvchi UNDA QATNASHGANINI emas.
///
/// Davomat yozuvi esa aynan "o'quvchi shu darsda bo'ldi" degan faktni
/// saqlaydi va u dars yakunlanganda bir marta yoziladi
/// (<c>Attendance.Finalize</c>) — qayta qo'shilish uni o'chirmaydi.
///
/// ★ FAQAT USTOZ DARSLARI: kurator mashg'ulotlari kurs mavzusini
/// oldinga surmaydi (gating'dagi AYNI qoida — <c>GatingService</c>
/// sur'atni ham faqat <c>SessionType.Teacher</c> bo'yicha sanaydi).
///
/// ★ YO'Q (Absent) SANALMAYDI: "8 darsdan to'kilmasdan O'QIB KETGAN"
/// degani — o'quvchi o'sha darslarda BO'LGAN. Kelmagan dars uni sinovdan
/// o'tkazmaydi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public interface IStudiedLessonCounter
{
    /// <summary>Bitta o'quvchi uchun.</summary>
    Task<int> CountAsync(long studentId, CancellationToken ct = default);

    /// <summary>
    /// Ko'p o'quvchi uchun BITTA so'rovda. Yozuvi yo'q o'quvchi natijada
    /// UMUMAN bo'lmaydi (chaqiruvchi uni 0 deb o'qiydi).
    /// </summary>
    Task<IReadOnlyDictionary<long, int>> CountManyAsync(
        IReadOnlyCollection<long> studentIds, CancellationToken ct = default);
}
