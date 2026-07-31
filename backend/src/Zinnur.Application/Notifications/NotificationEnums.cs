namespace Zinnur.Application.Notifications;

/// <summary>
/// Xabar yetkaziladigan kanal.
/// </summary>
/// <remarks>
/// Tartib MUHIM: qiymat bazaga <c>int</c> sifatida yoziladi. Yangi kanal
/// FAQAT oxiriga qo'shiladi, mavjud raqamlar hech qachon o'zgartirilmaydi
/// (<see cref="Zinnur.Domain.Enums.UserRole"/> bilan bir xil qoida).
///
/// ★ NIMA UCHUN <c>Zinnur.Domain.Enums</c> DA EMAS: bu enum biznes qoidasi
/// emas — u TRANSPORTni bildiradi. Domain uchun "o'quvchiga xabar berilsin"
/// degan fakt muhim, uni Telegram orqali yuborishmi yoki SMS — bu yetkazib
/// berish qatlamining ishi.
/// </remarks>
public enum NotificationChannel
{
    /// <summary>
    /// Telegram bot. FAZA 5.1 da o'quvchilar uchun YAGONA kirish yo'li
    /// bo'ladi, shuning uchun birinchi va hozircha yagona kanal.
    /// </summary>
    Telegram = 0,
}

/// <summary>
/// Navbatdagi (outbox) xabar holati.
/// </summary>
/// <remarks>
/// ATAYLAB "Processing" (ishlanmoqda) holati YO'Q. Ikki worker bir qatorni
/// olmasligi <c>FOR UPDATE SKIP LOCKED</c> va "ko'rinmaslik muddati"
/// (<c>NextAttemptAt</c> kelajakka suriladi) bilan ta'minlanadi. Alohida
/// "Processing" holati bo'lganda worker qulasa qator MANGU shu holatda
/// osilib qolardi va uni qo'lda tuzatish kerak bo'lardi — eski tizimlarda
/// eng ko'p uchraydigan navbat kasalligi shu.
/// </remarks>
public enum OutboxStatus
{
    /// <summary>Yuborilishi kutilmoqda (yoki qayta urinish kutilmoqda).</summary>
    Pending = 0,

    /// <summary>Kanal xabarni qabul qildi.</summary>
    Sent = 1,

    /// <summary>Barcha urinishlar tugadi — bu xabar boshqa yuborilmaydi.</summary>
    Failed = 2,
}
