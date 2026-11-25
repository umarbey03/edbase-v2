using Zinnur.Domain.Enums;

namespace Zinnur.Application.SelfStudy.Nudges;

/// <summary>
/// Bosqichlar chegarasi — faolsizlikning necha KALENDAR kunida qaysi bosqich.
/// </summary>
/// <param name="Day1">Yengil turtki.</param>
/// <param name="Day2">Keyingi dars nomi bilan.</param>
/// <param name="CuratorAlert">Kuratorga xabar.</param>
/// <param name="Ask">Tugmali savol.</param>
/// <param name="Final">Oxirgi xabar.</param>
/// <param name="MaxIdleDays">
/// Shundan uzoq faolsiz bo'lgan va hali hech qanday eslatma olmagan o'quvchiga
/// zanjir BOSHLANMAYDI. Sabab: funksiya yoqilgan kuni bir oydan beri kirmagan
/// odamga birdaniga xabar ketsa, bu «spam» bo'lib tuyuladi va botni bloklashadi.
/// </param>
public sealed record NudgeThresholds(
    int Day1 = 1,
    int Day2 = 2,
    int CuratorAlert = 3,
    int Ask = 5,
    int Final = 14,
    int MaxIdleDays = 21)
{
    public static NudgeThresholds Default { get; } = new();
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// ESLATMALAR ZANJIRI QOIDASI — HOLATSIZ, BAZASIZ (2026-11-25)
/// ════════════════════════════════════════════════════════════════════════
///
/// Bitta savolga javob beradi: «shu o'quvchiga HOZIR qaysi bosqich
/// yuborilsin (yoki hech qaysi)?». Qoida bazadan va Telegram'dan mustaqil,
/// shuning uchun to'liq unit-test qilinadi (<c>NudgeLadderTests</c>).
///
/// QOIDALAR:
///   1. Faqat OLDINGA: shu faolsizlik davrida yuborilgan eng yuqori
///      bosqichdan pastga qaytilmaydi. Masalan, funksiya yoqilgan kuni
///      o'quvchi 4 kundan beri faolsiz bo'lsa — kuratorga xabar ketadi,
///      ertasiga esa «1-kun» xabari EMAS (bu bema'ni bo'lardi).
///   2. Bir nechta bosqich birdan yetilgan bo'lsa — faqat ENG YUQORISI.
///   3. O'quvchiga yozish taqiqlangan paytda (o'chirgan, «keyinroq» degan,
///      bugun allaqachon olgan) faqat KURATOR bosqichi qoladi: u o'quvchiga
///      emas, xodimga boradi.
/// </summary>
public static class NudgeLadder
{
    /// <summary>Zanjir tartibi (<see cref="NudgeStage.SnoozeReturn"/> zanjirdan tashqarida).</summary>
    public static IReadOnlyList<NudgeStage> Ladder { get; } =
    [
        NudgeStage.Day1,
        NudgeStage.Day2,
        NudgeStage.CuratorAlert,
        NudgeStage.Ask,
        NudgeStage.Final,
    ];

    /// <summary>Bosqich o'quvchining o'ziga boradimi (kuratorga emas).</summary>
    public static bool IsStudentFacing(NudgeStage stage) => stage != NudgeStage.CuratorAlert;

    public static int Threshold(NudgeStage stage, NudgeThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        return stage switch
        {
            NudgeStage.Day1 => thresholds.Day1,
            NudgeStage.Day2 => thresholds.Day2,
            NudgeStage.CuratorAlert => thresholds.CuratorAlert,
            NudgeStage.Ask => thresholds.Ask,
            NudgeStage.Final => thresholds.Final,
            _ => int.MaxValue,
        };
    }

    /// <summary>
    /// Necha KALENDAR kun faolsiz (markaz vaqt zonasida). Kecha dars qilgan
    /// o'quvchi bugun kechqurun — 1, bugun qilgan — 0.
    ///
    /// ★ NIMA UCHUN SOAT EMAS, SANA: eslatma kechqurun ketadi. Soat bilan
    /// o'lchansa, kecha 21:00 da dars qilgan o'quvchi bugun 19:00 da «22 soat»
    /// bo'lib chiqar va bosqich bir kunga surilib ketardi.
    /// </summary>
    public static int IdleDays(DateTimeOffset anchor, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var from = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(anchor, zone).DateTime);
        var to = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

        return Math.Max(0, to.DayNumber - from.DayNumber);
    }

    /// <summary>
    /// Hozir yuboriladigan bosqich yoki <c>null</c>.
    /// </summary>
    /// <param name="idleDays">Faolsizlik kunlari (<see cref="IdleDays"/>).</param>
    /// <param name="sentForAnchor">SHU faolsizlik davrida allaqachon yuborilgan bosqichlar.</param>
    /// <param name="studentMessagesAllowed">O'quvchiga hozir yozish mumkinmi.</param>
    /// <param name="thresholds">Chegaralar.</param>
    public static NudgeStage? Next(
        int idleDays,
        IReadOnlyCollection<NudgeStage> sentForAnchor,
        bool studentMessagesAllowed,
        NudgeThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(sentForAnchor);
        ArgumentNullException.ThrowIfNull(thresholds);

        var lastSent = -1;
        for (var i = 0; i < Ladder.Count; i++)
        {
            if (sentForAnchor.Contains(Ladder[i])) lastSent = i;
        }

        // Uzoq vaqtdan beri yo'q va hali hech narsa olmagan — boshlamaymiz.
        if (lastSent < 0 && idleDays > thresholds.MaxIdleDays) return null;

        NudgeStage? pick = null;

        for (var i = lastSent + 1; i < Ladder.Count; i++)
        {
            var stage = Ladder[i];
            if (Threshold(stage, thresholds) > idleDays) break;
            if (!studentMessagesAllowed && IsStudentFacing(stage)) continue;
            pick = stage;
        }

        return pick;
    }

    /// <summary>
    /// Matn varianti: o'quvchi shu bosqichni nechanchi marta olayotganiga qarab
    /// aylanadi — bir xil matn ikki marta ketma-ket kelmasin.
    /// </summary>
    public static int PickVariant(int previousCount, int variantCount) =>
        variantCount <= 1 ? 0 : Math.Abs(previousCount) % variantCount;
}
