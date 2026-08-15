using Zinnur.Application.Notifications.Dtos;

namespace Zinnur.Application.Notifications.Services;

/// <summary>
/// Qo'ng'iroqcha ro'yxatining use-case'lari.
///
/// ★ <see cref="INotificationOutbox"/> BILAN ARALASHTIRILMAYDI: u
/// YUBORISH navbati (Telegram), bu esa ILOVA ICHIDAGI ro'yxat. Ikkalasi
/// bitta hodisadan oziqlanadi, lekin bir-biriga bog'liq emas — Telegram
/// yiqilsa qo'ng'iroqcha baribir ishlaydi va aksincha.
///
/// ★ RUXSAT SHU YERDA VA U ODDIY: har kim FAQAT o'zinikini ko'radi.
/// Controller'da rol atributi yo'q, chunki qoida rolga UMUMAN bog'liq
/// emas — <c>userId</c> so'rovdan emas, TOKENDAN keladi.
/// </summary>
public interface INotificationFeed
{
    /// <summary>
    /// Ro'yxat — kursorli sahifalash, YANGIDAN ESKIGA tartibda.
    ///
    /// ★ O'QISH HOLATNI O'ZGARTIRMAYDI: "o'qildi" uchun alohida metod bor
    /// (<see cref="MarkReadAsync"/>). Aks holda ro'yxatni fon rejimida
    /// yangilash o'qilmaganlar sanog'ini "yeb qo'yardi" —
    /// <c>IDirectMessageService.GetThreadAsync</c> dagi AYNI qoida.
    /// </summary>
    /// <param name="userId">Kimning ro'yxati (TOKENDAN, so'rovdan emas).</param>
    /// <param name="beforeId">Shu Id'dan ESKIROQ qatorlar (keyingi sahifa).</param>
    /// <param name="unreadOnly">Faqat o'qilmaganlar.</param>
    /// <param name="take">1..50, standart 20.</param>
    Task<NotificationPageDto> ListAsync(
        long userId,
        long? beforeId = null,
        bool unreadOnly = false,
        int take = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Faqat o'qilmaganlar soni.
    ///
    /// ★ ALOHIDA METOD (ro'yxatning bir qismi emas): qo'ng'iroqcha nishoni
    /// HAR sahifada ko'rinadi va u uchun 20 ta qator olib kelish keraksiz
    /// trafik bo'lardi. Bu so'rov indeksdan to'liq o'qiladi.
    /// </summary>
    Task<NotificationUnreadDto> UnreadCountAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// Bildirishnomalarni "o'qildi" deb belgilaydi (idempotent).
    /// </summary>
    /// <param name="ids">
    /// Belgilanadigan qatorlar. BO'SH yoki <c>null</c> bo'lsa — foydalanuvchining
    /// BARCHA o'qilmaganlari ("hammasini o'qildi qilish" tugmasi).
    ///
    /// ★ BEGONA Id JIMGINA E'TIBORSIZ QOLDIRILADI, 403 QAYTMAYDI: so'rov
    /// <c>UserId</c> bo'yicha filtrlanadi, ya'ni boshqa odamning qatoriga
    /// umuman yetib bormaydi. 403 qaytarish esa hujumchiga "bu Id mavjud"
    /// deb aytardi — <c>ListAsync</c> bilan bir xil himoya.
    /// </param>
    Task<NotificationReadResultDto> MarkReadAsync(
        long userId, IReadOnlyCollection<long>? ids, CancellationToken ct = default);

    /// <summary>
    /// Bildirishnomalarni BUTUNLAY o'chiradi (idempotent).
    ///
    /// ⚠️ TARIXIY IZOH: bu metod dastlab ATAYLAB yo'q edi — "o'qildi"
    /// yetarli deb hisoblangan. 2026-08-15 da loyiha egasi qo'ng'iroqchaga
    /// o'chirish tugmasi, belgilash rejimi va "belgilanganlarni o'chirish"
    /// talab qildi. Ya'ni bu yerdagi qaror O'ZGARDI, esdan chiqmadi.
    ///
    /// ★ QAYTARIB BO'LMAYDI (soft-delete YO'Q): jadvalda `DeletedAt`
    /// ustuni yo'q va uni faqat shu ekran uchun qo'shish har bir so'rovga
    /// filtr, indeksga ustun va "chiqmayotgan bildirishnoma" turkumidagi
    /// xatolarni olib kelardi. Shuning uchun klientda tasdiqlash oynasi
    /// MAJBURIY.
    /// </summary>
    /// <param name="ids">
    /// O'chiriladigan qatorlar — BO'SH BO'LMASLIGI SHART.
    ///
    /// 🔴 <c>MarkReadAsync</c> DAN FARQI SHUNDA: u yerda bo'sh ro'yxat
    /// "hammasini" degani, bu yerda esa bunday MA'NO BERILMAYDI. Sabab —
    /// xato narxi assimetrik: noto'g'ri "hammasini o'qildi" bir bosishda
    /// qaytariladigan bezovtalik, noto'g'ri "hammasini o'chir" esa
    /// ma'lumotning butunlay yo'qolishi. Klientdagi bitta bo'sh massiv
    /// (belgilanmagan holatda yuborilgan so'rov) butun ro'yxatni yo'q
    /// qilmasligi kerak.
    ///
    /// ★ BEGONA Id JIMGINA E'TIBORSIZ QOLDIRILADI, 403 QAYTMAYDI —
    /// <see cref="MarkReadAsync"/> dagi AYNI sabab (mavjudlikni oshkor
    /// qilmaslik).
    /// </param>
    Task<NotificationDeleteResultDto> DeleteAsync(
        long userId, IReadOnlyCollection<long> ids, CancellationToken ct = default);
}
