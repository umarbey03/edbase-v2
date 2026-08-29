namespace Zinnur.Application.Telegram.Services;

/// <summary>
/// BOT HAVOLASINI yasovchi PORT (2026-08-28).
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN ALOHIDA PORT, <see cref="ITelegramInitDataValidator"/> GA
///   QO'SHILMADI: u imzo tekshirish porti va uning yagona kirish sharti —
///   BOT TOKENI. Bu yerdagi shart esa BOSHQA qiymat: bot foydalanuvchi
///   nomi (<c>telegram.bot_username</c>). Ikkalasini bitta interfeysga
///   qo'shsak, "sozlangan" degan tushuncha ikki xil ma'noni bildirib
///   qolardi va chaqiruvchi qaysi biri yetishmayotganini bilmasdi.
///
/// ★ NIMA UCHUN UMUMAN PORT: nom <c>Telegram:BotUsername</c> sozlamasidan,
///   ya'ni Infrastructure'dan keladi (va u ISH JARAYONIDA bazadan
///   o'zgaradi — <c>RuntimeTelegramOptions</c>). Application qatlami
///   `IRuntimeOptions&lt;TelegramOptions&gt;` ni ko'rmaydi.
///
/// 🔴 QIYMAT MAXFIY EMAS: bot nomi Telegram qidiruvida ham topiladi.
///   Shuning uchun havolani ANONIM endpoint qaytarishi mumkin — bu
///   `bot-link.ts` dagi (frontend) "build-time o'zgaruvchi" vaqtinchalik
///   yechimining o'rnini bosadi.
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public interface ITelegramBotLink
{
    /// <summary>
    /// Bot nomi sozlanganmi. <c>false</c> bo'lsa deep-link yasab bo'lmaydi,
    /// ya'ni "bot orqali kirish" oqimi UMUMAN boshlanmasligi kerak (503).
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// <c>https://t.me/&lt;bot&gt;?start=&lt;payload&gt;</c>.
    /// Nom sozlanmagan bo'lsa — <c>null</c>.
    /// </summary>
    /// <param name="payload">
    /// <c>/start</c> dan keyin botga yetib boradigan qism. Telegram uni
    /// 64 belgigacha va faqat <c>A-Z a-z 0-9 _ -</c> belgilari bilan
    /// qabul qiladi; chaqiruvchi shunga mos qiymat berishi SHART
    /// (bu yerda tekshirilmaydi — noto'g'ri payload dasturchi xatosi).
    /// </param>
    string? DeepLink(string payload);
}
