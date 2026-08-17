namespace Zinnur.Domain.Enums;

/// <summary>
/// Ilova ICHIDAGI bildirishnoma turi (qo'ng'iroqcha ro'yxati).
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN <c>NotificationChannel</c> DAN ALOHIDA VA DOMAIN'DA
///
/// <c>Zinnur.Application.Notifications.NotificationChannel</c> — TRANSPORT
/// ("Telegram orqali"). Bu esa BIZNES HODISASI ("vazifa tekshirildi").
/// Ikkalasi bir enum bo'lganda "Telegram" va "vazifa tekshirildi" bitta
/// ro'yxatda turardi va yangi kanal qo'shilganda hodisalar raqami surilib
/// ketardi.
///
/// ★ Domain'da, chunki bu FAKT — u qaysi kanal orqali yetkazilishidan
/// qat'i nazar to'g'ri qoladi: qo'ng'iroqchada ham, Telegram'da ham AYNI
/// hodisa.
///
/// 🔴 TARTIB MUHIM: qiymat bazaga <c>int</c> sifatida yoziladi. Yangi tur
/// FAQAT oxiriga qo'shiladi, mavjud raqamlar HECH QACHON o'zgartirilmaydi
/// (<see cref="UserRole"/> bilan bir xil qoida). Raqam surilsa, bazadagi
/// eski qatorlar jimgina BOSHQA turga aylanardi.
/// </summary>
public enum NotificationKind
{
    /// <summary>
    /// Uy vazifasi tekshirildi (baho qo'yildi).
    /// R35/R36 ning YAGONA manbai — loyiha egasi: *"vazifa tekshirilgan
    /// avtomatik studentda ham yangilanish kerak … va notification kelsin"*.
    /// </summary>
    SubmissionGraded = 0,

    /// <summary>
    /// Ustoz bugungi darsga o'ta olmasligini bildirdi (2026-08-17,
    /// <c>TeacherDailyCheckin.SubmitDays</c> yakunida) — o'quv bo'limiga.
    /// </summary>
    TeacherDeclinedSession = 1,

    /// <summary>
    /// O'rinbosar ustoz topildi va darsni oldi (2026-08-17,
    /// <c>ISubstituteOfferService.RespondAsync</c>) — o'quv bo'limiga.
    /// </summary>
    SubstituteFound = 2,
}
