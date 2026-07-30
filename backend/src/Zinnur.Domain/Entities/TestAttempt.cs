using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// O'quvchining bitta testdagi urinishi.
/// <c>(TestId, StudentId)</c> — UNIKAL: bir test bir marta topshiriladi.
/// </summary>
public class TestAttempt : BaseEntity
{
    public long TestId { get; set; }

    public Test? Test { get; set; }

    public long StudentId { get; set; }

    public User? Student { get; set; }

    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;

    public decimal? Score { get; set; }

    public decimal? MaxScore { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>
    /// Vaqt tugab ketgani uchun majburan yopilganmi.
    /// Hisobotda "tashlab ketilgan" va "vaqti tugagan" ni ajratish uchun.
    /// </summary>
    public bool ClosedByTimeout { get; set; }

    public ICollection<TestAnswer> Answers { get; set; } = new List<TestAnswer>();

    // ---------------------------------------------------------------- hisoblanuvchi

    public bool IsSubmitted => Status == AttemptStatus.Submitted;

    /// <summary>Foizli natija (reyting uchun).</summary>
    public decimal? Percent =>
        Score is { } s && MaxScore is { } m && m > 0
            ? Math.Round(s / m * 100m, 1)
            : null;

    /// <summary>
    /// Urinish qachon tugaydi — SERVER hisobi bo'yicha. Chegara yo'q bo'lsa <c>null</c>.
    ///
    /// IKKI mustaqil chegara bor va ERTAROG'I kuchda bo'ladi:
    ///   • vaqt chegarasi  — <c>StartedAt + TimeLimitMinutes</c> (har o'quvchida boshqa);
    ///   • topshirish muddati — <c>Test.DueAt</c> (butun guruhga bir xil).
    ///
    /// ★ NEGA IKKALASI: ilgari faqat vaqt chegarasi hisoblanardi va bu
    ///   o'quvchining ishini YO'QOTARDI. Misol: test 30 daqiqalik, muddati
    ///   18:00. O'quvchi 17:59 da boshlaydi -> klient taymeri 18:29 ni
    ///   ko'rsatadi, lekin <c>Test.EnsureOpenForSubmission</c> 18:01 dagi
    ///   topshirishni RAD ETADI. Ya'ni taymer server BERMAYDIGAN vaqtni
    ///   va'da qilardi. Endi taymer 18:00 ni ko'rsatadi.
    ///
    /// Tolerantlik (<see cref="Test.SubmitGracePeriod"/>) eng oxirida
    /// BIR MARTA qo'shiladi — `EnsureOpenForSubmission` ham aynan shunday
    /// qiladi, ya'ni ikkala tekshiruv bir xil onda ishga tushadi va
    /// "taymer tugadi, lekin server hali qabul qiladi" farqi qolmaydi.
    ///
    /// Testni PARAMETR sifatida olamiz (navigatsiya <see cref="Test"/> emas):
    /// urinish ko'pincha `AsNoTracking` bilan alohida o'qiladi va navigatsiya
    /// null bo'ladi — o'sha holda muddat JIMGINA yo'qolib ketardi.
    /// </summary>
    public DateTimeOffset? Deadline(Test test)
    {
        ArgumentNullException.ThrowIfNull(test);

        DateTimeOffset? byTimeLimit = test.TimeLimitMinutes is { } limit
            ? StartedAt.AddMinutes(limit)
            : null;

        DateTimeOffset? earliest = (byTimeLimit, test.DueAt) switch
        {
            ({ } byLimit, { } byDue) => byLimit <= byDue ? byLimit : byDue,
            ({ } byLimit, null) => byLimit,
            (null, { } byDue) => byDue,
            _ => null,
        };

        return earliest?.Add(Test.SubmitGracePeriod);
    }

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// Javoblarni qabul qilib, ballni hisoblaydi va urinishni yopadi.
    ///
    /// Baholash SERVERDA — klient hisoblagan ballga hech qachon ishonilmaydi.
    /// </summary>
    /// <param name="questions">Testning barcha savollari (variantlari bilan).</param>
    /// <param name="selections">savol -> tanlangan variantlar.</param>
    public void SubmitAnswers(
        IReadOnlyCollection<TestQuestion> questions,
        IReadOnlyDictionary<long, IReadOnlyCollection<long>> selections,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(selections);

        if (IsSubmitted)
            throw new DomainException("Bu test allaqachon topshirilgan.");

        var total = 0m;

        foreach (var question in questions)
        {
            var selected = selections.TryGetValue(question.Id, out var picked)
                ? picked
                : Array.Empty<long>();

            // BEGONA VARIANT FILTRI: klient boshqa savolning yoki boshqa
            // testning variant ID'sini yuborishi mumkin. Faqat shu savolga
            // tegishli variantlarni qabul qilamiz.
            var valid = selected
                .Where(id => question.Options.Any(o => o.Id == id))
                .ToList();

            total += question.Score(valid);

            foreach (var optionId in valid)
                Answers.Add(new TestAnswer
                {
                    AttemptId = Id,
                    QuestionId = question.Id,
                    OptionId = optionId,
                });
        }

        Score = total;
        MaxScore = questions.Sum(q => q.Points);
        Status = AttemptStatus.Submitted;
        SubmittedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Vaqt tugagani uchun urinishni 0 ball bilan yopadi.
    /// Javob yozilmaydi — o'quvchi ularni yubormagan.
    /// </summary>
    public void CloseByTimeout(decimal maxScore, DateTimeOffset now)
    {
        if (IsSubmitted) return;   // idempotent

        Score = 0;
        MaxScore = maxScore;
        Status = AttemptStatus.Submitted;
        SubmittedAt = now;
        ClosedByTimeout = true;
        UpdatedAt = now;
    }
}

/// <summary>
/// Urinishdagi bitta tanlov.
/// <c>(AttemptId, QuestionId, OptionId)</c> — UNIKAL: bir variant bir marta.
///
/// DIQQAT: <c>(AttemptId, QuestionId)</c> unikal EMAS — bir savolga bir
/// nechta to'g'ri javob bo'lishi mumkin va o'quvchi bir nechtasini tanlaydi.
/// Eski tizimda bu juftlik unikal edi va shu sababli ko'p tanlovli savol
/// umuman ishlamasdi.
/// </summary>
public class TestAnswer : BaseEntity
{
    public long AttemptId { get; set; }

    public TestAttempt? Attempt { get; set; }

    public long QuestionId { get; set; }

    public TestQuestion? Question { get; set; }

    public long OptionId { get; set; }

    public TestOption? Option { get; set; }
}
