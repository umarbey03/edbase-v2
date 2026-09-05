using System.Globalization;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TUNGI OYNA — 00:00–09:00 (Asia/Tashkent)
/// ════════════════════════════════════════════════════════════════════════
///
/// Kodlash 3.5 yadroni yeydi. Uni dars vaqtida yurgizish LiveKit SFU va
/// API bilan AYNI 4 yadroni bo'lishish demakdir — ya'ni jonli darsning
/// sifati tungi montaj hisobiga tushardi. Shuning uchun ish faqat kechasi
/// bajariladi.
///
/// ── 🔴 OXIRIGA 30 DAQIQA QOLGANDA YANGI ISH BOSHLANMAYDI ────────────────
///
/// Boshlangan kodlash oyna tugashi bilan UZILADI va uning natijasi
/// TASHLANADI (yarim mp4 da <c>moov</c> atomi yo'q — davom ettirib
/// bo'lmaydi). Ya'ni 08:55 da boshlangan ish deyarli aniq behuda
/// sarflangan protsessor vaqti. Bu chegara o'sha behuda ishni to'sadi.
///
/// ⚠️ SIG'MAGAN ISH YO'QOLMAYDI: qator <c>Queued</c> holida qoladi va
/// KEYINGI kechada, eng eskisi birinchi bo'lib olinadi
/// (<c>IRecordingCompositionStore</c>). Bu loyiha egasining oshkor talabi,
/// optimizatsiya emas.
///
/// ── NIMA UCHUN SOF FUNKSIYA ─────────────────────────────────────────────
///
/// Yarim tunni kesib o'tadigan oyna (masalan 22:00–06:00), mahalliy vaqt
/// va "oxirigacha qancha qoldi" hisobi — uchalasi ham xatoga moyil va
/// ularni fon xizmatini yurgizmasdan tekshirishning yagona yo'li shu.
/// </summary>
public static class RecordingCompositionWindow
{
    /// <summary>SPEC §2.7 dagi standart boshlanish (<c>recordings.compose_window_start</c>).</summary>
    public static TimeOnly DefaultStart { get; } = new(0, 0);

    /// <summary>SPEC §2.7 dagi standart tugash (<c>recordings.compose_window_end</c>).</summary>
    public static TimeOnly DefaultEnd { get; } = new(9, 0);

    /// <summary>Oyna oxiriga shundan kam qolgan bo'lsa yangi ish boshlanmaydi.</summary>
    public static TimeSpan DefaultStartCutoff { get; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Sozlamadagi <c>HH:mm</c> matnini o'qiydi; buzuq qiymat
    /// <paramref name="fallback"/> ga tushadi.
    ///
    /// ★ ISTISNO TASHLAMAYDI — ATAYLAB. Sozlamalar panelidan kelgan bitta
    /// xato belgi butun tungi montajni yiqitmasligi kerak: bunday holda
    /// standart oyna ishlaydi va bu xodimga darhol ko'rinadi (yozuvlar
    /// baribir tayyor bo'ladi), yiqilgan fon xizmati esa ko'rinmasdi.
    /// </summary>
    public static TimeOnly Parse(string? value, TimeOnly fallback) =>
        TimeOnly.TryParseExact(
            value?.Trim(), "HH\\:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : fallback;

    /// <summary>
    /// Oynaning HOZIRGI holati.
    /// </summary>
    /// <param name="nowUtc">Joriy payt (UTC).</param>
    /// <param name="timeZone">
    /// Jadval zonasi (<c>IScheduleTimeZoneProvider</c>). Konteyner UTC'da
    /// ishlaydi, ya'ni <c>TimeZoneInfo.Local</c> ishlatilsa oyna besh
    /// soatga siljib, montaj AYNAN dars vaqtida ishlab turardi.
    /// </param>
    /// <param name="start">Oynaning boshlanishi (mahalliy).</param>
    /// <param name="end">Oynaning tugashi (mahalliy).</param>
    /// <param name="startCutoff">Oxirida yangi ish boshlanmaydigan zaxira.</param>
    public static CompositionWindow Evaluate(
        DateTimeOffset nowUtc,
        TimeZoneInfo timeZone,
        TimeOnly start,
        TimeOnly end,
        TimeSpan startCutoff)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        // ⚠️ BOSHI VA OXIRI TENG BO'LSA OYNA YOPIQ, "SUTKA BO'YI" EMAS.
        //    Ikkinchi talqin xato sozlamani ("00:00"–"00:00") jimgina
        //    "har doim kodla" ga aylantirardi va montaj tushlik paytida
        //    ishlab turardi — aynan bu quvur oldini olishi kerak bo'lgan
        //    narsa. Yopiq oyna esa darhol ko'rinadi: hech narsa
        //    yig'ilmaydi.
        if (start == end)
            return new CompositionWindow(false, false, nowUtc);

        var local = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var localTime = TimeOnly.FromDateTime(local.DateTime);

        // Yarim tunni kesib o'tadigan oyna (22:00–06:00) uchun shart
        // TESKARI bo'ladi — bu yagona farq.
        var isOpen = start < end
            ? localTime >= start && localTime < end
            : localTime >= start || localTime < end;

        var endsAtUtc = NextOccurrence(local, end, timeZone);

        return new CompositionWindow(
            IsOpen: isOpen,
            CanStart: isOpen && endsAtUtc - nowUtc > startCutoff,
            EndsAtUtc: endsAtUtc);
    }

    /// <summary>
    /// <paramref name="time"/> ning mahalliy vaqtdagi KEYINGI kelishi
    /// (UTC da).
    /// </summary>
    private static DateTimeOffset NextOccurrence(
        DateTimeOffset local, TimeOnly time, TimeZoneInfo timeZone)
    {
        var candidate = DateOnly.FromDateTime(local.DateTime).ToDateTime(time);

        if (candidate <= local.DateTime) candidate = candidate.AddDays(1);

        // ⚠️ Yoz vaqtiga o'tishda mahalliy soat "mavjud bo'lmagan" bo'lishi
        //    mumkin. Asia/Tashkent da bunday o'tish YO'Q, lekin zona
        //    sozlanadigan (`App:TimeZone`) va boshqa mintaqada bu
        //    `ConvertTimeToUtc` ni ISTISNO bilan yiqitardi.
        if (timeZone.IsInvalidTime(candidate)) candidate = candidate.AddHours(1);

        return new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), timeZone),
            TimeSpan.Zero);
    }
}

/// <summary>Tungi oynaning holati.</summary>
/// <param name="IsOpen">Hozir oyna ichidamizmi.</param>
/// <param name="CanStart">
/// YANGI ish boshlash mumkinmi — ya'ni oyna ochiq VA oxirigacha zaxiradan
/// ko'p vaqt bor.
/// </param>
/// <param name="EndsAtUtc">
/// Oyna qachon yopiladi. Kodlashning bekor qilish signali AYNAN shu paytga
/// qo'yiladi: 09:00 da ffmpeg to'xtaydi va qator navbatga qaytadi.
/// </param>
public sealed record CompositionWindow(bool IsOpen, bool CanStart, DateTimeOffset EndsAtUtc);
