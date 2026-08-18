namespace Zinnur.Application.Students.Dtos;

/// <summary>
/// O'QUVCHILAR BO'YICHA UMUMIY KO'RSATKICHLAR (2026-08-18) —
/// "Foydalanuvchilar" panelining tepasidagi kartalar uchun.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ BU JADVAL FILTRIGA BOG'LIQ EMAS — MARKAZ BO'YICHA UMUMIY.
///
/// Paneldagi filtrlar (rol, guruh, qidiruv) jadvalga tegishli; kartalar
/// esa har doim BUTUN markazning o'quvchilar manzarasini ko'rsatadi.
/// Sabab: filtr `rol = Ustoz` ga qo'yilsa "probniy" yoki "pauza" degan
/// ko'rsatkich MA'NOSIZ bo'lardi — ular faqat o'quvchiga tegishli.
/// Shuning uchun kartalar sarlavhasi ham "Markaz bo'yicha" deb yoziladi.
///
/// ★ HAR O'QUVCHI BITTA MARTA sanaladi (a'zolik emas, ODAM). O'quvchining
/// bir guruhda chiqarilgan, boshqasida faol yozuvi bo'lishi mumkin —
/// bunday holatda u FAOL hisoblanadi (ustunlik tartibi:
/// faol → pauza → to'xtagan).
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Active">
/// Hozir o'qiyotgan va sinov davridan O'TGAN o'quvchilar
/// (<c>GroupMembershipEvent.TrialLessonCount</c> = 8 va undan ko'p dars).
/// </param>
/// <param name="Trial">
/// Hozir o'qiyotgan, lekin hali 8 darsni tugatmagan — "probniy/demo".
/// Loyiha egasi ta'rifi (2026-08-17): *"8 darsdan to'kilmasdan o'qib
/// ketgan o'quvchilar aktiv hisoblanadi, ungacha demo yoki probniy"*.
/// </param>
/// <param name="Paused">Muzlatilgan (vaqtincha to'xtatgan) o'quvchilar.</param>
/// <param name="Stopped">
/// Guruhdan chiqarilgan va hozir HECH QAYERDA faol bo'lmagan o'quvchilar.
/// </param>
/// <param name="ActiveLosses">
/// Sinovdan o'tgandan KEYIN (8+ dars o'tab) ketgan o'quvchilar soni —
/// markaz uchun eng qimmat yo'qotish turi.
///
/// ⚠️ MANBASI BOSHQA: bu ko'rsatkich a'zolik HOLATIDAN emas, o'chmaydigan
/// HODISA JURNALIDAN (<c>GroupMembershipEvent</c>) hisoblanadi — chunki
/// "ketgan paytda nechta dars o'tagan edi" faqat o'sha yerda suratga
/// olinadi. Jurnal 2026-08-17 dan boshlab yuritiladi, ya'ni undan
/// OLDINGI chiqishlar bu raqamga KIRMAYDI (lekin <see cref="Stopped"/>
/// ga kiradi).
/// </param>
/// <param name="WithoutGroup">
/// Hech qanday guruhga biriktirilmagan o'quvchilar — yuqoridagi
/// birorta turkumga kirmaydi, shuning uchun alohida ko'rsatiladi
/// (aks holda "jami" hech qachon to'g'ri chiqmasdi).
/// </param>
public sealed record StudentStatsDto(
    int Active,
    int Trial,
    int Paused,
    int Stopped,
    int ActiveLosses,
    int WithoutGroup);
