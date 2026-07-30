using System.Globalization;
using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Finance;

/// <summary>
/// Hisob-kitob oyi (<c>YYYY-MM</c>).
///
/// NIMA UCHUN ALOHIDA TUR, SATR EMAS: eski tizimda davr oddiy satr edi
/// (<c>"2026-07"</c>) va u BIR NECHTA joyda qo'lda yasalardi. Bitta joyda
/// oldiga nol qo'yilmasa (<c>"2026-7"</c>) qator butunlay boshqa oyga tushardi
/// — satr solishtiruvida <c>"2026-7" &gt; "2026-12"</c> bo'ladi, ya'ni
/// "eng eski qarz" tartibi jimgina buzilardi va noto'g'ri oy yopilardi.
///
/// Bu tur formatni bitta joyda qulflaydi va taqqoslashni SON bo'yicha qiladi.
/// </summary>
public readonly record struct BillingPeriod : IComparable<BillingPeriod>
{
    private BillingPeriod(int year, int month)
    {
        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    public static BillingPeriod Create(int year, int month)
    {
        if (year is < 2000 or > 2200)
            throw new DomainException($"Yil oralig'i noto'g'ri: {year.ToString(CultureInfo.InvariantCulture)}.");

        if (month is < 1 or > 12)
            throw new DomainException($"Oy 1..12 bo'lishi kerak: {month.ToString(CultureInfo.InvariantCulture)}.");

        return new BillingPeriod(year, month);
    }

    /// <summary>
    /// Sanadan davr yasaydi. Sana MAHALLIY bo'lishi kerak (markaz vaqti):
    /// UTC'da 1-avgust 00:30 Toshkentda hali 31-iyul — davr esa mahalliy
    /// kalendar bo'yicha hisoblanadi.
    /// </summary>
    public static BillingPeriod FromDate(DateOnly localDate) =>
        new(localDate.Year, localDate.Month);

    public static BillingPeriod Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var parts = value.Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month))
        {
            throw new DomainException($"Davr formati noto'g'ri: '{value}'. Kutilgan format: YYYY-MM.");
        }

        return Create(year, month);
    }

    /// <summary>Keyingi/oldingi oy. Manfiy qiymat orqaga suradi.</summary>
    public BillingPeriod AddMonths(int months)
    {
        var total = ((Year * 12) + (Month - 1)) + months;
        return Create(total / 12, (total % 12) + 1);
    }

    /// <summary>Oyning birinchi kuni — hisobotlarda oraliq chegarasi sifatida.</summary>
    public DateOnly FirstDay() => new(Year, Month, 1);

    /// <summary>
    /// Oyning UTC oralig'i: <c>[boshi, keyingi oy boshi)</c> — chap chegara
    /// KIRADI, o'ng chegara KIRMAYDI.
    ///
    /// ★ NIMA UCHUN ZONA ARGUMENT: oy chegarasi MAHALLIY kalendar bo'yicha
    /// aniqlanadi. Toshkentda 1-avgust 00:00 — UTC'da 31-iyul 19:00. Oraliq
    /// to'g'ridan-to'g'ri UTC'da olinsa, 31-iyul kechqurungi (19:00 dan
    /// keyingi) dars, vazifa va test AVGUST oyiga tushib ketardi — ya'ni
    /// oyning oxirgi kunidagi mehnat keyingi oyning reytingiga yozilardi.
    ///
    /// ★ NIMA UCHUN O'NG CHEGARA "KIRMAYDI": klassik chegara xatosidan
    /// himoya. Chegara <c>23:59:59</c> deb yozilsa, o'sha oxirgi soniya
    /// ichidagi yozuv IKKI oyning HECH BIRIGA tushmay yo'qolardi.
    ///
    /// Moliya hisobotlari ham, oylik reyting ham shu YAGONA oraliqni
    /// ishlatadi — "oy" tushunchasi ikki xil bo'lib qolmasin.
    /// </summary>
    public (DateTimeOffset StartUtc, DateTimeOffset EndUtc) UtcRange(TimeZoneInfo timeZone) =>
        (LocalWallClock.StartOfDayUtc(FirstDay(), timeZone),
         LocalWallClock.StartOfDayUtc(AddMonths(1).FirstDay(), timeZone));

    public int CompareTo(BillingPeriod other) =>
        Year != other.Year ? Year.CompareTo(other.Year) : Month.CompareTo(other.Month);

    public static bool operator <(BillingPeriod left, BillingPeriod right) => left.CompareTo(right) < 0;

    public static bool operator >(BillingPeriod left, BillingPeriod right) => left.CompareTo(right) > 0;

    public static bool operator <=(BillingPeriod left, BillingPeriod right) => left.CompareTo(right) <= 0;

    public static bool operator >=(BillingPeriod left, BillingPeriod right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// <c>YYYY-MM</c>. Bazada ham AYNAN shu ko'rinishda saqlanadi — tartiblash
    /// satr bo'yicha ham to'g'ri ishlashi uchun oy ikki xonali.
    /// </summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}");
}
