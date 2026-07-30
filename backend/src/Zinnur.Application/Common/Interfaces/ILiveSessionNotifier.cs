namespace Zinnur.Application.Common.Interfaces;

/// <summary>
/// Jonli dars holatidagi o'zgarishni xonadagi ishtirokchilarga REAL VAQTDA
/// bildiradi.
///
/// NIMA UCHUN PORT (interfeys) KERAK:
/// xabarni yuborish SignalR orqali bo'ladi, SignalR esa `Zinnur.WebApi` da.
/// `Zinnur.Application` WebApi'ga bog'lanmaydi (qatlam yo'nalishi faqat
/// ichkariga) — shuning uchun use-case shu ABSTRAKSIYAGA murojaat qiladi,
/// amalga oshirish esa WebApi tomonida (`LiveSessionNotifier`).
///
/// NIMA UCHUN CONTROLLER'DA EMAS:
/// darsni yakunlash bitta yo'l bilan cheklanmaydi — rejada muddati o'tgan
/// darslarni avtomatik yakunlaydigan fon xizmati ham bor
/// (`docs/ROADMAP.md`, FAZA 5.5). Broadcast controller'da bo'lsa, o'sha yo'l
/// jimgina xabarsiz qolardi va "ba'zan ishlaydi, ba'zan yo'q" xatosi
/// tug'ilardi. Use-case ichida bo'lsa — dars QANDAY yakunlansa ham xabar ketadi.
///
/// ★ KELISHUV: amalga oshirish HECH QACHON istisno ko'tarmaydi.
/// Xabar yuborilmasa ham dars bazada yakunlangan bo'lib qoladi: aks holda
/// ustoz 500 xatosini ko'rib "yakunlanmadi" deb o'ylab qayta bosardi.
/// Xato faqat logga yoziladi.
/// </summary>
public interface ILiveSessionNotifier
{
    /// <summary>
    /// Dars yakunlanganini bildiradi (klient `SessionEnded` hodisasini tinglaydi
    /// va video/hub ulanishini yopadi).
    ///
    /// Chaqiriladigan joy: ma'lumot bazaga YOZILGANDAN KEYIN (commit-then-send).
    /// </summary>
    Task SessionEndedAsync(long sessionId, CancellationToken ct = default);
}
