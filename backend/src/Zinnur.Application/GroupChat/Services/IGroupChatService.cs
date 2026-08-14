using Zinnur.Application.GroupChat.Dtos;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.GroupChat.Services;

/// <summary>
/// Guruh chatining use-case qatlami: ruxsat, tarix, yuborish, o'qilganlik.
///
/// ★ RUXSAT QOIDASI FAQAT SHU YERDA. Uni HTTP controller ham, SignalR
/// hub'i ham chaqiradi. Ikki transport ikki nusxa tekshiruvga ega bo'lsa,
/// biri yangilanganda ikkinchisi eskirib qolardi — va aynan kanal
/// izolyatsiyasi (ustoz kurator oqimini ko'rmasligi) jimgina buzilardi.
/// </summary>
public interface IGroupChatService
{
    /// <summary>
    /// "Chatlar" hubi: foydalanuvchining BARCHA guruh chatlari bitta
    /// ro'yxatda — guruh nomi, oxirgi xabar va o'qilmaganlar soni bilan.
    ///
    /// O'qilmagani borlar tepada, keyin oxirgi faollik bo'yicha.
    /// </summary>
    /// <param name="query">
    /// R38 filtri (guruh turi va kategoriyasi). <c>null</c> — filtrsiz.
    ///
    /// 🔴 FILTR SHU YERDA, mijozda EMAS: ro'yxat saralashdan keyin
    /// <c>MaxThreads</c> da kesiladi va mijozdagi filtr kesilgandan
    /// KEYINGI guruhlarni umuman ko'rmasdi (batafsil
    /// <see cref="GroupChatThreadQuery"/> izohida).
    /// </param>
    Task<IReadOnlyList<GroupChatThreadDto>> ListThreadsAsync(
        long userId, GroupChatThreadQuery? query = null, CancellationToken ct = default);

    /// <summary>
    /// Ruxsatni tekshiradi va oqimni aniqlaydi. Huquq bo'lmasa
    /// <see cref="Common.Exceptions.ForbiddenException"/>.
    ///
    /// SignalR hub'i obunadan OLDIN shuni chaqiradi.
    /// </summary>
    Task<GroupChatAccessDto> ResolveAccessAsync(
        long userId, long groupId, GroupChatChannel? channel, CancellationToken ct = default);

    /// <summary>
    /// Tarix — kursorli sahifalash, eskidan yangiga tartibda.
    /// ★ O'QISH HOLATNI O'ZGARTIRMAYDI: "o'qildi" uchun
    /// <see cref="MarkReadAsync"/> bor (kurator yozishmasidagi bilan bir xil
    /// kelishuv — ikki modul bir xil ishlasin).
    /// </summary>
    Task<GroupChatPageDto> GetMessagesAsync(
        long userId,
        long groupId,
        GroupChatChannel? channel,
        long? beforeId,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Xabar yuboradi: ruxsat -> tezlik chegarasi -> BAZAGA YOZISH ->
    /// real vaqtda tarqatish (commit-then-send).
    /// </summary>
    Task<GroupChatMessageDto> SendAsync(
        long userId,
        long groupId,
        SendGroupChatMessageRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// FAYL BIRIKTIRILGAN XABAR (R16b) — REST'GA XOS YO'L
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// 🔴 SIGNALR BU YO'LNI TAKRORLAY OLMAYDI va takrorlashga urinilmadi:
    /// hub metodi <c>SendMessage(long, GroupChatChannel?, string)</c> —
    /// tanasi SATR. Baytlarni base64 qilib satrga solish mumkin edi, lekin
    /// o'shanda 10 MB fayl 13 MB matnga aylanib, hub'ning xabar chegarasidan
    /// oshib ketardi va butun ULANISH uzilardi (SignalR shunday ishlaydi:
    /// chegaradan katta freym — ulanishning oxiri). Ya'ni rasm yuborishga
    /// urinish CHATNI o'ldirardi.
    ///
    /// Shu sababli klient uchun qoida: <b>biriktirma bor -> REST, yo'q ->
    /// hub</b>. Xabarning O'ZI baribir har ikkala yo'lda ham
    /// <c>IGroupChatNotifier</c> orqali tarqatiladi, ya'ni qarama-qarshi
    /// tomon farqni SEZMAYDI.
    ///
    /// ★ FAYLLAR VA XABAR — BITTA SO'ROV, BITTA TRANZAKSIYA. "Avval yukla,
    /// id ol, keyin shu id bilan yubor" degan ikki fazali muqobil RAD
    /// ETILDI: u bekor qilingan har yozishda ombordа pul turadigan YETIM
    /// obyekt qoldirardi (batafsil <c>GroupChatAttachment</c> izohida).
    /// </summary>
    /// <exception cref="Common.Exceptions.ValidationException">
    /// Fayl yo'q, bo'sh yoki turi qo'llanmaydi.
    /// </exception>
    /// <exception cref="Common.Exceptions.PayloadTooLargeException">Hajm chegaradan oshdi.</exception>
    /// <exception cref="Common.Exceptions.TooManyRequestsException">
    /// Yuklash budjeti tugadi (xabar budjetidan ALOHIDA — izohi
    /// amalga oshirishda).
    /// </exception>
    Task<GroupChatMessageDto> SendWithAttachmentsAsync(
        long userId,
        long groupId,
        SendGroupChatAttachmentRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Biriktirmani O'QISHGA ochadi (oqim, <c>Range</c> bilan).
    ///
    /// 🔴 RUXSAT — OQIMNI O'QISH BILAN AYNI: <c>AuthorizeAsync(userId,
    /// groupId, channel)</c>. Ya'ni chat rasmini butun <c>(guruh, kanal)</c>
    /// ko'radi.
    ///
    /// ⚠️ VAZIFA JAVOBINING QOIDASI BU YERGA KO'CHIRILMAYDI. U yerda fayl
    /// faqat EGASI va uning ustoziga ko'rinadi
    /// (<c>EnsureCanReadStudentWorkAsync</c>); chatda esa "egasi" degan
    /// tushuncha ruxsatga UMUMAN ta'sir qilmaydi — o'sha qoidani ko'chirsak,
    /// guruhdoshlar bir-birining rasmini ko'rmay qolardi va chat buzilardi.
    /// </summary>
    Task<Courses.Services.LessonAssetDownload> OpenAttachmentAsync(
        long attachmentId,
        string? rangeHeader,
        long userId,
        CancellationToken ct = default);

    /// <summary>Oqimni o'qilgan deb belgilaydi (idempotent, faqat oldinga).</summary>
    Task<GroupChatReadResultDto> MarkReadAsync(
        long userId,
        long groupId,
        MarkGroupChatReadRequest request,
        CancellationToken ct = default);
}
