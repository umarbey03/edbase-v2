using System.Text.Json.Nodes;
using Zinnur.Application.Interactive.Content;
using static Zinnur.Application.Interactive.Content.UnitJson;

namespace Zinnur.Application.Interactive.Grading;

/// <summary>Mini-test savoliga javob: variant indeksi yoki yozma matn.</summary>
public sealed record MiniTestAnswer(int Index, int? Choice, string? Text);

/// <summary>Bitta savol natijasi.</summary>
public sealed record TestQuestionResult(int Index, bool? Correct, int? CorrectChoice, string? Javob, bool Oral);

/// <summary>Mini-test natijasi. <see cref="Max"/> — avtomatik tekshiriladigan savollar soni.</summary>
public sealed record TestGrade(int Score, int Max, int OralCount, IReadOnlyList<TestQuestionResult> Questions);

/// <summary>
/// MINI-TEST (birlik oxiridagi `test.savollar`) — kalitsiz savollar va baholash
/// (2026-10-29).
///
/// 🔴 `matn` (tinglash savolida eshittirilgan gapning matni) va `tts` —
///   to'g'ri javobni aytib qo'yadi, savollar ro'yxatiga KIRMAYDI.
/// ★ Gapirish (`ochiq`) savoli avtomatik baholanmaydi — natijada "N/M + ovozli
///   qism ustozda" (ovozli qism vazifa oqimi keyingi bosqichda).
/// </summary>
public static class MiniTest
{
    public static JsonArray Questions(JsonObject unit, long versionId, AudioHandleCodec audio)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(audio);

        var result = new JsonArray();
        var i = 0;

        foreach (var q in Objects(Obj(unit, "test"), "savollar"))
        {
            var audioId = Str(q, "audio");
            var mode = Flag(q, "ochiq") == true ? "oral"
                : Str(q, "javob_matn") is not null ? "text"
                : "choice";

            result.Add(new JsonObject
            {
                ["index"] = i++,
                ["kon"] = Str(q, "kon"),
                ["uz"] = Str(q, "uz"),
                ["mode"] = mode,
                ["variantlar"] = mode == "choice"
                    ? new JsonArray([.. Strings(q, "variantlar").Select(v => (JsonNode?)JsonValue.Create(v))])
                    : null,
                ["audio"] = audioId is null ? null : audio.Handle(versionId, audioId, AudioHandleCodec.Quiz),
            });
        }

        return result;
    }

    public static TestGrade Grade(JsonObject unit, IReadOnlyList<MiniTestAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(answers);

        var byIndex = answers
            .GroupBy(a => a.Index)
            .ToDictionary(g => g.Key, g => g.First());

        var results = new List<TestQuestionResult>();
        int score = 0, max = 0, oral = 0, i = 0;

        foreach (var q in Objects(Obj(unit, "test"), "savollar"))
        {
            byIndex.TryGetValue(i, out var a);

            if (Flag(q, "ochiq") == true)
            {
                oral++;
                results.Add(new TestQuestionResult(i, null, null, null, Oral: true));
            }
            else if (Str(q, "javob_matn") is { } text)
            {
                max++;
                var ok = a?.Text is { Length: > 0 } given && ArabicNorm.Same(given, text);
                if (ok) score++;
                results.Add(new TestQuestionResult(i, ok, null, text, Oral: false));
            }
            else
            {
                max++;
                var key = Number(q, "javob") ?? -1;
                var ok = a?.Choice is { } choice && choice == key;
                if (ok) score++;
                results.Add(new TestQuestionResult(i, ok, key, null, Oral: false));
            }

            i++;
        }

        return new TestGrade(score, max, oral, results);
    }
}
