using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Finance;

/// <summary>Taqsimlash natijasi.</summary>
/// <param name="Applied">Qarzlarga haqiqatan tushgan summa.</param>
/// <param name="MonthsClosed">To'liq yopilgan oylar soni.</param>
/// <param name="MonthsPartial">Qisman yopilgan oy (0 yoki 1).</param>
/// <param name="Leftover">Qarzlardan ortib qolgan pul (balansga tushadi).</param>
/// <param name="TouchedIds">O'zgargan yozuvlar — audit va javob uchun.</param>
public sealed record AllocationResult(
    decimal Applied,
    int MonthsClosed,
    int MonthsPartial,
    decimal Leftover,
    IReadOnlyList<long> TouchedIds);

/// <summary>Qaytarish natijasi.</summary>
/// <param name="FromBalance">Balansdan yechilgan qism.</param>
/// <param name="FromPayments">Oylardan qaytarilgan qism.</param>
/// <param name="Unreturned">Qaytarib bo'lmagan qoldiq (yetarli pul topilmadi).</param>
public sealed record ReversalResult(
    decimal FromBalance,
    decimal FromPayments,
    decimal Unreturned,
    IReadOnlyList<long> TouchedIds)
{
    public decimal Returned => FromBalance + FromPayments;
}

/// <summary>
/// Pulni oylarga taqsimlash va orqaga qaytarish — moliyaning YURAGI.
///
/// Sof funksiya sifatida yozilgan (baza, HTTP, vaqt manbasi yo'q): yozuvlar
/// ro'yxati va summa kiradi, o'zgargan holat chiqadi. Shu sababli bu qoidalar
/// bazasiz, sekundlarda test qilinadi — eski tizimda esa ular servis ichida
/// SQL bilan aralashib ketgani uchun umuman test qilinmagan va aynan shu
/// yerda eng qimmat xato yashagan.
///
/// ★ ASOSIY QOIDA: pul QANCHA bo'lsa, SHUNCHA qarz yopiladi. Hech qanday
/// "kamida bitta oy" yaxlitlash YO'Q.
/// </summary>
public static class PaymentAllocator
{
    /// <summary>
    /// Kelgan summani ENG ESKI qarzdan boshlab ketma-ket yopadi.
    ///
    /// <paramref name="openPayments"/> — shu o'quvchining yopilmagan yozuvlari.
    /// Tartib SHU YERDA quriladi (davr bo'yicha, keyin Id): chaqiruvchi
    /// noto'g'ri tartibda bersa ham eng eski qarz birinchi yopiladi. Ilgari
    /// tartib SQL <c>ORDER BY</c> ga tayangan edi va uni buzish uchun bitta
    /// so'rovni o'zgartirish kifoya edi.
    /// </summary>
    public static AllocationResult Allocate(
        IEnumerable<Payment> openPayments,
        decimal amount,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(openPayments);

        if (amount <= 0)
            throw new DomainException("To'lov summasi musbat bo'lishi kerak.");

        var ordered = openPayments
            .Where(p => p.IsOpen && p.Outstanding > 0)
            .OrderBy(p => p.PeriodValue)
            .ThenBy(p => p.Id)
            .ToList();

        var left = amount;
        var closed = 0;
        var partial = 0;
        var touched = new List<long>();

        foreach (var payment in ordered)
        {
            if (left <= 0) break;

            var before = payment.Outstanding;
            var take = payment.ApplyPayment(left, now);
            if (take <= 0) continue;

            left -= take;
            touched.Add(payment.Id);

            if (take >= before) closed++;
            else partial++;
        }

        // ★ Ortib qolgan pul YO'QOLMAYDI: chaqiruvchi uni balansga qo'shadi.
        // Eski tizimda u shunchaki e'tibordan chetda qolardi.
        return new AllocationResult(amount - left, closed, partial, left, touched);
    }

    /// <summary>
    /// Balansdagi pulni ochiq qarzlarga sarflaydi (eng eskidan).
    ///
    /// Yangi oy yozuvlari yaratilgandan KEYIN chaqiriladi — shunda oldindan
    /// to'lagan o'quvchi keyingi oy "qarzdor" bo'lib chiqmaydi va bloklanmaydi.
    /// </summary>
    public static AllocationResult ConsumeBalance(
        StudentAccount account,
        IEnumerable<Payment> openPayments,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(openPayments);

        if (account.Balance <= 0)
            return new AllocationResult(0m, 0, 0, 0m, []);

        var payments = openPayments.ToList();
        var need = payments.Where(p => p.IsOpen).Sum(p => p.Outstanding);
        if (need <= 0)
            return new AllocationResult(0m, 0, 0, 0m, []);

        // Balansdan FAQAT kerak bo'lgan qismini olamiz: ortig'ini yechib,
        // keyin qaytarib qo'yish ikki audit yozuvi qoldirardi va hisobotda
        // bo'lmagan harakat ko'rinardi.
        var taken = account.Withdraw(Math.Min(account.Balance, need), now);
        if (taken <= 0)
            return new AllocationResult(0m, 0, 0, 0m, []);

        var result = Allocate(payments, taken, now);

        // Nazariy jihatdan qoldiq bo'lmasligi kerak (kerakdan ortiq olmadik),
        // lekin bo'lsa — balansga QAYTARAMIZ, aks holda pul yo'qolardi.
        if (result.Leftover > 0)
            account.Deposit(result.Leftover, now);

        return result;
    }

    /// <summary>
    /// Pulni ORQAGA qaytaradi.
    ///
    /// Tartib ATAYLAB shunday:
    ///   1) avval BALANSdan (u yerdagi pul hali hech qaysi oyga tegmagan);
    ///   2) qolgani ENG YANGI to'langan oylardan — eski oylar yopiq qolsin,
    ///      aks holda o'quvchi bir necha oy oldin bloklangan holatga qaytardi.
    /// </summary>
    public static ReversalResult Reverse(
        StudentAccount? account,
        IEnumerable<Payment> payments,
        decimal amount,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payments);

        if (amount <= 0)
            throw new DomainException("Qaytariladigan summa musbat bo'lishi kerak.");

        var left = amount;
        var fromBalance = 0m;

        if (account is not null && account.Balance > 0)
        {
            fromBalance = account.Withdraw(Math.Min(account.Balance, left), now);
            left -= fromBalance;
        }

        var touched = new List<long>();
        var fromPayments = 0m;

        var ordered = payments
            .Where(p => p.PaidAmount > 0 && p.Status != PaymentStatus.Waived)
            .OrderByDescending(p => p.PeriodValue)
            .ThenByDescending(p => p.Id)
            .ToList();

        foreach (var payment in ordered)
        {
            if (left <= 0) break;

            var given = payment.Reverse(left, now);
            if (given <= 0) continue;

            left -= given;
            fromPayments += given;
            touched.Add(payment.Id);
        }

        // Qoldiq qolsa — bu XATO EMAS, balki xodimga aytiladigan fakt:
        // qaytarilmoqchi bo'lgan summa tizimda umuman tushmagan bo'lishi
        // mumkin. Jimgina "qaytarildi" deb yozish hisobni buzardi.
        return new ReversalResult(fromBalance, fromPayments, left, touched);
    }
}
