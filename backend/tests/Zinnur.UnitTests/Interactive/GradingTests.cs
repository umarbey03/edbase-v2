using System.Text.Json.Nodes;
using Zinnur.Application.Interactive.Content;
using Zinnur.Application.Interactive.Grading;
using static Zinnur.Application.Interactive.Content.UnitJson;

namespace Zinnur.UnitTests.Interactive;

public sealed class ArabicNormTests
{
    [Theory]
    [InlineData("أَقْرَأُ", "اقرا")]
    [InlineData("  إِلَى   اللِّقَاءِ! ", "الى اللقاء")]
    [InlineData("مَا اسْمُكَ؟", "ما اسمك")]
    [InlineData("كِتَابٌ، قَلَمٌ.", "كتاب قلم")]
    [InlineData("آبْ", "اب")]
    [InlineData("ٱلْـكِتَابُ", "الكتاب")]
    [InlineData("", "")]
    public void Normalizes_like_the_book_renderer(string input, string expected) =>
        ArabicNorm.Normalize(input).Should().Be(expected);

    [Fact]
    public void Answer_without_harakat_is_accepted() =>
        ArabicNorm.Same("رُ", "ر").Should().BeTrue();
}

public sealed class AnswerCheckerTests
{
    private static readonly JsonObject Unit1 = InteractiveTestKit.Load("atf1_birlik_01");

    private static CheckVerdict Check(JsonObject unit, string key, CheckAnswer answer) =>
        AnswerChecker.Check(AnswerChecker.Find(unit, key)!, answer, InteractiveTestKit.Match, InteractiveTestKit.VersionId);

    [Fact]
    public void Choice_question_is_graded_and_reveals_only_that_answer()
    {
        // A2m1, 1-savol: «بُ qanday o'qiladi?» — javob 2 (bu).
        var right = Check(Unit1, "A2m1", new CheckAnswer(Index: 0, Choice: 2));
        var wrong = Check(Unit1, "A2m1", new CheckAnswer(Index: 0, Choice: 0));

        right.Correct.Should().BeTrue();
        wrong.Correct.Should().BeFalse();
        wrong.Reveal["correctChoice"]!.GetValue<int>().Should().Be(2);
        wrong.Reveal.Count.Should().Be(1);
        wrong.Slot.Should().Be("0");
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(9, 0)]
    [InlineData(0, 7)]
    [InlineData(0, -1)]
    public void Malformed_answer_is_rejected_not_graded(int? index, int choice)
    {
        var act = () => Check(Unit1, "A2m1", new CheckAnswer(Index: index, Choice: choice));
        act.Should().Throw<InvalidAnswerException>();
    }

    [Fact]
    public void Sound_test_uses_block_key()
    {
        // A4 tovush testi, 1-savol: javob 1.
        Check(Unit1, "A4:test", new CheckAnswer(Index: 0, Choice: 1)).Correct.Should().BeTrue();
        Check(Unit1, "A4:test", new CheckAnswer(Index: 0, Choice: 0)).Correct.Should().BeFalse();
    }

    [Fact]
    public void Match_pair_is_checked_by_opaque_tokens()
    {
        var match = InteractiveTestKit.Match;
        string L(int i) => match.Token(InteractiveTestKit.VersionId, "A1m1", 'L', i);
        string R(int i) => match.Token(InteractiveTestKit.VersionId, "A1m1", 'R', i);

        Check(Unit1, "A1m1", new CheckAnswer(Left: L(2), Right: R(2))).Correct.Should().BeTrue();
        Check(Unit1, "A1m1", new CheckAnswer(Left: L(2), Right: R(3))).Correct.Should().BeFalse();

        var forged = () => Check(Unit1, "A1m1", new CheckAnswer(Left: "L2", Right: "R2"));
        forged.Should().Throw<InvalidAnswerException>();
    }

    [Fact]
    public void Hunt_word_is_found_by_its_index_in_the_text()
    {
        var unit = InteractiveTestKit.Load("atf1_imtihon");
        var (block, ex) = Exercises(unit).First(e => Str(e.Exercise, "tur") == "ovlash");
        var words = ReadingWords(block);
        var key = Str(ex, "id")!;

        var target = AnswerChecker.HuntTargets(ex);
        var hit = words.FindIndex(w => target.Contains(ArabicNorm.Normalize(CleanWord(w))));
        var miss = words.FindIndex(w => !target.Contains(ArabicNorm.Normalize(CleanWord(w))));

        Check(unit, key, new CheckAnswer(Word: hit)).Correct.Should().BeTrue();
        Check(unit, key, new CheckAnswer(Word: miss)).Correct.Should().BeFalse();
    }

    [Fact]
    public void Table_is_graded_cell_by_cell_without_harakat()
    {
        var unit = InteractiveTestKit.Load("atf2_birlik_05");
        var ex = Exercises(unit).Select(e => e.Exercise).First(e => Str(e, "tur") == "jadval");
        var rows = Arr(ex, "qatorlar").OfType<JsonArray>().ToList();
        var open = AnswerChecker.OpenCells(ex);

        var cells = open.ToDictionary(c => $"{c.Row}:{c.Col}", c => AsString(rows[c.Row][c.Col])!);
        var first = open[0];
        cells[$"{first.Row}:{first.Col}"] = "noto'g'ri";

        var verdict = Check(unit, Str(ex, "id")!, new CheckAnswer(Cells: cells));

        verdict.Right.Should().Be(open.Count - 1);
        verdict.Reveal["cells"]![$"{first.Row}:{first.Col}"]!["correct"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void True_false_reveals_the_correction()
    {
        var unit = InteractiveTestKit.Load("atf1_birlik_28");
        var verdict = Check(unit, "A6m2", new CheckAnswer(Index: 1, Value: true));

        verdict.Correct.Should().BeFalse();
        verdict.Reveal["correctValue"]!.GetValue<bool>().Should().BeFalse();
        verdict.Reveal["tuzatish"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Dictation_accepts_text_without_harakat()
    {
        var w = Objects(Unit1, "mashq_daftari").First(x => Str(x, "tur") == "diktant");
        var word = Strings(w, "sozlar")[0];

        Check(Unit1, Str(w, "id")!, new CheckAnswer(Index: 0, Text: ArabicNorm.Normalize(word))).Correct.Should().BeTrue();
        Check(Unit1, Str(w, "id")!, new CheckAnswer(Index: 0, Text: "ب")).Correct.Should().BeFalse();
    }

    [Fact]
    public void Open_and_voice_workbook_tasks_are_not_auto_graded()
    {
        AnswerChecker.Find(Unit1, "W1").Should().BeNull(); // ochiq
        AnswerChecker.Find(Unit1, "W2").Should().BeNull(); // ovoz
    }
}

public sealed class FirstAttemptTests
{
    private static readonly JsonObject Unit1 = InteractiveTestKit.Load("atf1_birlik_01");

    [Fact]
    public void Only_the_first_answer_of_each_question_counts()
    {
        var item = AnswerChecker.Find(Unit1, "A2m1")!; // 4 savol
        var state = new FirstAttemptState();

        FirstAttempt.Apply(state, item, Verdict(false, "0")).Should().BeNull();
        FirstAttempt.Apply(state, item, Verdict(true, "0")).Should().BeNull(); // qayta javob — hisobga olinmaydi
        FirstAttempt.Apply(state, item, Verdict(true, "1")).Should().BeNull();
        FirstAttempt.Apply(state, item, Verdict(true, "2")).Should().BeNull();
        var done = FirstAttempt.Apply(state, item, Verdict(true, "3"));

        done.Should().Be(new FirstScore(3, 4));
    }

    [Fact]
    public void State_survives_a_round_trip()
    {
        var item = AnswerChecker.Find(Unit1, "A2m1")!;
        var state = new FirstAttemptState();
        FirstAttempt.Apply(state, item, Verdict(true, "0"));

        var restored = FirstAttemptState.Read(state.Write());
        restored.Slots.Should().ContainKey("0");
        FirstAttemptState.Read("buzuq").Slots.Should().BeEmpty();
    }

    [Fact]
    public void Match_score_is_pairs_minus_mistakes()
    {
        var item = AnswerChecker.Find(Unit1, "A1m1")!; // 5 juftlik
        var state = new FirstAttemptState();

        FirstAttempt.Apply(state, item, Verdict(false, null));
        FirstAttempt.Apply(state, item, Verdict(false, null));
        for (var i = 0; i < 4; i++) FirstAttempt.Apply(state, item, Verdict(true, "L" + i)).Should().BeNull();
        FirstAttempt.Apply(state, item, Verdict(true, "L1")).Should().BeNull(); // takror — sanalmaydi

        FirstAttempt.Apply(state, item, Verdict(true, "L4")).Should().Be(new FirstScore(3, 5));
    }

    [Fact]
    public void Table_counts_the_first_check()
    {
        var unit = InteractiveTestKit.Load("atf2_birlik_05");
        var ex = Exercises(unit).Select(e => e.Exercise).First(e => Str(e, "tur") == "jadval");
        var item = AnswerChecker.Find(unit, Str(ex, "id")!)!;

        FirstAttempt.Apply(new FirstAttemptState(), item, new CheckVerdict(false, null, 5, []))
            .Should().Be(new FirstScore(5, item.Total));
    }

    private static CheckVerdict Verdict(bool correct, string? slot) => new(correct, slot, null, []);
}

public sealed class MiniTestTests
{
    private static readonly JsonObject Unit1 = InteractiveTestKit.Load("atf1_birlik_01");

    [Fact]
    public void Questions_carry_no_keys_or_transcripts()
    {
        var questions = MiniTest.Questions(Unit1, InteractiveTestKit.VersionId, InteractiveTestKit.Audio);
        var json = questions.ToJsonString();

        questions.Should().NotBeEmpty();
        json.Should().NotContain("\"javob").And.NotContain("\"matn\"").And.NotContain("\"tts\"");

        var listening = questions.OfType<JsonObject>().First(q => Str(q, "kon") == "tinglash");
        Str(listening, "audio").Should().HaveLength(16);
        Str(listening, "audio").Should().NotBe(InteractiveTestKit.Audio.Handle(InteractiveTestKit.VersionId, "x01", AudioHandleCodec.Display));
    }

    [Fact]
    public void Grading_counts_choice_and_text_and_skips_oral()
    {
        var source = Objects(Obj(Unit1, "test"), "savollar").ToList();
        var answers = source.Select((q, i) => Str(q, "javob_matn") is { } text
                ? new MiniTestAnswer(i, null, text)
                : new MiniTestAnswer(i, Number(q, "javob"), null))
            .ToList();

        var grade = MiniTest.Grade(Unit1, answers);

        grade.OralCount.Should().Be(source.Count(q => Flag(q, "ochiq") == true));
        grade.Max.Should().Be(source.Count - grade.OralCount);
        grade.Score.Should().Be(grade.Max);
        MiniTest.Grade(Unit1, []).Score.Should().Be(0);
    }
}

public sealed class AudioHandleTests
{
    private readonly AudioHandleCodec _codec = InteractiveTestKit.Audio;

    [Fact]
    public void Handle_is_stable_and_context_bound()
    {
        var display = _codec.Handle(7, "s02", AudioHandleCodec.Display);

        display.Should().Be(_codec.Handle(7, "s02", AudioHandleCodec.Display));
        display.Should().NotBe(_codec.Handle(7, "s02", AudioHandleCodec.Quiz));
        display.Should().NotBe(_codec.Handle(8, "s02", AudioHandleCodec.Display));
        display.Should().NotContain("s02");
    }

    [Fact]
    public void Handle_resolves_back_to_the_audio_id_of_that_version_only()
    {
        var quiz = _codec.Handle(7, "s04", AudioHandleCodec.Quiz);
        var ids = new[] { "s01", "s02", "s04" };

        _codec.Resolve(7, ids, quiz).Should().Be("s04");
        _codec.Resolve(8, ids, quiz).Should().BeNull();
        _codec.Resolve(7, ids, "x").Should().BeNull();
    }

    [Fact]
    public void Unit_ticket_is_bound_to_unit_and_expires()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2026-10-29T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var tickets = new UnitTicketCodec(InteractiveTestKit.Signer, clock);
        var (token, _) = tickets.Issue(5, 11);

        tickets.TryResolveUserId(token, 5).Should().Be(11);
        tickets.TryResolveUserId(token, 6).Should().BeNull();
        tickets.TryResolveUserId(token.Replace(".11.", ".12.", StringComparison.Ordinal), 5).Should().BeNull();

        clock.Now = clock.Now.AddMinutes(16);
        tickets.TryResolveUserId(token, 5).Should().BeNull();
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

public sealed class ForbiddenTermsTests
{
    [Theory]
    [InlineData("Bugun masjidga bordik", "masjidga")]
    [InlineData("بِسْمِ اللهِ", "بسم الله")]
    [InlineData("لَبَّيْكَ", "لبيك")]
    public void Finds_terms(string text, string term) => ForbiddenTerms.Find(text).Should().Contain(term);

    [Theory]
    [InlineData("Surat chizdik")]
    [InlineData("الحَجَرُ كَبِيرٌ")] // "tosh" — "الحج" emas
    [InlineData("Roza guli")]
    public void Ignores_lookalikes(string text) => ForbiddenTerms.Find(text).Should().BeEmpty();
}
