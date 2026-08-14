using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Progress;

/// <summary>
/// ========================================================================
/// OYLIK REYTING BALI — ADOLATLI, FOIZLI MODEL
/// ========================================================================
///
/// Qoida ESKI TIZIMDAN olingan (<c>app/services/points_svc.py</c>,
/// 2026-07-20 da qayta yozilgan versiya) va o'zgartirilmagan:
///
///   • HAR OY toza start. Faqat shu oydagi darslar/vazifalar/testlar
///     hisobga olinadi — kech qo'shilgan o'quvchi ham birinchi o'rinni
///     ola oladi. (Absolyut ball modelida bu IMKONSIZ edi: yanvardan
///     o'qiyotgan o'quvchining ballini iyunda qo'shilgan hech qachon
///     quvib yetolmasdi.)
///
///   • TO'RT mezon, TENG vaznda, har biri 0..100 foiz:
///       – davomat%  = qatnashgan ustoz darslari / o'tilgan ustoz darslari
///       – vazifa%   = o'rtacha (baho / maksimal baho)
///       – test%     = o'rtacha (ball / maksimal ball)
///       – dars%     = o'rtacha (dars bahosi / maksimal ball)   ← R24
///
///     🔴 TO'RTINCHI MEZON 2026-08-14 DA QO'SHILDI (R24). QARORNING
///     SABABI: dars bahosi ustozning KUNDALIK baholash quroli
///     ("bugungi darsga 5") va uni reytingdan chiqarib qoldirish
///     jimgina yolg'on jadval yasardi — ustoz har kuni baho qo'yadi,
///     reyting esa ularni umuman ko'rmasdi. Vaznlar TENG qoldi:
///     dars bahosi vazifa bahosidan na kam, na ko'p ahamiyatli.
///
///     ★ ORQAGA MOSLIK BEPUL: dars bahosi yo'q oyda mezon `null`
///     bo'ladi va quyidagi qoida bo'yicha o'rtachaga UMUMAN KIRMAYDI —
///     ya'ni bu funksiyani ishlatmaydigan guruhning bali BIR ZARRA
///     ham o'zgarmaydi.
///
///   • ★ Yakuniy ball = MAVJUD mezonlar o'rtachasi. Elementi bo'lmagan
///     mezon o'rtachaga UMUMAN KIRMAYDI.
///
///     NIMA UCHUN AYNAN SHUNDAY: agar bo'sh mezon 0 deb olinsa, oyning
///     birinchi haftasida (hali test ham, vazifa ham berilmagan) hamma
///     o'quvchining bali 33 ga tushib qolardi va reyting mutlaqo
///     ma'nosiz bo'lardi. O'quvchi O'ZI bajarmagan ish uchun emas,
///     faqat MARKAZ hali bermagan ish uchun jazolanmasligi kerak.
///
///   • Hech bir mezon bo'lmasa — 0.
///
/// SOF FUNKSIYA: bazaga ham, joriy vaqtga ham bog'liq emas. Shu tufayli
/// butun hisob bazasiz test qilinadi.
/// </summary>
/// <param name="StudentId">O'quvchi.</param>
/// <param name="StudentName">Ko'rinadigan ism (teng balda tartib uchun ham kerak).</param>
/// <param name="AttendancePercent">Davomat foizi. <c>null</c> — shu oyda o'tilgan dars YO'Q.</param>
/// <param name="AssignmentPercent">Vazifa foizi. <c>null</c> — shu oyda baholangan vazifa YO'Q.</param>
/// <param name="TestPercent">Test foizi. <c>null</c> — shu oyda topshirilgan test YO'Q.</param>
/// <param name="LessonPercent">
/// Dars bahosi foizi (R24). <c>null</c> — shu oyda dars bahosi YO'Q.
///
/// ★ OXIRGI POZITSIYADA VA STANDART QIYMAT BILAN — ataylab: mavjud
/// chaqiruvlar (<c>new LeaderboardScore(id, ism, a, b, c)</c>) o'zgarishsiz
/// kompilyatsiya bo'ladi va yangi mezonni "unutgan" joy jimgina noto'g'ri
/// ARGUMENTGA emas, `null` ga (ya'ni "mezon yo'q" ga) tushadi.
/// </param>
public sealed record LeaderboardScore(
    long StudentId,
    string StudentName,
    decimal? AttendancePercent,
    decimal? AssignmentPercent,
    decimal? TestPercent,
    decimal? LessonPercent = null)
{
    /// <summary>Foizlar bir xonali kasr bilan saqlanadi (78.4).</summary>
    public const int PercentDecimals = 1;

    /// <summary>Yakuniy ball (0..100).</summary>
    public decimal Total =>
        Combine(AttendancePercent, AssignmentPercent, TestPercent, LessonPercent);

    /// <summary>
    /// Mavjud mezonlar o'rtachasi. <c>null</c> mezon hisobga OLINMAYDI.
    /// </summary>
    /// <param name="lesson">
    /// Dars bahosi (R24). Standart <c>null</c> — uch mezonli eski
    /// chaqiruvlar AYNAN avvalgi natijani beradi.
    /// </param>
    public static decimal Combine(
        decimal? attendance, decimal? assignment, decimal? test, decimal? lesson = null)
    {
        var sum = 0m;
        var count = 0;

        if (attendance is { } a) { sum += a; count++; }
        if (assignment is { } b) { sum += b; count++; }
        if (test is { } c) { sum += c; count++; }
        if (lesson is { } d) { sum += d; count++; }

        return count == 0 ? 0m : Round(sum / count);
    }

    /// <summary>
    /// Ulushni foizga aylantiradi. Maxraj 0 yoki manfiy bo'lsa 0 —
    /// nolga bo'lish (<c>NaN</c>) hech qachon foydalanuvchiga chiqmasin.
    /// </summary>
    public static decimal Percent(decimal achieved, decimal max) =>
        max <= 0 ? 0m : Round(achieved / max * 100m);

    /// <summary>Nisbat (0..1) dan foizga.</summary>
    public static decimal PercentFromRatio(decimal ratio) => Round(ratio * 100m);

    /// <summary>
    /// YAGONA yaxlitlash qoidasi. <see cref="MidpointRounding.AwayFromZero"/>
    /// ATAYLAB: .NET ning standarti "banker's rounding" (juftga yaxlitlash)
    /// va u 82.25 ni 82.2 ga, 82.35 ni esa 82.4 ga aylantiradi — bir xil
    /// ko'rinadigan ikki holat turlicha yaxlitlanardi va o'quvchiga buni
    /// tushuntirib bo'lmasdi.
    /// </summary>
    private static decimal Round(decimal value) =>
        Math.Round(value, PercentDecimals, MidpointRounding.AwayFromZero);
}

/// <summary>Reytingdagi bitta qator: o'rin + ball tafsiloti.</summary>
public sealed record RankedScore(int Rank, LeaderboardScore Score);

/// <summary>
/// Ballardan O'RIN chiqaradi.
///
/// ★ ESKI TIZIMDAN FARQ — ATAYLAB QILINGAN TUZATISH:
/// eski kod shunchaki <c>rows.sort(...)</c> qilib <c>i + 1</c> yozardi,
/// ya'ni AYNAN BIR XIL balga ega ikki o'quvchi turli o'rin olardi va
/// kim yuqori turishi Python sort'ining barqarorligiga — ya'ni
/// tasodifga — bog'liq edi. Bir xil ball bilan "5-o'rin" va "6-o'rin"
/// olgan ikki o'quvchi orasidagi farqni hech kim tushuntira olmasdi.
///
/// Bu yerda MUSOBAQA (standart) tartibi: teng ball — TENG o'rin, keyingi
/// o'rin esa sakraydi (1, 2, 2, 4). Ko'rsatish tartibi ham deterministik:
/// ball -> ism -> Id. Ya'ni bir xil ma'lumot HAR DOIM bir xil jadval beradi.
/// </summary>
public static class LeaderboardRanking
{
    /// <summary>
    /// GURUH chegarasi — bir guruhda shuncha o'quvchidan oshmaydi.
    ///
    /// ★ BU SON MARKAZ REYTINGIGA TEGISHLI EMAS va ataylab
    /// KO'TARILMADI (2026-08-13). U ikki ishni bajaradi:
    ///
    ///   1) MA'LUMOT SOG'LIG'I: 500 kishilik "guruh" — bu guruh emas,
    ///      bu ma'lumotdagi xato (masalan noto'g'ri import). Jimgina
    ///      500 ta qator chizib berish xatoni yashirardi.
    ///
    ///   2) JAVOB HAJMI: guruh jadvali TO'LIQ yuboriladi (o'quvchi o'z
    ///      guruhidagi hammani ko'radi) — chegarasiz u cheksiz o'sardi.
    ///
    /// Markaz jadvalida ikkala sabab ham ISHLAMAYDI: 3000 o'quvchili
    /// markaz mutlaqo normal holat, va javob baribir TOP-N gacha
    /// qisqartiriladi (<c>LeaderboardService.CenterTopRows</c>).
    /// Shuning uchun markaz yo'li <see cref="RankAll"/> ni chaqiradi.
    /// </summary>
    public const int MaxRows = 500;

    /// <summary>
    /// GURUH jadvali: tartiblaydi, o'rin beradi va
    /// <see cref="MaxRows"/> chegarasini QO'RIQLAYDI.
    /// </summary>
    /// <exception cref="DomainException">Qatorlar soni chegaradan oshdi.</exception>
    public static IReadOnlyList<RankedScore> Rank(IEnumerable<LeaderboardScore> scores)
    {
        var ranked = RankAll(scores);

        if (ranked.Count > MaxRows)
            throw new DomainException($"Reyting jadvali {MaxRows} qatordan oshmasligi kerak.");

        return ranked;
    }

    /// <summary>
    /// CHEGARASIZ tartiblash — markaz (butun o'quv markaz) jadvali uchun.
    ///
    /// ★ O'RINLAR HAMMA UCHUN HISOBLANADI, keyin javob qisqartiriladi.
    /// Teskarisi (avval kesib, keyin o'rin berish) 101-o'rindagi
    /// o'quvchiga "1-o'rin" deb ko'rsatardi — chunki u kesilgan
    /// ro'yxatning birinchisi bo'lib qolardi.
    ///
    /// 🔴 XOTIRA O'QUVCHILAR SONIGA CHIZIQLI: butun markaz ro'yxati
    ///    xotirada tartiblanadi. Bugungi hajmda (yuzlab o'quvchi) bu
    ///    arzon va natija keshlanadi, ya'ni hisob daqiqada bir marta
    ///    bo'ladi. Markaz o'n minglab o'quvchiga yetganda bu yondashuv
    ///    o'rniga snapshot jadvali kerak bo'ladi — o'shanda
    ///    <c>LeaderboardService</c> izohidagi "snapshot qo'shilmadi"
    ///    qarori QAYTA KO'RIB CHIQILSIN.
    /// </summary>
    public static IReadOnlyList<RankedScore> RankAll(IEnumerable<LeaderboardScore> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);

        var ordered = scores
            .OrderByDescending(s => s.Total)
            .ThenBy(s => s.StudentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.StudentId)
            .ToList();

        var result = new List<RankedScore>(ordered.Count);
        var rank = 0;
        decimal? previousTotal = null;

        for (var i = 0; i < ordered.Count; i++)
        {
            var score = ordered[i];

            // Teng ball — oldingi o'rin saqlanadi; aks holda joriy pozitsiya.
            if (previousTotal != score.Total)
                rank = i + 1;

            result.Add(new RankedScore(rank, score));
            previousTotal = score.Total;
        }

        return result;
    }
}
