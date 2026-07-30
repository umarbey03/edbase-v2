using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// O'quvchining BALANSI — ortiqcha to'langan pul shu yerda turadi va
/// keyingi oy qarzi paydo bo'lganda ishlatiladi.
///
/// ★ NIMA UCHUN KERAK: eski tizimda oyning summasidan ortiq pul kelsa,
/// ortiqcha qism JIM YO'QOLARDI — tizim uni hech qayerga yozmasdi. Ota-ona
/// "3 oyga oldindan to'ladim" desa, keyingi oy o'quvchi qarzdor bo'lib
/// chiqardi va bloklanardi.
///
/// NIMA UCHUN ALOHIDA ENTITY, <c>User.Balance</c> EMAS: balans — MOLIYA
/// tushunchasi. Uni foydalanuvchi qatoriga qo'shish har profil o'zgarishida
/// pul maydonini ham qo'lga tushirardi (bizda `PUT` to'liq almashtirish
/// semantikasi — aynan shunday jimgina yo'qotish xatosi bugun topilgan).
/// </summary>
public class StudentAccount : BaseEntity
{
    public long StudentId { get; set; }

    public User? Student { get; set; }

    /// <summary>Joriy balans. HECH QACHON manfiy bo'lmaydi.</summary>
    public decimal Balance { get; set; }

    /// <summary>Balansga pul qo'shadi (ortiqcha to'lov yoki qaytarilgan pul).</summary>
    public void Deposit(decimal amount, DateTimeOffset now)
    {
        if (amount <= 0)
            throw new DomainException("Balansga qo'shiladigan summa musbat bo'lishi kerak.");

        Balance += amount;
        UpdatedAt = now;
    }

    /// <summary>
    /// Balansdan pul yechadi va HAQIQATAN yechilgan summani qaytaradi.
    ///
    /// So'ralganidan kam bo'lishi MUMKIN (balansda yetarli pul bo'lmasa) —
    /// bu xato emas, chaqiruvchi qolganini boshqa manbadan oladi. Balans
    /// manfiyga tushmaydi: manfiy balans "yashirin qarz" bo'lib, qarz
    /// hisobotida ko'rinmay qolardi.
    /// </summary>
    public decimal Withdraw(decimal amount, DateTimeOffset now)
    {
        if (amount <= 0)
            throw new DomainException("Balansdan yechiladigan summa musbat bo'lishi kerak.");

        var take = Math.Min(amount, Balance);
        if (take <= 0) return 0m;

        Balance -= take;
        UpdatedAt = now;
        return take;
    }
}
