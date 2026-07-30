using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Onlayn test. Ikki tur:
///  - <see cref="TestKind.Lesson"/> — kurs darsiga bog'langan, sur'at nazoratiga kiradi
///  - <see cref="TestKind.Competition"/> — musobaqa, kursdan mustaqil
/// </summary>
public class Test : BaseEntity
{
    public const int MaxTitleLength = 200;

    /// <summary>Vaqt chegarasi tugagach beriladigan tolerantlik (tarmoq kechikishi uchun).</summary>
    public static readonly TimeSpan SubmitGracePeriod = TimeSpan.FromSeconds(60);

    public required string Title { get; set; }

    public string? Description { get; set; }

    public TestKind Kind { get; set; } = TestKind.Competition;

    /// <summary>Dars testi bo'lsa — kurs darsi.</summary>
    public long? ModuleLessonId { get; set; }

    public ModuleLesson? ModuleLesson { get; set; }

    /// <summary>Vaqt chegarasi (daqiqa). <c>null</c> — chegarasiz.</summary>
    public int? TimeLimitMinutes { get; set; }

    /// <summary>
    /// Topshirish muddati. <c>null</c> — muddatsiz.
    ///
    /// SERVERDA MAJBURIY tekshiriladi (<see cref="EnsureOpenForSubmission"/>).
    /// Eski tizimda bu ustun bor edi, lekin `take_test` ham, `submit_test` ham
    /// uni TEKSHIRMASDI — o'quvchi muddat tugagandan keyin ham topshira olardi.
    /// </summary>
    public DateTimeOffset? DueAt { get; set; }

    public bool IsPublished { get; set; }

    public long? CreatedById { get; set; }

    public ICollection<TestQuestion> Questions { get; set; } = new List<TestQuestion>();

    // ---------------------------------------------------------------- hisoblanuvchi

    public bool IsLessonTest => Kind == TestKind.Lesson && ModuleLessonId is not null;

    /// <summary>
    /// Testning umumiy maksimal bali (savollar balining yig'indisi).
    ///
    /// ★ SAVOLSIZ TESTDA 0 EMAS, XATO QAYTADI.
    ///
    /// NEGA: <c>Questions</c> — navigatsiya to'plami. Test uni `Include`
    /// qilmasdan o'qilgan bo'lsa to'plam BO'SH keladi va eski hisob JIMGINA
    /// `0` qaytarardi. Ya'ni "ma'lumot yuklanmagan" va "testda savol yo'q"
    /// bir xil qiymat berardi — Domain esa EF haqida hech narsa bilmagani
    /// uchun ularni ajrata olmaydi.
    ///
    /// 0 ning narxi jimgina va katta: chaqiruvchi foizni `Score / MaxScore`
    /// deb hisoblasa nolga bo'lish yoki `NaN` chiqadi, "o'tish bali"
    /// tekshiruvi esa har doim "o'tdi" beradi. Bug BAHOda ko'rinadi —
    /// sababidan uzoqda.
    ///
    /// Baland ovozda yiqilish bu yerda XAVFSIZ, chunki bo'sh test amalda
    /// yechilmaydi: <see cref="Publish"/> savolsiz testni e'lon qildirmaydi.
    /// Demak bo'sh to'plam deyarli har doim "yuklashni unutdim" degani.
    ///
    /// METOD EMAS, XOSSA bo'lib qoladi: EF konfiguratsiyasi uni
    /// `builder.Ignore(t => t.MaxScore)` bilan ustunlardan chiqaradi va bu
    /// ifoda XOSSA talab qiladi (metodga aylantirilsa o'sha satr
    /// kompilyatsiya bo'lmaydi).
    ///
    /// DIQQAT — bu xossa EF SO'ROVI ichida ishlatilmaydi: Application qatlami
    /// yig'indini BAZADA (`t.Questions.Sum(q => (decimal?)q.Points) ?? 0m`)
    /// hisoblaydi. O'sha yerda savolsiz test HAQIQATAN 0 bo'lishi kerak —
    /// ro'yxatda hali to'ldirilmagan qoralama ham ko'rinadi.
    /// </summary>
    /// <exception cref="DomainException">Savollar yuklanmagan yoki test bo'sh.</exception>
    public decimal MaxScore => Questions.Count > 0
        ? Questions.Sum(q => q.Points)
        : throw new DomainException(
            "Testning maksimal bali savolsiz hisoblanmaydi — "
            + "savollar yuklanmagan bo'lishi mumkin (Include unutilgan).");

    // ---------------------------------------------------------------- xatti-harakat

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
            throw new DomainException("Test sarlavhasi kiritilishi shart.");

        if (Title.Length > MaxTitleLength)
            throw new DomainException($"Sarlavha {MaxTitleLength} belgidan oshmasin.");

        if (TimeLimitMinutes is { } limit && limit <= 0)
            throw new DomainException("Vaqt chegarasi noldan katta bo'lishi kerak.");

        if (Kind == TestKind.Lesson && ModuleLessonId is null)
            throw new DomainException("Dars testi uchun kurs darsi ko'rsatilishi shart.");

        if (Kind == TestKind.Competition && ModuleLessonId is not null)
            throw new DomainException("Musobaqa testi kurs darsiga bog'lanmaydi.");
    }

    /// <summary>E'lon qilish oldidan tekshiruv — bo'sh test e'lon qilinmasin.</summary>
    public void Publish()
    {
        if (Questions.Count == 0)
            throw new DomainException("Avval savol qo'shing — bo'sh test e'lon qilinmaydi.");

        foreach (var question in Questions)
            question.Validate();

        IsPublished = true;
    }

    public void Unpublish() => IsPublished = false;

    /// <summary>
    /// Test hozir topshirish uchun ochiqmi. Yopiq bo'lsa
    /// <see cref="DomainException"/> — sababi bilan.
    /// </summary>
    public void EnsureOpenForSubmission(DateTimeOffset now)
    {
        if (!IsPublished)
            throw new DomainException("Test e'lon qilinmagan.");

        // Muddat + tolerantlik: klient taymeri bilan server o'rtasidagi
        // bir necha soniyalik farq o'quvchini jazolamasin.
        if (DueAt is { } due && now > due + SubmitGracePeriod)
            throw new DomainException("Test topshirish muddati tugagan.");
    }
}

/// <summary>Test savoli.</summary>
public class TestQuestion : BaseEntity
{
    public const int MaxBodyLength = 2000;
    public const int MinOptions = 2;

    public long TestId { get; set; }

    public Test? Test { get; set; }

    public required string Body { get; set; }

    /// <summary>Savol rasmi (obyekt kaliti).</summary>
    public string? ImageKey { get; set; }

    public int Position { get; set; }

    /// <summary>Shu savol uchun ball.</summary>
    public decimal Points { get; set; } = 1;

    public ICollection<TestOption> Options { get; set; } = new List<TestOption>();

    // ---------------------------------------------------------------- hisoblanuvchi

    /// <summary>
    /// To'g'ri variantlar. BIR NECHTA bo'lishi mumkin.
    ///
    /// Eski tizim buni `dict[question_id] = option_id` sifatida saqlardi va
    /// faqat OXIRGI to'g'ri variant hisobga olinardi — o'quvchi to'g'ri javob
    /// berib ham ball olmasligi mumkin edi.
    /// </summary>
    public IReadOnlyCollection<long> CorrectOptionIds =>
        Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList();

    /// <summary>Bir nechta to'g'ri javobli savolmi (interfeys checkbox ko'rsatadi).</summary>
    public bool IsMultipleChoice => Options.Count(o => o.IsCorrect) > 1;

    // ---------------------------------------------------------------- xatti-harakat

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Body))
            throw new DomainException("Savol matni kiritilishi shart.");

        if (Body.Length > MaxBodyLength)
            throw new DomainException($"Savol matni {MaxBodyLength} belgidan oshmasin.");

        if (Points <= 0)
            throw new DomainException("Savol bali noldan katta bo'lishi kerak.");

        if (Options.Count < MinOptions)
            throw new DomainException($"Kamida {MinOptions} ta variant kerak.");

        if (!Options.Any(o => o.IsCorrect))
            throw new DomainException("Kamida bitta to'g'ri variant belgilanishi kerak.");
    }

    /// <summary>
    /// Berilgan javob(lar) uchun ball hisoblaydi.
    ///
    /// QOIDA: "hammasi yoki hech nima" — tanlangan to'plam to'g'ri to'plam
    /// bilan AYNAN mos kelishi kerak. Qisman ball berilmaydi, chunki
    /// bir nechta to'g'ri javobda qisman ball tasodifiy tanlashni
    /// rag'batlantiradi.
    /// </summary>
    public decimal Score(IReadOnlyCollection<long> selectedOptionIds)
    {
        ArgumentNullException.ThrowIfNull(selectedOptionIds);

        var correct = CorrectOptionIds.ToHashSet();
        if (correct.Count == 0) return 0;

        return selectedOptionIds.ToHashSet().SetEquals(correct) ? Points : 0m;
    }
}

/// <summary>Savol varianti.</summary>
public class TestOption : BaseEntity
{
    public const int MaxBodyLength = 1000;

    public long QuestionId { get; set; }

    public TestQuestion? Question { get; set; }

    public required string Body { get; set; }

    public bool IsCorrect { get; set; }

    public int Position { get; set; }
}
