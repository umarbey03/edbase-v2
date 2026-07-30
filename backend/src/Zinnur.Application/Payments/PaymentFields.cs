namespace Zinnur.Application.Payments;

/// <summary>
/// <c>User</c> ning MOLIYAGA oid SOYA (shadow) ustuni.
///
/// NIMA UCHUN SOYA USTUN: bloklash qoidasi
/// (<c>Zinnur.Domain.Finance.PaymentBlockPolicy.IsBlocked</c>) <c>exempt</c>
/// argumentini talab qiladi — "bu o'quvchiga blok tushmaydi" (eski tizimdagi
/// <c>users.payment_exempt</c>). <c>Zinnur.Domain.Entities.User</c> da bunday
/// maydon yo'q va Domain qatlami bu ish doirasida O'ZGARTIRILMAYDI.
///
/// Soya ustun EF Core imkoniyati: ustun BAZADA bor va migratsiyaga tushadi,
/// lekin entity sinfida property sifatida ko'rinmaydi. Naqsh
/// <see cref="Zinnur.Application.Groups.GroupMemberFields"/> bilan bir xil.
///
/// ⚠️ KEYINGI QADAM: Domain navbatdagi marta o'zgartirilganda bu ustun
/// <c>User.PaymentExempt</c> haqiqiy property'siga ko'chirilsin — ustun nomi
/// va turi bir xil bo'lgani uchun migratsiya talab qilinmaydi.
/// </summary>
public static class PaymentFields
{
    /// <summary>Bloklashdan istisno (Postgres <c>boolean</c>, standart <c>false</c>).</summary>
    public const string Exempt = "PaymentExempt";
}
