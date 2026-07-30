namespace Zinnur.Application.Groups;

/// <summary>
/// <c>GroupMember</c> ning SOYA (shadow) ustunlari.
///
/// NIMA UCHUN SOYA USTUN: pauzani muddat bilan qo'yish
/// (<c>POST /groups/{id}/members/{studentId}/pause</c> — <c>pausedUntil</c>)
/// talab qilinadi, lekin <c>Zinnur.Domain.Entities.GroupMember</c> da bunday
/// maydon yo'q va Domain qatlami bu ish doirasida O'ZGARTIRILMAYDI.
///
/// Soya ustun EF Core imkoniyati: ustun BAZADA bor va migratsiyaga tushadi,
/// lekin entity sinfida property sifatida ko'rinmaydi. Shu tufayli endpoint
/// shartnomasi to'liq bajariladi va ma'lumot JIMGINA YO'QOLMAYDI (aks holda
/// klient sana yuborib, u hech qayerda saqlanmasdi).
///
/// ⚠️ KEYINGI QADAM: Domain qatlami navbatdagi marta o'zgartirilganda bu
/// ustun <c>GroupMember.PausedUntil</c> haqiqiy property'siga ko'chirilsin —
/// o'sha paytda bu fayl o'chiriladi va migratsiya talab qilinmaydi
/// (ustun nomi va turi bir xil bo'lgani uchun).
/// </summary>
public static class GroupMemberFields
{
    /// <summary>Pauza qachongacha (Postgres <c>date</c>, <c>DateOnly?</c>).</summary>
    public const string PausedUntil = "PausedUntil";
}
