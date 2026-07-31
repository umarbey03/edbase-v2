using Zinnur.Application.Notifications;
using Zinnur.Domain.Common;

namespace Zinnur.Infrastructure.Persistence;

/// <summary>
/// Yuborilishi kerak bo'lgan xabar navbati (transactional outbox).
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN JADVAL KERAK (eski tizimning xatosi)
///
/// Eski loyihada xabar AVVAL yuborilib, keyin bazaga yozilardi. Server
/// qayta ishga tushsa yoki tranzaksiya orqaga qaytsa, xabar allaqachon
/// ketgan bo'lardi: o'quvchi bir eslatmani bir necha marta olardi, ba'zan
/// esa BEKOR QILINGAN dars haqida xabar kelardi.
///
/// Bu yerda tartib teskari: yozuv biznes o'zgarishi bilan BITTA
/// tranzaksiyada saqlanadi, yuborishni esa fon worker'i KOMMITDAN KEYIN
/// bajaradi. Tranzaksiya orqaga qaytsa xabar ham qolmaydi.
///
/// ★ NIMA UCHUN DOMAIN ENTITY EMAS (<c>AppSetting</c> bilan bir xil sabab):
/// bu biznes tushunchasi emas, YETKAZIB BERISH mexanizmi. Domain "xabar
/// navbatda turibdi" degan faktni bilmasligi kerak. Shu sababli sinf
/// Infrastructure ichida qoladi va <c>IApplicationDbContext</c> da
/// OCHILMAYDI — use-case'lar navbatga <c>INotificationOutbox</c> porti
/// orqali yozadi.
///
/// ★ KALIT/QIYMAT EMAS, TIPLI USTUNLAR: navbat bo'yicha eng ko'p
/// beriladigan savollar — "nima yuborilmayapti", "qaysi turdagi xabarlar
/// yiqilyapti", "shu o'quvchiga nima ketgan" — SQL bilan javob berilishi
/// kerak. JSON qopchada bu har safar qo'lda ajratish demakdir.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class MessageOutbox : BaseEntity
{
    /// <summary>Qaysi kanal orqali (hozircha Telegram).</summary>
    public NotificationChannel Channel { get; set; }

    /// <summary>Platforma foydalanuvchisi — hisobot va qidiruv uchun.</summary>
    public long? RecipientUserId { get; set; }

    /// <summary>
    /// Kanal ichidagi manzil (Telegram <c>chat_id</c>) — YOZUV PAYTIDAGI
    /// nusxa. Sabab <c>NotificationRequest.RecipientAddress</c> izohida.
    /// </summary>
    public string? RecipientAddress { get; set; }

    /// <summary>Xabar turi: <c>lesson_reminder</c> kabi qisqa kod (guruhlash uchun).</summary>
    public required string TemplateKey { get; set; }

    /// <summary>Yuborishga TAYYOR matn (Telegram HTML uchun ekranlangan).</summary>
    public required string Body { get; set; }

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>Necha marta MUVAFFAQIYATSIZ urinilgan (band qilish hisoblanmaydi).</summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Shu vaqtdan oldin xabar OLINMAYDI. Uch vazifani bajaradi:
    ///   1) rejalashtirilgan yuborish (eslatma vaqti);
    ///   2) qayta urinish oralig'i (eksponensial backoff);
    ///   3) band qilish muddati — olingan qator boshqa worker'ga
    ///      ko'rinmasligi uchun kelajakka suriladi.
    ///
    /// Uchalasi bitta ustunda: har biri uchun alohida ustun bo'lganda
    /// "qaysi biri hozir kuchda?" degan savol paydo bo'lardi va tanlash
    /// so'rovi ham indeksdan foydalana olmasdi.
    /// </summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>Oxirgi xato matni — "nega yuborilmayapti" savoliga javob.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>
    /// TAKRORLANISHGA QARSHI kalit; UNIKAL indeks bilan mahkamlangan.
    ///
    /// ★ NEGA MAJBURIY: eslatmalarni hisoblovchi fon vazifasi qayta ishga
    /// tushsa yoki ikki instance bir vaqtda hisoblasa, "45-dars 15 daqiqada
    /// boshlanadi" xabari ikki marta yozilib qolardi. Baza darajasidagi
    /// unikal indeks — yagona ishonchli to'siq: kod tomonidagi tekshiruv
    /// ikki jarayon orasidagi poygada ishlamaydi.
    /// </summary>
    public required string IdempotencyKey { get; set; }
}
