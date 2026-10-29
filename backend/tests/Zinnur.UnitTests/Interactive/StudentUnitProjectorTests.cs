using System.Text.Json.Nodes;
using Zinnur.Application.Interactive.Content;
using Zinnur.Application.Interactive.Import;
using static Zinnur.Application.Interactive.Content.UnitJson;

namespace Zinnur.UnitTests.Interactive;

/// <summary>
/// 🔴 O'quvchi proyeksiyasida javob kalitlari, ustoz izohlari va audio id'lari
/// YO'Qligini haqiqiy kontent ustida tekshiradi (rejaning 9-bo'limi, 2-mezon).
/// </summary>
public sealed class StudentUnitProjectorTests
{
    /// <summary>Kontentdagi kalit nomlari — proyeksiyada HECH QAYERDA bo'lmasligi kerak.</summary>
    private static readonly string[] ForbiddenKeys =
        ["ustozga", "tts", "javob", "javob_matn", "tuzatish", "javoblar", "namuna", "bosh", "ovoz_id", "maqsad_gap"];

    public static TheoryData<string> Units()
    {
        var data = new TheoryData<string>();
        foreach (var f in InteractiveTestKit.Fixtures) data.Add(f);
        return data;
    }

    [Theory]
    [MemberData(nameof(Units))]
    public void Projection_has_no_answer_or_teacher_keys(string name)
    {
        var projected = Project(name);

        var keys = AllKeys(projected).ToHashSet(StringComparer.Ordinal);
        keys.Should().NotIntersectWith(ForbiddenKeys);
    }

    [Theory]
    [MemberData(nameof(Units))]
    public void Teacher_notes_never_reach_the_student(string name)
    {
        var unit = InteractiveTestKit.Load(name);
        var json = Project(name).ToJsonString();

        var notes = UnitJson.Blocks(unit).Select(b => Str(b.Block, "ustozga")).OfType<string>().ToList();
        notes.Should().NotBeEmpty();

        foreach (var note in notes)
            json.Should().NotContain(System.Text.Json.JsonEncodedText.Encode(note).ToString());
    }

    [Theory]
    [MemberData(nameof(Units))]
    public void Audio_ids_are_replaced_by_handles(string name)
    {
        var unit = InteractiveTestKit.Load(name);
        var ids = ContentVerifier.AudioIds(unit);
        var handles = AudioValues(Project(name)).ToList();

        handles.Should().NotBeEmpty();
        handles.Should().OnlyContain(h => h.Length == 16);
        handles.Should().NotIntersectWith(ids);
    }

    [Fact]
    public void Sound_quiz_handles_differ_from_the_displayed_word_handles()
    {
        // 🔴 Tovush blokida savol audiosi (s02) ekrandagi so'zning audiosi bilan AYNI fayl.
        var projected = Project("atf1_birlik_01");

        foreach (var block in ProjectedBlocks(projected).Where(b => Str(b, "tur") == "tovush"))
        {
            var shown = Objects(block, "juftlar").SelectMany(j => Strings(j, "audio")).ToHashSet(StringComparer.Ordinal);
            var quiz = Objects(block, "test").Select(t => Str(t, "audio")).OfType<string>().ToList();

            quiz.Should().NotBeEmpty();
            quiz.Should().NotIntersectWith(shown);
            Objects(block, "test").Should().OnlyContain(t => t["javob"] == null);
        }
    }

    [Fact]
    public void Match_exercise_hides_the_pairing()
    {
        var unit = InteractiveTestKit.Load("atf1_birlik_01");
        var source = Exercises(unit).Select(e => e.Exercise).First(e => Str(e, "tur") == "ula");
        var projected = ExercisesOf(Project("atf1_birlik_01")).First(e => Str(e, "id") == Str(source, "id"));

        var left = Objects(projected, "chap").ToList();
        var right = Objects(projected, "ong").ToList();

        left.Should().HaveCount(Arr(source, "juftlar").Count);
        right.Should().HaveCount(left.Count);
        projected["juftlar"].Should().BeNull();

        // Belgilar indeksni oshkor qilmaydi va o'ng ustun manba tartibida emas.
        right.Select(r => Str(r, "id")).Should().OnlyContain(id => id!.StartsWith('R') && id.Length == 11);
        right.Select(r => Str(r, "text"))
            .Should().NotEqual(Arr(source, "juftlar").OfType<JsonArray>().Select(p => AsString(p[1])));
    }

    [Fact]
    public void Table_exercise_sends_only_given_cells()
    {
        var unit = InteractiveTestKit.Load("atf2_birlik_05");
        var source = Exercises(unit).Select(e => e.Exercise).First(e => Str(e, "tur") == "jadval");
        var projected = ExercisesOf(Project("atf2_birlik_05")).First(e => Str(e, "id") == Str(source, "id"));

        var rows = Arr(projected, "qatorlar").OfType<JsonArray>().ToList();
        var given = Arr(source, "bosh").OfType<JsonArray>().Select(p => ((int)p[0]!, (int)p[1]!)).ToHashSet();

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Count; c++)
            {
                if (given.Contains((r, c))) rows[r][c].Should().NotBeNull();
                else rows[r][c].Should().BeNull($"katak {r}:{c} — kalit");
            }
        }
    }

    [Fact]
    public void Writing_sample_and_dictation_words_are_not_sent()
    {
        var unit = InteractiveTestKit.Load("atf1_birlik_01");
        var projected = Project("atf1_birlik_01");

        var json = projected.ToJsonString();
        var dictation = Objects(unit, "mashq_daftari").First(w => Str(w, "tur") == "diktant");
        Objects(projected, "daftar").First(w => Str(w, "tur") == "diktant")["sozlar"].Should().BeNull();

        var yozish = ProjectedBlocks(projected).First(b => Str(b, "tur") == "yozish");
        Objects(yozish, "pogonalar").Should().Contain(p => Flag(p, "hasNamuna") == true);
        json.Should().NotContain("\"namuna\"");
        Strings(dictation, "sozlar").Should().NotBeEmpty();
    }

    [Fact]
    public void Test_questions_are_not_in_the_unit_payload()
    {
        var projected = Project("atf1_birlik_01");

        var test = Obj(projected, "test");
        test.Should().NotBeNull();
        Number(test, "savollarSoni").Should().BeGreaterThan(0);
        test!["savollar"].Should().BeNull();
    }

    [Fact]
    public void Projection_is_stable_for_the_same_version()
    {
        Project("atf1_birlik_28").ToJsonString().Should().Be(Project("atf1_birlik_28").ToJsonString());
    }

    // ---------------------------------------------------------------- yordamchilar

    private static JsonObject Project(string name) =>
        InteractiveTestKit.Projector.Project(InteractiveTestKit.Load(name), InteractiveTestKit.VersionId);

    private static IEnumerable<JsonObject> ProjectedBlocks(JsonObject projected) =>
        Objects(projected, "darslar").SelectMany(d => Objects(d, "bloklar"));

    private static IEnumerable<JsonObject> ExercisesOf(JsonObject projected) =>
        ProjectedBlocks(projected).SelectMany(b => Objects(b, "mashqlar"));

    private static IEnumerable<string> AllKeys(JsonNode? node) => node switch
    {
        JsonObject o => o.SelectMany(kv => AllKeys(kv.Value).Prepend(kv.Key)),
        JsonArray a => a.SelectMany(AllKeys),
        _ => [],
    };

    private static IEnumerable<string> AudioValues(JsonNode? node) => node switch
    {
        JsonObject o => o.SelectMany(kv => kv.Key is "audio" or "audioToliq"
            ? kv.Value switch
            {
                JsonArray arr => arr.Select(AsString).OfType<string>(),
                _ => AsString(kv.Value) is { } s ? [s] : [],
            }
            : AudioValues(kv.Value)),
        JsonArray a => a.SelectMany(AudioValues),
        _ => [],
    };
}
