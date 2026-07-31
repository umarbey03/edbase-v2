using Zinnur.Application.GroupChat.Dtos;

namespace Zinnur.Application.GroupChat.Services;

/// <summary>
/// Yangi guruh chati xabarini oqim obunachilariga REAL VAQTDA yetkazadi.
///
/// NIMA UCHUN PORT (interfeys) KERAK — <c>ILiveSessionNotifier</c> dagi
/// bilan bir xil sabab: yetkazish SignalR orqali bo'ladi, SignalR esa
/// <c>Zinnur.WebApi</c> da. <c>Zinnur.Application</c> WebApi'ga
/// bog'lanmaydi (qatlam yo'nalishi faqat ichkariga), shuning uchun use-case
/// ABSTRAKSIYAGA murojaat qiladi, amalga oshirish esa WebApi tomonida.
///
/// NIMA UCHUN CONTROLLER'DA EMAS: xabar IKKI yo'l bilan yuboriladi —
/// REST (<c>POST .../messages</c>) va SignalR hub metodi. Broadcast
/// controller'da bo'lsa, hub orqali yozilgan xabar jimgina tarqatilmasdi
/// (yoki teskarisi) va "ba'zan yetadi, ba'zan yo'q" xatosi tug'ilardi.
/// Use-case ichida bo'lsa — xabar QANDAY yozilsa ham bir xil yetkaziladi.
///
/// ★ KELISHUV 1 — COMMIT-THEN-SEND: bu metod xabar bazaga YOZILGANDAN
/// KEYIN chaqiriladi. Teskarisi jimgina buziladi: saqlash yiqilsa
/// ekranlarda xabar chiqib turardi, bazada esa yo'q edi — va o'quvchi
/// javob berilgan deb o'ylab qolardi.
///
/// ★ KELISHUV 2 — HECH QACHON ISTISNO KO'TARMAYDI. Xabar bazada
/// saqlangan; SignalR yiqilgani uchun endpoint 500 qaytarsa foydalanuvchi
/// "yuborilmadi" deb o'ylab qayta bosardi va xabar IKKI marta yozilardi.
/// Xato faqat logga tushadi; sahifa yangilanganda xabar baribir ko'rinadi.
/// </summary>
public interface IGroupChatNotifier
{
    /// <summary>
    /// Yangi xabarni <c>(GroupId, Channel)</c> oqimiga obuna bo'lganlarga
    /// yuboradi. Klient <c>GroupChatMessage</c> hodisasini tinglaydi.
    /// </summary>
    Task MessageSentAsync(GroupChatMessageDto message, CancellationToken ct = default);
}
