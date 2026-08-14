using System.Globalization;

namespace Zinnur.Application.Notifications;

/// <summary>
/// ILOVA ICHIDAGI bildirishnoma matnlari (qo'ng'iroqcha ro'yxati).
///
/// ══════════════════════════════════════════════════════════════════════
/// 🔴 <see cref="Zinnur.Application.Telegram.TelegramTemplates"/> DAN
///    FARQI — VA NEGA IKKALASI BIRLASHTIRILMAGAN
///
/// Bu yerdagi matn SOF: hech qanday <c>&lt;b&gt;</c> yo'q va foydalanuvchi
/// ma'lumoti <c>NotificationText.Parameter</c> orqali O'TKAZILMAYDI.
/// Telegram shablonlari esa aynan teskari: ular HTML yasaydi va har
/// parametrni ekranlaydi.
///
/// Bittasini ikkinchisiga ishlatib bo'lmaydi:
///   • Telegram matnini Vue ro'yxatiga qo'ysak — o'quvchi so'zma-so'z
///     <c>&lt;b&gt;Vazifa&lt;/b&gt;</c> va <c>&amp;amp;</c> ni ko'radi
///     (yoki `v-html` ishlatilib, XSS yo'li ochiladi);
///   • sof matnni Telegram'ga <c>parse_mode=HTML</c> bilan yuborsak,
///     o'quvchi ismidagi bitta <c>&lt;</c> butun so'rovni yiqitadi
///     (`400 can't parse entities`) va xabar UMUMAN yetib bormaydi.
///
/// Ya'ni bu takrorlanish emas — IKKI XIL CHIQISH FORMATI. Umumiy qism
/// (ballni formatlash) esa <see cref="FormatScore"/> da BIR MARTA yozilgan
/// va Telegram shabloni ham AYNAN shuni chaqiradi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public static class NotificationTemplates
{
    /// <summary>Vazifa tekshirilgani haqidagi sarlavha.</summary>
    public static string SubmissionGradedTitle() => "Vazifa tekshirildi";

    /// <summary>
    /// Vazifa tekshirilgani haqidagi tana matni.
    ///
    /// ★ IZOH (ustoz yozgan fikr) MATNGA KIRITILADI, faqat sarlavha emas:
    /// o'quvchi uchun eng qimmat ma'lumot AYNAN u. Uzun izoh
    /// <c>Notification.Create</c> ichida xavfsiz qirqiladi — bu yerda
    /// qirqish TAKRORLANMAYDI (ikki joyda bo'lsa chegaralar ajralib
    /// ketardi).
    /// </summary>
    /// <param name="assignmentTitle">Vazifa sarlavhasi (bo'sh bo'lishi mumkin).</param>
    /// <param name="score">Qo'yilgan ball.</param>
    /// <param name="maxScore">Maksimal ball.</param>
    /// <param name="feedback">Ustozning izohi (bo'lmasligi mumkin).</param>
    public static string SubmissionGradedBody(
        string? assignmentTitle, decimal score, decimal maxScore, string? feedback)
    {
        var title = Clean(assignmentTitle);
        var head = title.Length == 0 ? "Vazifa" : title;

        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{head} — {FormatScore(score)}/{FormatScore(maxScore)} ball.");

        var note = Clean(feedback);

        return note.Length == 0 ? text : $"{text} Izoh: {note}";
    }

    /// <summary>
    /// Ballni matnga aylantiradi.
    ///
    /// 🔴 <see cref="CultureInfo.InvariantCulture"/> SHART VA BU XATO
    /// TUZATISHI EMAS, OLDINI OLISH: <c>Score</c> va <c>MaxScore</c> —
    /// <c>decimal</c>. Serverning madaniyati <c>uz-UZ</c> yoki <c>ru-RU</c>
    /// bo'lsa o'nlik ajratgich VERGUL bo'ladi va o'quvchi "4,5/5" ni
    /// ko'rardi — Telegram matnida bu shunchaki g'alati, lekin AYNI matn
    /// keyin CSV yoki hisobotga tushsa vergul ustunni bo'lib yuborardi.
    /// Konteynerdagi madaniyat esa muhitga bog'liq, ya'ni bu xato faqat
    /// PRODUKSIYADA chiqadigan turdan.
    ///
    /// ★ <c>0.##</c> — butun ball "5" bo'lib ko'rinadi, kasri borida
    /// "4.5". <c>ToString()</c> ning o'zi <c>5.00</c> berardi (bazadagi
    /// <c>numeric(5,2)</c> masshtabi tufayli) va bu ekranda shovqin edi.
    /// </summary>
    public static string FormatScore(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Bo'shliqni kesadi va ichki qator uzilishlarini bitta probelga
    /// aylantiradi: qo'ng'iroqcha qatori BIR-IKKI satrda chiziladi, ichida
    /// <c>\n</c> bo'lsa balandligi sakrab ketardi.
    /// </summary>
    private static string Clean(string? value)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0) return string.Empty;

        return text.IndexOfAny(['\r', '\n', '\t']) < 0
            ? text
            : string.Join(' ', text.Split(
                ['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
