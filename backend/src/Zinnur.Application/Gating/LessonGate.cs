using Zinnur.Application.Gating.Dtos;

namespace Zinnur.Application.Gating;

/// <summary>
/// Bitta darsning gating uchun kerakli FAKTLARI — bazadan o'qilgan xom holat.
/// Qoida (<see cref="LessonGate"/>) faqat shu turga tayanadi, ya'ni bazasiz
/// test qilinadi.
/// </summary>
/// <param name="HasVideo">Darsda video kontenti bormi.</param>
/// <param name="HasAssignment">Darsga KURS vazifasi biriktirilganmi.</param>
/// <param name="HasTest">Darsga E'LON QILINGAN test biriktirilganmi.</param>
/// <param name="UnlockedOverride">O'quv bo'limi qo'lda ochib berganmi (istisno).</param>
public sealed record LessonFacts(
    long LessonId,
    bool HasVideo,
    bool VideoWatched,
    bool HasAssignment,
    bool AssignmentSubmitted,
    bool HasTest,
    bool TestTaken,
    bool UnlockedOverride);

/// <summary>
/// ========================================================================
/// SUR'AT NAZORATI (GATING) QOIDASI — SOF FUNKSIYA
/// ========================================================================
///
/// Dars N OCHIQ ⟺ (N−1) dars TUGATILGAN **VA** N ustoz sur'atidan oshmagan.
///
/// TUGATILGAN = video ko'rilgan (agar video bor) **VA** vazifa topshirilgan
/// (agar kurs vazifasi bor) **VA** test yechilgan (agar dars testi bor).
/// Ya'ni mavjud bo'lmagan shart TALAB QILINMAYDI.
///
/// ★ GURUH BOSHLANISH NUQTASI (<c>startIndex</c>): guruh kursning
/// O'RTASIDAN boshlagan bo'lsa (<c>Group.VideoStartLessonId</c>) zanjir
/// 0-darsdan emas, SHU NUQTADAN yuritiladi. Undan oldingi darslar
/// <see cref="LessonLockReason.BeforeGroupStart"/> bilan yopiq bo'ladi va
/// zanjirga UMUMAN kirmaydi — ular hech qachon o'tilmaydi, shuning uchun
/// "tugatilmagan" bo'lib butun kursni qulflab qo'yishga haqqi yo'q.
/// Ustoz sur'ati ham NISBIY o'lchanadi (<c>index − startIndex</c>): guruh
/// 3 ta dars o'tgan bo'lsa, u 20-, 21-, 22-darslarni o'tgan.
/// <c>startIndex = 0</c> — bugungi xatti-harakat, bit-to-bit o'zgarmaydi.
///
/// Istisnolar (tartib bo'yicha):
///   1) <c>UnlockedOverride</c> — o'quv bo'limi qo'lda ochgan: DOIM ochiq.
///      (Kasallik, kursga kech qo'shilish.) Bu boshlanish nuqtasidan
///      OLDINGI darsga ham tegishli: o'tib ketilgan qismni o'zlashtirmoqchi
///      bo'lgan o'quvchiga o'quv bo'limi uni ocha oladi.
///   2) GURUH uchun BIRINCHI dars (<c>index == startIndex</c>) — DOIM
///      ochiq, aks holda kurs boshlanmasdi.
///
/// NIMA UCHUN ALOHIDA, HOLATSIZ SINF: qoida bazadan, keshdan va HTTP'dan
/// mustaqil. Shu tufayli u bitta joyda yozilgan (DRY) va bazasiz test
/// qilinadi — <c>tests/Zinnur.UnitTests/Gating/LessonGateTests.cs</c>.
/// "Bitta dars" va "butun daraxt" yo'llari AYNI shu funksiyani chaqiradi,
/// shuning uchun ular hech qachon boshqa-boshqa javob bermaydi.
/// </summary>
public static class LessonGate
{
    /// <summary>Dars tugatilganmi (mavjud bo'lmagan shart talab qilinmaydi).</summary>
    public static bool IsComplete(LessonFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var video = !facts.HasVideo || facts.VideoWatched;
        var assignment = !facts.HasAssignment || facts.AssignmentSubmitted;
        var test = !facts.HasTest || facts.TestTaken;

        return video && assignment && test;
    }

    /// <summary>
    /// Bitta darsni baholaydi — BUTUN DARAXTNI QURMASDAN.
    ///
    /// Chaqiruvchi faqat ikki fakt to'plamini topib beradi: shu darsning va
    /// undan OLDINGI darsning. Zanjir emas: qoida faqat BEVOSITA oldingi
    /// darsga qaraydi, shuning uchun N-darsni tekshirish uchun 1..N−1
    /// darslarni hisoblash shart emas.
    /// </summary>
    /// <param name="index">Darsning kurs ichidagi global tartib raqami (0 dan).</param>
    /// <param name="facts">Shu darsning faktlari.</param>
    /// <param name="previous">Oldingi dars faktlari; birinchi darsda <c>null</c>.</param>
    /// <param name="taughtLessonCount">Ustoz sur'ati (yakunlangan ustoz darslari soni).</param>
    /// <param name="startIndex">
    /// ★ Guruh kursni QAYSI global indeksdan boshlaydi
    /// (<c>Group.VideoStartLessonId</c> ning tartib raqami). Standart 0 —
    /// kurs boshidan, ya'ni bugungi xatti-harakat AYNAN saqlanadi.
    /// </param>
    public static (bool Unlocked, LessonLockReason? Reason) Evaluate(
        int index,
        LessonFacts facts,
        LessonFacts? previous,
        int taughtLessonCount,
        int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(facts);

        // 1) Qo'lda ochilgan — boshqa hech qanday shart tekshirilmaydi.
        //    Boshlanish nuqtasidan OLDIN ham ustun turadi: o'quv bo'limi
        //    o'tib ketilgan qismni ataylab ocha oladi.
        if (facts.UnlockedOverride)
            return (true, null);

        // 2) ★ GURUH BOSHLANISH NUQTASIDAN OLDINGI DARS.
        //
        //    Guruh kursning o'rtasidan boshlagan: bu dars uning o'quv
        //    rejasiga umuman kirmaydi. Sabab ALOHIDA, chunki o'quvchiga
        //    "oldingi darsni tugat" deyish ma'nosiz bo'lardi — u darsni
        //    hech qachon o'tmaydi.
        if (index < startIndex)
            return (false, LessonLockReason.BeforeGroupStart);

        // 3) GURUH uchun BIRINCHI dars doim ochiq: aks holda o'quvchi kursni
        //    umuman boshlay olmasdi (ustoz hali hech qanday dars o'tmagan
        //    bo'lishi mumkin). `startIndex = 0` da bu AYNAN eski shart
        //    (`index <= 0`).
        if (index <= startIndex || previous is null)
            return (true, null);

        // 4) USTOZ SUR'ATI — NISBIY. `taughtLessonCount` guruhda YAKUNLANGAN
        //    ustoz darslari soni: guruh N ta dars o'tgan bo'lsa u
        //    `startIndex .. startIndex + N − 1` darslarni o'tgan, ya'ni
        //    KEYINGI (`startIndex + N`) dars ham ochiladi.
        //
        //    ★ NIMA UCHUN AYNAN NISBIY: mutlaq taqqoslash (`index >
        //    taughtLessonCount`) 20-darsdan boshlagan guruhda BUTUN kursni
        //    abadiy `TeacherPace` bilan yopib qo'yardi — sur'at hech qachon
        //    20 ga yetmasdi (guruh 8 oyda ~70 dars o'tadi, lekin hisob
        //    guruh ochilgan kundan, ya'ni noldan boshlanadi).
        if (index - startIndex > taughtLessonCount)
            return (false, LessonLockReason.TeacherPace);

        // 5) Oldingi dars tugatilgan bo'lishi shart. `previous` bu yerda
        //    DOIM `startIndex` yoki undan keyingi dars (3-qadam oldin
        //    qaytgani uchun), ya'ni zanjir boshlanish nuqtasidan orqaga
        //    hech qachon o'tmaydi.
        return IsComplete(previous)
            ? (true, null)
            : (false, LessonLockReason.PreviousIncomplete);
    }

    /// <summary>
    /// Butun kursni BITTA o'tishda baholaydi (O(n), hech qanday ichma-ich sikl yo'q).
    /// Darslar KURS TARTIBIDA (modul tartibi, keyin dars tartibi) berilishi shart.
    /// </summary>
    /// <param name="startIndex">
    /// ★ Guruh boshlanish nuqtasi (batafsil: <see cref="Evaluate"/>).
    /// Standart 0 — kurs boshidan.
    /// </param>
    public static IReadOnlyList<LessonGateDto> EvaluateAll(
        IReadOnlyList<LessonFacts> orderedLessons,
        int taughtLessonCount,
        int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(orderedLessons);

        var result = new List<LessonGateDto>(orderedLessons.Count);
        LessonFacts? previous = null;

        for (var index = 0; index < orderedLessons.Count; index++)
        {
            var facts = orderedLessons[index];
            var (unlocked, reason) = Evaluate(index, facts, previous, taughtLessonCount, startIndex);

            result.Add(Describe(index, facts, unlocked, reason));

            // `previous` SHARTSIZ yangilanadi — boshlanish nuqtasidan
            // oldingi darslar ham shu yerda o'tadi, lekin ular hech qachon
            // zanjirga TA'SIR QILMAYDI: `Evaluate` `index <= startIndex`
            // holatida `previous` ni umuman o'qimaydi.
            previous = facts;
        }

        return result;
    }

    /// <summary>Faktlarni + qarorni tashqi shaklga (DTO) o'giradi.</summary>
    public static LessonGateDto Describe(
        int index, LessonFacts facts, bool unlocked, LessonLockReason? reason)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return new LessonGateDto(
            facts.LessonId,
            index,
            unlocked,
            reason,
            IsComplete(facts),
            facts.HasVideo,
            facts.VideoWatched,
            facts.HasAssignment,
            facts.AssignmentSubmitted,
            facts.HasTest,
            facts.TestTaken,
            facts.UnlockedOverride);
    }
}
