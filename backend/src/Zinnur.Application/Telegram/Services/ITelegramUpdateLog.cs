namespace Zinnur.Application.Telegram.Services;

/// <summary>
/// Ishlangan Telegram yangilanishlarini eslab qoluvchi PORT (idempotentlik).
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN KERAK: Telegram 200 javobini KUTADI va uni bir necha soniyada
/// ololmasa AYNI yangilanishni QAYTA yuboradi. Himoyasiz holatda o'quvchi
/// bitta "raqamni ulashish" uchun ikki-uch xil javob olardi va navbatga
/// takroriy xabar tushardi.
///
/// ★ NIMA UCHUN <c>IApplicationDbContext</c> DA EMAS
/// (<c>MessageOutbox</c>/<c>AppSetting</c> bilan AYNI sabab): "qaysi
/// yangilanish ishlangan" — bu biznes tushunchasi emas, YETKAZIB BERISH
/// mexanizmi. Domain va use-case'lar bunday jadval borligini bilmasligi
/// kerak.
///
/// ★ COMMIT-THEN-SEND: metod <c>SaveChanges</c> ni CHAQIRMAYDI. Yozuv
/// JORIY <c>DbContext</c> kuzatuvchisiga qo'shiladi va bog'lash (User.
/// TelegramId) hamda javob xabari bilan BITTA tranzaksiyada saqlanadi.
/// Shu tufayli "yangilanish ishlangan deb belgilandi, lekin bog'lash
/// saqlanmadi" holati IMKONSIZ.
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public interface ITelegramUpdateLog
{
    /// <summary>
    /// Yangilanishni "ishlanmoqda" deb belgilaydi.
    /// </summary>
    /// <returns>
    /// <c>true</c> — birinchi marta ko'rilyapti, ishlash mumkin;
    /// <c>false</c> — allaqachon ishlangan, JIMGINA tashlab yuborilsin.
    /// </returns>
    Task<bool> TryBeginAsync(long updateId, CancellationToken ct = default);
}
