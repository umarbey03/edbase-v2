namespace Zinnur.Application.Telegram.Services;

/// <summary>
/// <c>answerCallbackQuery</c> — inline tugma bosilganda Telegram'ga
/// DARHOL (webhook ICHIDA) yuboriladigan yagona chaqiruv.
///
/// ══════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN OUTBOX'DAN CHETLAB O'TADI (bu qoidaning YAGONA istisnosi):
/// tugma bosilgandan keyin Telegram klientida "⏳" aylanib turadi va u
/// FAQAT shu chaqiruvdan keyin to'xtaydi — kechiktirib yuborilgan navbat
/// xabari buni yechmaydi (u BOSHQA, YANGI xabar, ekrandagi "⏳" holatiga
/// tegmaydi). Chaqiruv o'ta yengil (bitta kichik POST) va tanaga
/// bog'liq emas, shuning uchun "webhook ICHIDA tashqi chaqiruv yo'q"
/// qoidasini bu yerda buzish arzon va oqlanadi.
///
/// ★ XATO YUTILADI: chaqiruvchi natijani E'TIBORSIZ qoldiradi — muvaffaqiyatsiz
/// bo'lsa ham foydalanuvchi darsni ko'radi (asosiy javob xabari baribir
/// outbox orqali ketadi), faqat "⏳" belgisi biroz uzoqroq turishi mumkin.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public interface ITelegramCallbackAcknowledger
{
    /// <summary>
    /// Ma'lum bir <paramref name="callbackQueryId"/> ni tasdiqlaydi.
    /// </summary>
    /// <param name="callbackQueryId"><c>TelegramCallbackQueryDto.Id</c>.</param>
    /// <param name="toastText">Foydalanuvchiga qisqa "toast" sifatida ko'rsatiladigan matn (bo'lmasligi mumkin).</param>
    Task AcknowledgeAsync(string callbackQueryId, string? toastText, CancellationToken ct = default);
}
