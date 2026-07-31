using Zinnur.Application.Notifications.Dtos;

namespace Zinnur.Application.Notifications.Services;

/// <summary>
/// Xabarni TASHQI kanalga uzatuvchi PORT.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ SHARTNOMA (FAZA 5.1 — Telegram implementatsiyasi UCHUN TAYYOR).
///
/// Keyingi bosqichda bu interfeysning Telegram varianti yoziladi. Shartnoma
/// AYNAN shunday qoladi — interfeysni o'zgartirish shart emas:
///
///  1) <see cref="Dtos.OutboxMessage.Body"/> — YUBORISHGA TAYYOR matn.
///     Sender uni QAYTA ISHLAMAYDI: escape qilmaydi, qirqmaydi, formatni
///     o'zgartirmaydi. Telegram HTML uchun ekranlash matn YASALAYOTGANDA
///     bajarilgan (sabab: <see cref="NotificationText"/>). Sender bu yerda
///     yana bir marta escape qilsa, shablonning o'z teglari
///     (<c>&lt;b&gt;</c>) foydalanuvchi ekranida so'zma-so'z ko'rinardi.
///
///  2) <see cref="Dtos.OutboxMessage.RecipientAddress"/> — kanal ichidagi
///     manzil (Telegram uchun <c>chat_id</c>). Bo'sh yoki yaroqsiz bo'lsa
///     sender <see cref="MessageSendResult.Permanent"/> qaytaradi: qayta
///     urinish holatni o'zgartirmaydi.
///
///  3) ISTISNO TASHLANMAYDI. Har qanday kanal xatosi
///     <see cref="MessageSendResult"/> ga aylantiriladi va vaqtinchalik
///     (<see cref="MessageSendResult.Retry"/>) yoki doimiy
///     (<see cref="MessageSendResult.Permanent"/>) deb belgilanadi.
///     Telegram'da bu xaritalash shunday bo'ladi:
///       429 / 5xx / tarmoq xatosi  -> Retry
///       400 (chat topilmadi, bot bloklangan), 403 -> Permanent
///
///  4) Sender NAVBAT HOLATIGA TEGMAYDI — holatni faqat
///     <see cref="IOutboxDispatcher"/> yozadi (yagona javobgarlik).
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public interface IMessageSender
{
    /// <summary>Bu yuboruvchi qaysi kanalga xizmat qiladi.</summary>
    NotificationChannel Channel { get; }

    /// <summary>Xabarni kanalga uzatadi. Istisno tashlamaydi (izohga qarang).</summary>
    Task<MessageSendResult> SendAsync(OutboxMessage message, CancellationToken ct = default);
}
