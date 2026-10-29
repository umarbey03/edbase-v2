using System.Text.Json.Nodes;
using static Zinnur.Application.Interactive.Content.UnitJson;

namespace Zinnur.Application.Interactive.Content;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// O'QUVCHI PROYEKSIYASI — OQ RO'YXAT (2026-10-29)
/// ════════════════════════════════════════════════════════════════════════
///
/// `dars.json` da javob kalitlari va ustoz izohlari ochiq turadi. O'quvchiga
/// esa faqat shu sinf YIG'GAN obyekt boradi.
///
/// 🔴 OQ RO'YXAT, QORA EMAS: har blok va mashq turi uchun ruxsat etilgan
///   kalitlar NOMI bilan ko'chiriladi. Noma'lum kalit avtomatik tushib
///   qoladi — kontentga keyin yangi ustoz maydoni qo'shilsa ham u o'quvchiga
///   chiqmaydi. Kesiladiganlar (hech qachon ko'chirilmaydi): `ustozga`, `tts`,
///   `javob`, `javob_matn`, `tuzatish`, `ovlash.javoblar`, `jadval` ning `bosh`
///   dan tashqari kataklari, `diktant.sozlar`, `yozish.namuna`,
///   `test.savollar` (faqat soni), personajning `ovoz` i.
///
/// ★ AUDIO — id emas, belgi (<see cref="AudioHandleCodec"/>); `ula` —
///   belgilar (<see cref="MatchTokenCodec"/>), o'ng ustun aralashtirilgan.
///
/// ★ Kalitlar camelCase (frontend shu shaklni kutadi), qiymatlar — kitobdagidek.
/// </summary>
public sealed class StudentUnitProjector(AudioHandleCodec audio, MatchTokenCodec match)
{
    /// <summary>Proyeksiya sxemasi o'zgarsa oshiriladi (kesh kaliti va ETag uchun).</summary>
    public const int ProjectorVersion = 1;

    public JsonObject Project(JsonObject unit, long versionId)
    {
        ArgumentNullException.ThrowIfNull(unit);

        var ctx = new Ctx(versionId, audio, match);

        return new JsonObject
        {
            ["meta"] = Meta(Obj(unit, "meta")),
            ["personajlar"] = List(Objects(unit, "personajlar"), p => new JsonObject
            {
                ["id"] = Str(p, "id"),
                ["ar"] = Str(p, "ar"),
                ["uz"] = Str(p, "uz"),
                ["rang"] = Str(p, "rang"),
            }),
            ["lugat"] = List(Objects(unit, "lugat"), w => new JsonObject
            {
                ["id"] = Str(w, "id"),
                ["dars"] = Str(w, "dars"),
                ["ar"] = Str(w, "ar"),
                ["uz"] = Str(w, "uz"),
                ["tur"] = Str(w, "tur"),
                ["audio"] = ctx.Display(Str(w, "audio")),
            }),
            ["darslar"] = Parts(unit, ctx),
            ["test"] = TestSummary(Obj(unit, "test")),
            ["daftar"] = List(Objects(unit, "mashq_daftari"), w => Workbook(w, ctx)),
        };
    }

    // ================================================================ meta

    private static JsonObject Meta(JsonObject? meta) => new()
    {
        ["kursNomi"] = Str(meta, "kurs_nomi"),
        ["daraja"] = Str(meta, "daraja"),
        ["birlik"] = Number(meta, "birlik"),
        ["birlikNomi"] = Str(meta, "birlik_nomi"),
        ["nomUz"] = Str(meta, "nom_uz"),
        ["nomAr"] = Str(meta, "nom_ar"),
        ["shior"] = Str(meta, "shior"),
        ["kirish"] = Str(meta, "kirish"),
        ["canDo"] = StrArr(meta, "can_do"),
        ["grammatika"] = StrArr(meta, "grammatika"),
        ["vazifa"] = Str(meta, "vazifa"),
        ["tovush"] = Str(meta, "tovush"),
    };

    private static JsonObject? TestSummary(JsonObject? test)
    {
        if (test is null) return null;

        var questions = Objects(test, "savollar").ToList();

        return new JsonObject
        {
            ["nom"] = Str(test, "nom"),
            ["savollarSoni"] = questions.Count,
            ["ovozliSoni"] = questions.Count(q => Flag(q, "ochiq") == true),
        };
    }

    // ================================================================ bloklar

    private static JsonArray Parts(JsonObject unit, Ctx ctx)
    {
        var result = new JsonArray();

        if (unit["darslar"] is not JsonObject parts) return result;

        foreach (var (key, value) in parts)
        {
            result.Add(new JsonObject
            {
                ["key"] = key,
                ["nomUz"] = Str(value, "nom_uz"),
                ["nomAr"] = Str(value, "nom_ar"),
                ["qisqa"] = Str(value, "qisqa"),
                ["bloklar"] = List(Objects(value, "bloklar"), b => Block(b, ctx)),
            });
        }

        return result;
    }

    private static JsonObject Block(JsonObject b, Ctx ctx)
    {
        var tur = Str(b, "tur") ?? string.Empty;

        var o = new JsonObject
        {
            ["id"] = Str(b, "id"),
            ["tur"] = tur,
            ["vaqt"] = Number(b, "vaqt"),
            ["sarlavhaUz"] = Str(b, "sarlavha_uz"),
            ["sarlavhaAr"] = Str(b, "sarlavha_ar"),
            ["korsatma"] = Str(b, "korsatma"),
        };

        switch (tur)
        {
            case "isinish":
                o["kopru"] = List(Objects(b, "kopru"), k => ArUz(k));
                o["lugatIds"] = StrArr(b, "lugat_ids");
                break;

            case "grammatika":
                o["kuzat"] = List(Objects(b, "kuzat"), k => new JsonObject
                {
                    ["ar"] = Str(k, "ar"),
                    ["uz"] = Str(k, "uz"),
                    ["rangli"] = Str(k, "rangli"),
                });
                o["savol"] = Str(b, "savol");
                o["qoida"] = StrArr(b, "qoida");
                o["jadval"] = Obj(b, "jadval") is { } t
                    ? new JsonObject
                    {
                        ["sarlavha"] = Str(t, "sarlavha"),
                        ["ustunlar"] = StrArr(t, "ustunlar"),
                        ["qatorlar"] = Rows(t),
                        ["audio"] = ctx.DisplayAll(Strings(t, "audio")),
                    }
                    : null;
                o["kontrast"] = Obj(b, "kontrast") is { } c
                    ? new JsonObject { ["uz"] = Str(c, "uz"), ["ar"] = Str(c, "ar"), ["izoh"] = Str(c, "izoh") }
                    : null;
                break;

            case "harf":
                o["harflar"] = List(Objects(b, "harflar"), h => new JsonObject
                {
                    ["h"] = Str(h, "h"),
                    ["nom"] = Str(h, "nom"),
                    ["tovush"] = Str(h, "tovush"),
                    ["shakllar"] = StrArr(h, "shakllar"),
                    ["audio"] = ctx.Display(Str(h, "audio")),
                    ["misollar"] = List(Objects(h, "misollar"), m => new JsonObject
                    {
                        ["ar"] = Str(m, "ar"),
                        ["uz"] = Str(m, "uz"),
                        ["audio"] = ctx.Display(Str(m, "audio")),
                    }),
                });
                break;

            case "tovush":
                o["izoh"] = Str(b, "izoh");
                o["juftlar"] = List(Objects(b, "juftlar"), j => new JsonObject
                {
                    ["sozlar"] = StrArr(j, "sozlar"),
                    ["uz"] = StrArr(j, "uz"),
                    ["audio"] = ctx.DisplayAll(Strings(j, "audio")),
                });

                // 🔴 Savolning audiosi — BOSHQA kontekst belgisi, javob YO'Q.
                o["test"] = List(Objects(b, "test"), t => new JsonObject
                {
                    ["audio"] = ctx.Quiz(Str(t, "audio")),
                    ["variantlar"] = StrArr(t, "variantlar"),
                });
                o["testKey"] = $"{Str(b, "id")}:test";
                o["ehtiyot"] = Obj(b, "ehtiyot") is { } e
                    ? new JsonObject { ["sarlavha"] = Str(e, "sarlavha"), ["matn"] = Str(e, "matn") }
                    : null;
                break;

            case "yozish":
                // Namuna oldindan yuborilmaydi — faqat "Yozdim" dan keyin (`reveal`).
                o["pogonalar"] = List(Objects(b, "pogonalar"), p => new JsonObject
                {
                    ["n"] = Number(p, "n"),
                    ["uz"] = Str(p, "uz"),
                    ["hasNamuna"] = Str(p, "namuna") is not null,
                });
                break;

            case "yakun":
                o["cando"] = Flag(b, "cando") ?? false;
                break;

            case "vazifa":
                o["maqsad"] = Str(b, "maqsad");
                o["qolip"] = StrArr(b, "qolip");
                o["baholash"] = List(Objects(b, "baholash"), x => new JsonObject
                {
                    ["mezon"] = Str(x, "mezon"),
                    ["ball"] = Number(x, "ball"),
                });
                o["rollar"] = List(Objects(b, "rollar"), r => new JsonObject
                {
                    ["ism"] = Str(r, "ism"),
                    ["belgi"] = Str(r, "belgi"),
                    ["qatorlar"] = StrArr(r, "qatorlar"),
                });
                o["natija"] = Str(b, "natija");
                break;

            case "tabir":
                o["kartochkalar"] = List(Objects(b, "kartochkalar"), k => new JsonObject
                {
                    ["ibora"] = Str(k, "ibora"),
                    ["uz"] = Str(k, "uz"),
                    ["misollar"] = StrArr(k, "misollar"),
                    ["siz"] = Str(k, "siz"),
                    ["audio"] = ctx.Display(Str(k, "audio")),
                });
                break;

            case "dialog":
                o["vaziyat"] = Str(b, "vaziyat");
                o["bosqichlar"] = List(Objects(b, "bosqichlar"), s => new JsonObject
                {
                    ["n"] = Number(s, "n"),
                    ["uz"] = Str(s, "uz"),
                });
                o["satrlar"] = List(Objects(b, "satrlar"), s => new JsonObject
                {
                    ["kim"] = Str(s, "kim"),
                    ["ar"] = Str(s, "ar"),
                    ["uz"] = Str(s, "uz"),
                    ["audio"] = ctx.Display(Str(s, "audio")),
                });
                o["audioToliq"] = ctx.Display(Str(b, "audio_toliq"));
                break;

            case "oqish":
                o["oldin"] = Str(b, "oldin");
                o["matn"] = StrArr(b, "matn");
                o["yangi"] = List(Objects(b, "yangi"), y => ArUz(y));
                o["audio"] = ctx.Display(Str(b, "audio"));
                o["ovoz"] = Str(b, "ovoz");
                break;
        }

        // Har qanday blokdagi mashqlar — turi bo'yicha oq ro'yxat.
        var exercises = Objects(b, "mashqlar").ToList();
        if (exercises.Count > 0)
            o["mashqlar"] = List(exercises, e => Exercise(e, b, ctx));

        return o;
    }

    // ================================================================ mashqlar

    private static JsonObject Exercise(JsonObject e, JsonObject block, Ctx ctx)
    {
        var id = Str(e, "id") ?? string.Empty;
        var tur = Str(e, "tur") ?? string.Empty;

        var o = new JsonObject
        {
            ["id"] = id,
            ["tur"] = tur,
            ["korsatma"] = Str(e, "korsatma"),
            ["bosqich"] = Number(e, "bosqich"),
        };

        switch (tur)
        {
            case "tanla":
                o["savollar"] = List(Objects(e, "savollar"), q => new JsonObject
                {
                    ["uz"] = Str(q, "uz"),
                    ["variantlar"] = StrArr(q, "variantlar"),
                    ["audio"] = ctx.Quiz(Str(q, "audio")),
                });
                break;

            case "tf":
                o["gaplar"] = List(Objects(e, "gaplar"), g => new JsonObject { ["ar"] = Str(g, "ar") });
                break;

            case "ula":
                FillMatch(o, Arr(e, "juftlar"), id, ctx);
                break;

            case "ovlash":
                o["sozlar"] = new JsonArray([.. ReadingWords(block).Select(w => (JsonNode?)JsonValue.Create(w))]);
                o["soni"] = Strings(e, "javoblar").Select(Grading.ArabicNorm.Normalize).Distinct(StringComparer.Ordinal).Count();
                break;

            case "jadval":
                o["ustunlar"] = StrArr(e, "ustunlar");
                o["qatorlar"] = GivenCells(e);
                break;

            case "toldir":
                o["gaplar"] = Fill(e);
                break;

            case "tartibla":
                o["gaplar"] = List(Objects(e, "gaplar"), g => new JsonObject
                {
                    ["sozlar"] = new JsonArray([.. Strings(g, "sozlar")
                        .Select((w, i) => (w, key: ctx.Match.Token(ctx.VersionId, id, 'T', i)))
                        .OrderBy(x => x.key, StringComparer.Ordinal)
                        .Select(x => (JsonNode?)JsonValue.Create(x.w))]),
                });
                break;
        }

        return o;
    }

    private static JsonObject Workbook(JsonObject w, Ctx ctx)
    {
        var id = Str(w, "id") ?? string.Empty;
        var tur = Str(w, "tur") ?? string.Empty;

        var o = new JsonObject
        {
            ["id"] = id,
            ["sessiya"] = Number(w, "sessiya"),
            ["tur"] = tur,
            ["uz"] = Str(w, "uz"),
        };

        switch (tur)
        {
            case "ula":
                FillMatch(o, Arr(w, "juftlar"), id, ctx);
                break;

            case "toldir":
                o["gaplar"] = Fill(w);
                break;

            case "diktant":
                // 🔴 `sozlar` — kalit, yuborilmaydi; audio — "quiz" belgisi.
                o["audio"] = ctx.QuizAll(Strings(w, "audio"));
                break;

            case "tanla":
                o["savollar"] = List(Objects(w, "savollar"), q => new JsonObject
                {
                    ["uz"] = Str(q, "uz"),
                    ["variantlar"] = StrArr(q, "variantlar"),
                });
                break;
        }

        return o;
    }

    private static void FillMatch(JsonObject o, JsonArray pairs, string itemKey, Ctx ctx)
    {
        var rows = pairs.OfType<JsonArray>().ToList();

        o["chap"] = new JsonArray([.. rows.Select((p, i) => (JsonNode?)new JsonObject
        {
            ["id"] = ctx.Match.Token(ctx.VersionId, itemKey, 'L', i),
            ["text"] = AsString(p.Count > 0 ? p[0] : null),
        })]);

        o["ong"] = new JsonArray([.. rows
            .Select((p, i) => (text: AsString(p.Count > 1 ? p[1] : null), id: ctx.Match.Token(ctx.VersionId, itemKey, 'R', i)))
            .OrderBy(x => x.id, StringComparer.Ordinal)
            .Select(x => (JsonNode?)new JsonObject { ["id"] = x.id, ["text"] = x.text })]);
    }

    private static JsonArray Fill(JsonObject e) =>
        List(Objects(e, "gaplar"), g => new JsonObject
        {
            ["ar"] = Str(g, "ar"),
            ["variantlar"] = StrArr(g, "variantlar"),
        });

    /// <summary>`jadval` mashqi: faqat `bosh` (berilgan) kataklar, qolgani <c>null</c>.</summary>
    private static JsonArray GivenCells(JsonObject e)
    {
        var given = Arr(e, "bosh").OfType<JsonArray>()
            .Where(p => p.Count == 2)
            .Select(p => (r: (int?)p[0] ?? -1, c: (int?)p[1] ?? -1))
            .ToHashSet();

        var result = new JsonArray();
        var r = 0;
        foreach (var row in Arr(e, "qatorlar").OfType<JsonArray>())
        {
            var cells = new JsonArray();
            for (var c = 0; c < row.Count; c++)
                cells.Add(given.Contains((r, c)) ? AsString(row[c]) : null);
            result.Add(cells);
            r++;
        }

        return result;
    }

    private static JsonArray Rows(JsonObject table) =>
        new([.. Arr(table, "qatorlar").OfType<JsonArray>()
            .Select(row => (JsonNode?)new JsonArray([.. row.Select(c => (JsonNode?)JsonValue.Create(AsString(c)))]))]);

    // ================================================================ yordamchilar

    private static JsonObject ArUz(JsonObject x) => new() { ["ar"] = Str(x, "ar"), ["uz"] = Str(x, "uz") };

    private static JsonArray StrArr(JsonNode? node, string key) =>
        new([.. Strings(node, key).Select(s => (JsonNode?)JsonValue.Create(s))]);

    private static JsonArray List(IEnumerable<JsonObject> items, Func<JsonObject, JsonObject> map) =>
        new([.. items.Select(x => (JsonNode?)map(x))]);

    private sealed record Ctx(long VersionId, AudioHandleCodec Audio, MatchTokenCodec Match)
    {
        public string? Display(string? id) => id is null ? null : Audio.Handle(VersionId, id, AudioHandleCodec.Display);

        public string? Quiz(string? id) => id is null ? null : Audio.Handle(VersionId, id, AudioHandleCodec.Quiz);

        public JsonArray DisplayAll(IEnumerable<string> ids) =>
            new([.. ids.Select(id => (JsonNode?)JsonValue.Create(Display(id)))]);

        public JsonArray QuizAll(IEnumerable<string> ids) =>
            new([.. ids.Select(id => (JsonNode?)JsonValue.Create(Quiz(id)))]);
    }
}
