using Zinnur.Application.Notifications.Dtos;

namespace Zinnur.Application.Notifications.Services;

/// <summary>
/// Xabarni navbatga qo'yish PORTI (use-case tomoni).
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ COMMIT-THEN-SEND — bu portning BUTUN MA'NOSI
///
/// Metod xabarni HECH KIMGA YUBORMAYDI va bazaga MUSTAQIL yozmaydi. U
/// yozuvni JORIY <c>DbContext</c> kuzatuvchisiga qo'shadi, xolos. Yozuv
/// biznes o'zgarishi bilan BITTA <c>SaveChangesAsync</c> — ya'ni bitta
/// tranzaksiya — bilan saqlanadi.
///
/// Natijada faqat ikki holat mumkin:
///   * biznes o'zgarishi ham, xabar ham bor;
///   * ikkalasi ham yo'q.
///
/// "Xabar ketdi, lekin o'zgarish saqlanmadi" holati IMKONSIZ.
///
/// ESKI TIZIMDAGI XATO: xabar avval yuborilib, keyin bazaga yozilardi.
/// Server qayta ishga tushsa yoki tranzaksiya orqaga qaytsa, o'quvchi
/// bekor qilingan dars haqida xabar olardi yoki bir eslatmani bir necha
/// marta olardi. Endi bunday bo'lishi mumkin emas: navbatdagi qatorni
/// worker faqat KOMMIT bo'lgandan keyin ko'radi.
///
/// ★ Yuborishning O'ZI HTTP so'rovi ichida BAJARILMAYDI — buni fon
/// worker'i qiladi. Sabab: Telegram sekin javob bersa foydalanuvchi
/// formani saqlashda kutib turardi va so'rov timeout bo'lardi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public interface INotificationOutbox
{
    /// <summary>
    /// Xabarni navbatga qo'shadi (kuzatuvchiga). <c>SaveChanges</c> ni
    /// CHAQIRMAYDI — uni chaqiruvchi use-case o'z tranzaksiyasida qiladi.
    /// </summary>
    /// <returns>
    /// <c>true</c> — qo'shildi; <c>false</c> — bunday
    /// <see cref="NotificationRequest.IdempotencyKey"/> allaqachon mavjud
    /// (takror yozuv yaratilmadi). Chaqiruvchi buni xato deb qaramasligi
    /// kerak: aynan shu himoya uchun kalit bor.
    /// </returns>
    Task<bool> EnqueueAsync(NotificationRequest request, CancellationToken ct = default);
}
