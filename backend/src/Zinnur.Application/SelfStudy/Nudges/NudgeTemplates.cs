using System.Globalization;
using Zinnur.Application.Notifications;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.SelfStudy.Nudges;

/// <summary>Matn yasash uchun o'quvchi holati.</summary>
/// <param name="StudentName">O'quvchi ismi (F.I.Sh.).</param>
/// <param name="LessonName">Hozirgi (davom ettiriladigan) dars nomi.</param>
/// <param name="CompletedLessons">Kurator «o'zlashtirdi» degan darslar soni.</param>
/// <param name="TotalLessons">Kursdagi jami darslar.</param>
/// <param name="NeedsWork">Hozirgi darsda kurator «qayta ishlang» degan.</param>
/// <param name="CuratorName">Kurator ismi (biriktirilmagan bo'lsa <c>null</c>).</param>
/// <param name="IdleDays">Necha kundan beri faolsiz.</param>
public sealed record NudgeContext(
    string StudentName,
    string? LessonName,
    int CompletedLessons,
    int TotalLessons,
    bool NeedsWork,
    string? CuratorName,
    int IdleDays)
{
    public int Percent => TotalLessons <= 0 ? 0 : (int)Math.Round(CompletedLessons * 100.0 / TotalLessons);
}

/// <summary>Tayyor xabar: Telegram uchun HTML va qo'ng'iroqcha uchun sof matn.</summary>
public sealed record NudgeMessage(string Title, string TelegramHtml, string PlainBody);

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// ESLATMA MATNLARI (2026-11-25)
/// ════════════════════════════════════════════════════════════════════════
///
/// QOIDALAR (loyiha egasi):
///   • NEYTRAL — diniy atama yo'q;
///   • UYALTIRMAYDI — «nega kirmadingiz?» emas, «sizni kutyapmiz»;
///   • ANIQ — ism, dars nomi, o'zlashtirilgan darslar soni: umumiy «darsga
///     kiring» matni bir haftada e'tiborsiz qoladi;
///   • KICHIK QADAM — «15 daqiqa», «bitta video»: katta va'da qo'rqitadi.
///
/// ★ IKKI FORMAT BITTA MANBADAN: <see cref="Fmt"/> HTML rejimida qalin
/// qiladi va qiymatni ekranlaydi, sof rejimda — shunchaki matn. Shu tufayli
/// Telegram va qo'ng'iroqchadagi gap hech qachon ajralib ketmaydi.
/// </summary>
public static class NudgeTemplates
{
    /// <summary>Har bosqichdagi variantlar soni (rotatsiya va o'lchov uchun).</summary>
    public static int VariantCount(NudgeStage stage) => stage switch
    {
        NudgeStage.Day1 => 3,
        NudgeStage.Day2 => 2,
        _ => 1,
    };

    // ================================================================ o'quvchiga

    public static NudgeMessage ForStudent(NudgeStage stage, int variant, NudgeContext c)
    {
        ArgumentNullException.ThrowIfNull(c);

        return new NudgeMessage(
            Title(stage, c),
            Body(stage, variant, c, new Fmt(Html: true)),
            Body(stage, variant, c, new Fmt(Html: false)));
    }

    private static string Title(NudgeStage stage, NudgeContext c) => stage switch
    {
        _ when c.NeedsWork && stage is NudgeStage.Day1 or NudgeStage.Day2 => "Kurator izohi sizni kutyapti",
        NudgeStage.Ask => "Darslar to'xtab qoldi",
        NudgeStage.Final => "Joyingiz saqlanib turibdi",
        NudgeStage.SnoozeReturn => "Eslatishimizni so'ragan edingiz",
        _ => "Darsingiz sizni kutyapti",
    };

    private static string Body(NudgeStage stage, int variant, NudgeContext c, Fmt f)
    {
        var name = f.P(c.StudentName);
        var lesson = string.IsNullOrWhiteSpace(c.LessonName) ? null : f.B($"«{f.P(c.LessonName)}»");

        // «Qayta ishlang» — eng ko'p tiqilib qolinadigan joy: umumiy matn
        // o'rniga aniq yechim aytiladi (tuzatsangiz — keyingi dars ochiladi).
        if (c.NeedsWork && stage is NudgeStage.Day1 or NudgeStage.Day2)
        {
            var curator = string.IsNullOrWhiteSpace(c.CuratorName)
                ? "kuratoringiz"
                : $"kuratoringiz {f.B(f.P(c.CuratorName))}";

            return $"🔓 {name}, {curator} {lesson ?? "darsingiz"} bo'yicha izoh qoldirgan edi."
                + f.Gap
                + "Ishni tuzatib qayta yuborsangiz, kurator tekshiradi va "
                + f.B("keyingi dars ochiladi") + "."
                + f.Gap
                + "Tushunarsiz joyi bo'lsa — ilovadagi chat orqali yozing, yordam beramiz.";
        }

        return stage switch
        {
            NudgeStage.Day1 => (variant % 3) switch
            {
                0 when lesson is not null =>
                    $"👋 {name}, {lesson} sizni kutyapti."
                    + f.Gap
                    + $"Bugun atigi {f.B("15 daqiqa")} ajrating — bitta kichik qadam ham odatni saqlab qoladi. 🎯",

                1 when c.CompletedLessons > 0 =>
                    $"💪 {name}, siz allaqachon {f.B($"{N(c.CompletedLessons)} ta darsni")} o'zlashtirdingiz"
                    + (c.TotalLessons > 0 ? $" — bu kursning {N(c.Percent)}%i." : ".")
                    + f.Gap
                    + "To'xtab qolmang: har kungi kichik qadam katta natija beradi.",

                _ =>
                    $"🌱 {name}, bugun o'zingizga 15 daqiqa ajrating."
                    + f.Gap
                    + (lesson is not null ? $"{lesson} — bitta video yoki bitta mashq. " : "Bitta video yoki bitta mashq. ")
                    + "Kichik, lekin har kuni — natija shundan keladi.",
            },

            NudgeStage.Day2 => (variant % 2) switch
            {
                0 =>
                    (lesson is not null ? $"📖 Keyingi darsingiz — {lesson}." : "📖 Keyingi darsingiz tayyor.")
                    + f.Gap
                    + $"{name}, bir kunlik tanaffus — tabiiy. Ikki kunlik tanaffus esa odatga aylanib qolishi mumkin. "
                    + "Bugun zanjirni tiklang 🙂",

                _ =>
                    $"⏳ {name}, eng qiyini — boshlash. Qolgani o'zi ketadi."
                    + f.Gap
                    + "Bugun " + (lesson is not null ? $"{lesson}dan " : string.Empty)
                    + "bitta video yoki bitta mashq — shu yetarli.",
            },

            NudgeStage.Ask =>
                $"🤔 {name}, darslar {N(c.IdleDays)} kundan beri to'xtab qoldi."
                + f.Gap
                + "Nima xalaqit beryapti? Ayting — birga yechim topamiz:",

            NudgeStage.Final =>
                $"🔄 {name}, joyingiz saqlanib turibdi."
                + f.Gap
                + (c.CompletedLessons > 0
                    ? $"O'zlashtirgan {f.B($"{N(c.CompletedLessons)} ta darsingiz")} va butun progressingiz yo'qolmaydi. "
                    : "Kursingiz ham, kuratoringiz ham tayyor. ")
                + "Qaytishga tayyor bo'lsangiz — qaerda to'xtagan bo'lsangiz, o'sha yerdan davom etasiz."
                + f.Gap
                + "Biz kutamiz. 🤝",

            NudgeStage.SnoozeReturn =>
                $"⏰ {name}, eslatishimizni so'ragan edingiz."
                + f.Gap
                + (lesson is not null ? $"Bugun {lesson}ga qaytamizmi? " : "Bugun darsga qaytamizmi? ")
                + "15 daqiqa kifoya.",

            _ => $"👋 {name}, darsingiz sizni kutyapti.",
        };
    }

    // ================================================================ tugmalar

    /// <summary>
    /// O'quvchi eslatmasi tugmalari. Har birida «Ilovani ochish» va «o'chirish» bor;
    /// 5-kungi savolda — javob variantlari.
    /// </summary>
    public static string StudentButtons(NudgeStage stage, long nudgeId)
    {
        var id = nudgeId.ToString(CultureInfo.InvariantCulture);
        var rows = new List<IReadOnlyList<(string, string)>>
        {
            new[] { ("🚀 Ilovani ochish", TelegramTemplates.OpenAppButtonData) },
        };

        if (stage == NudgeStage.Ask)
        {
            rows.Add([("✅ Bugun davom etaman", $"nd:{id}:{CodeToday}"), ("⏰ 3 kundan keyin", $"nd:{id}:{CodeSnooze}")]);
            rows.Add([("😕 Qiyin bo'lyapti", $"nd:{id}:{CodeHard}"), ("🗓 Vaqtim yo'q", $"nd:{id}:{CodeBusy}")]);
        }

        rows.Add([("🔕 Eslatmalarni o'chirish", $"nd:{id}:{CodeOff}")]);

        return TelegramTemplates.EncodeButtons(rows);
    }

    /// <summary>«O'chirildi» xabaridagi qayta yoqish tugmasi.</summary>
    public static string OptInButtons() =>
        TelegramTemplates.EncodeButtons([[("🔔 Eslatmalarni yoqish", CallbackOptIn)]]);

    public const string CallbackPrefix = "nd:";
    public const string CallbackOptIn = "nd:on";
    public const string CodeToday = "t";
    public const string CodeSnooze = "s";
    public const string CodeHard = "h";
    public const string CodeBusy = "b";
    public const string CodeOff = "off";

    // ================================================================ javoblar

    public static string BusyReplyText() =>
        "🙌 <b>Tushunamiz, vaqt — eng qimmat narsa.</b>\n\n"
        + "Kuniga <b>10–15 daqiqa</b> ham natija beradi: bitta video yoki bitta mashq. "
        + "2 kundan keyin yana bir eslatamiz.";

    public static string HardReplyText(string? curatorName) =>
        "🤝 <b>"
        + (string.IsNullOrWhiteSpace(curatorName)
            ? "O'quv bo'limiga xabar berdik"
            : $"Kuratoringiz {NotificationText.Parameter(curatorName)}ga xabar berdik")
        + "</b> — siz bilan tez orada bog'lanishadi.\n\n"
        + "Qaysi joyi qiyinligini ilovadagi chatda yozib qo'ysangiz, yordam yanada tezroq bo'ladi.";

    public static string OptOutReplyText() =>
        "🔕 <b>Avtomatik eslatmalar o'chirildi.</b>\n\n"
        + "Kurator xabarlari va dars bildirishnomalari kelaveradi. "
        + "Fikringizni o'zgartirsangiz — pastdagi tugmani bosing.";

    // ================================================================ kuratorga

    public static NudgeMessage CuratorIdleAlert(NudgeContext c)
    {
        ArgumentNullException.ThrowIfNull(c);

        string Build(Fmt f) =>
            $"📉 {f.B(f.P(c.StudentName))} {N(c.IdleDays)} kundan beri darsga kirmadi."
            + f.Line
            + (string.IsNullOrWhiteSpace(c.LessonName) ? string.Empty : $"Hozirgi dars: «{f.P(c.LessonName)}». ")
            + $"O'zlashtirgan: {N(c.CompletedLessons)}/{N(c.TotalLessons)}."
            + f.Gap
            + "Unga qisqa ovozli xabar yuboring — shaxsiy murojaat o'quvchini eng yaxshi qaytaradi.";

        return new NudgeMessage("O'quvchi darsga kirmayapti", Build(new Fmt(true)), Build(new Fmt(false)));
    }

    public static NudgeMessage CuratorHardAlert(NudgeContext c)
    {
        ArgumentNullException.ThrowIfNull(c);

        string Build(Fmt f) =>
            $"🆘 {f.B(f.P(c.StudentName))} «Qiyin bo'lyapti» tugmasini bosdi."
            + f.Line
            + (string.IsNullOrWhiteSpace(c.LessonName) ? string.Empty : $"Hozirgi dars: «{f.P(c.LessonName)}». ")
            + $"{N(c.IdleDays)} kundan beri faolsiz."
            + f.Gap
            + "Iloji bo'lsa, bugun o'zingiz yozing yoki ovozli xabar yuboring.";

        return new NudgeMessage("O'quvchiga yordam kerak", Build(new Fmt(true)), Build(new Fmt(false)));
    }

    // ================================================================ yordamchi

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Format: HTML (Telegram) yoki sof matn (qo'ng'iroqcha).</summary>
    private readonly record struct Fmt(bool Html)
    {
        /// <summary>Foydalanuvchi qiymati: HTML'da ekranlanadi va qirqiladi.</summary>
        public string P(string? value) =>
            Html ? NotificationText.Parameter(value, 120) : (value ?? string.Empty).Trim();

        /// <summary>Qalin (allaqachon tayyorlangan bo'lagi uchun).</summary>
        public string B(string text) => Html ? $"<b>{text}</b>" : text;

        /// <summary>Abzats: Telegram'da bo'sh qator, qo'ng'iroqchada probel (u 1–2 satrda chiziladi).</summary>
        public string Gap => Html ? "\n\n" : " ";

        public string Line => Html ? "\n" : " ";
    }
}
