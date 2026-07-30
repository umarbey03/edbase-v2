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
/// Istisnolar (tartib bo'yicha):
///   1) <c>UnlockedOverride</c> — o'quv bo'limi qo'lda ochgan: DOIM ochiq.
///      (Kasallik, kursga kech qo'shilish.)
///   2) BIRINCHI dars (indeks 0) — DOIM ochiq, aks holda kurs boshlanmasdi.
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
    public static (bool Unlocked, LessonLockReason? Reason) Evaluate(
        int index,
        LessonFacts facts,
        LessonFacts? previous,
        int taughtLessonCount)
    {
        ArgumentNullException.ThrowIfNull(facts);

        // 1) Qo'lda ochilgan — boshqa hech qanday shart tekshirilmaydi.
        if (facts.UnlockedOverride)
            return (true, null);

        // 2) Birinchi dars doim ochiq: aks holda o'quvchi kursni umuman
        //    boshlay olmasdi (ustoz hali hech qanday dars o'tmagan bo'lishi mumkin).
        if (index <= 0 || previous is null)
            return (true, null);

        // 3) USTOZ SUR'ATI. `taughtLessonCount` — yakunlangan ustoz darslari
        //    soni. Ustoz N ta dars o'tgan bo'lsa u 0..N−1 indeksli darslarni
        //    o'tgan, ya'ni o'quvchiga KEYINGI (N-indeksli) dars ham ochiladi.
        //    Shuning uchun shart `index <= taughtLessonCount`.
        if (index > taughtLessonCount)
            return (false, LessonLockReason.TeacherPace);

        // 4) Oldingi dars tugatilgan bo'lishi shart.
        return IsComplete(previous)
            ? (true, null)
            : (false, LessonLockReason.PreviousIncomplete);
    }

    /// <summary>
    /// Butun kursni BITTA o'tishda baholaydi (O(n), hech qanday ichma-ich sikl yo'q).
    /// Darslar KURS TARTIBIDA (modul tartibi, keyin dars tartibi) berilishi shart.
    /// </summary>
    public static IReadOnlyList<LessonGateDto> EvaluateAll(
        IReadOnlyList<LessonFacts> orderedLessons,
        int taughtLessonCount)
    {
        ArgumentNullException.ThrowIfNull(orderedLessons);

        var result = new List<LessonGateDto>(orderedLessons.Count);
        LessonFacts? previous = null;

        for (var index = 0; index < orderedLessons.Count; index++)
        {
            var facts = orderedLessons[index];
            var (unlocked, reason) = Evaluate(index, facts, previous, taughtLessonCount);

            result.Add(Describe(index, facts, unlocked, reason));
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
