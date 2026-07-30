namespace Zinnur.Domain.Common;

/// <summary>
/// Mahalliy "devor-soati" ni aniq UTC instant'ga aylantiradi.
///
/// NIMA UCHUN ALOHIDA: bu konvertatsiya ikki joyda kerak —
/// dars jadvali generatsiyasida (<c>ScheduleGenerator</c>) va oylik
/// reyting oralig'ini hisoblashda (<c>BillingPeriod.UtcRange</c>).
/// Ikkalasida ham DST tuzog'i bir xil, shuning uchun qoida BITTA joyda.
///
/// Domain'da tashqi bog'liqlik YO'Q: zona ARGUMENT sifatida keladi,
/// <c>TimeZoneInfo.Local</c> hech qachon ishlatilmaydi (konteyner UTC'da
/// ishlaydi — mahalliy zona 5 soatga siljigan jadval bergan bo'lardi).
/// </summary>
public static class LocalWallClock
{
    /// <summary>
    /// <paramref name="date"/> + <paramref name="time"/> mahalliy devor-vaqtini
    /// UTC instant'ga aylantiradi.
    ///
    /// NIMA UCHUN SHUNCHAKI <c>DateTime.SpecifyKind</c> EMAS: yozgi/qishki
    /// vaqt (DST) o'tishida mahalliy soat MAVJUD BO'LMASLIGI mumkin.
    /// Toshkentda DST yo'q, lekin bu mantiq boshqa mintaqada ham
    /// ishlatilishi mumkin — shuning uchun to'g'ri ishlov beramiz.
    /// </summary>
    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        // DST tufayli mavjud bo'lmagan soat (masalan 02:30 -> 03:30 sakragan):
        // bir soat oldinga suramiz, aks holda konvertatsiya xato beradi.
        if (timeZone.IsInvalidTime(local))
            local = local.AddHours(1);

        var offset = timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>Kun boshi (00:00) — oraliq chegaralari uchun.</summary>
    public static DateTimeOffset StartOfDayUtc(DateOnly date, TimeZoneInfo timeZone) =>
        ToUtc(date, TimeOnly.MinValue, timeZone);

    /// <summary>UTC instant qaysi MAHALLIY kalendar kuniga tushadi.</summary>
    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);
    }
}
