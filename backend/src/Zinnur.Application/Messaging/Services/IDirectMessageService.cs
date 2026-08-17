using Zinnur.Application.Courses.Services;
using Zinnur.Application.Messaging.Dtos;

namespace Zinnur.Application.Messaging.Services;

/// <summary>
/// Kurator ↔ o'quvchi shaxsiy yozishmasi.
///
/// ★ RUXSAT QOIDASI HAR METODDA BIR XIL va bitta yordamchida
/// (<c>ResolvePairAsync</c>) jamlangan:
///
///   • O'QUVCHI faqat O'ZIGA MAS'UL xodim(lar) bilan yozisha oladi
///     (<see cref="ICuratorDirectory.ResolveRespondersAsync"/>). Boshqa
///     <c>peerId</c> — 403.
///   • XODIM faqat O'ZIGA biriktirilgan o'quvchilar bilan
///     (<see cref="ICuratorDirectory.StudentIdsAsync"/>). Boshqasi — 403.
///   • Admin ham istisno EMAS: shaxsiy yozishma ikki kishilik va uni
///     "hamma narsani ko'radigan" rol ham o'qiy olmasligi kerak.
///     Nazorat kerak bo'lsa alohida, oshkora audit endpointi bilan
///     qo'shiladi — jimgina emas.
///
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 R40 — NIMA UCHUN «DARS SAVOLLARI» UCHUN YANGI ENTITY YARATILMADI
/// ════════════════════════════════════════════════════════════════════════
///
/// Ikki yo'l bor edi va ikkalasi ham qonuniy:
///
///   (i)  bitta o'quvchida BIR NECHTA xodim suhbatdoshi bo'lishiga ruxsat
///        berish (sxema buni allaqachon qo'llab-quvvatlaydi — faqat
///        <c>CuratorDirectory</c> bittaga cheklab turgan edi);
///   (ii) savolni O'Z entity'si bilan yo'naltirish
///        (<c>LessonQuestion</c> jadvali, o'zining <c>AssignedStaffId</c> si).
///
/// (i) TANLANDI. (ii) ning narxi juda katta bo'lardi: o'qilgan/o'qilmagan
/// holati, kursorli sahifalash, saqlash muddati
/// (<c>ChatRetentionJob</c>), bildirishnoma va butun UI IKKI NUSXADA
/// bo'lardi — ya'ni "ikki mustaqil mexanizm" xatosining aynan o'zi. Ustiga
/// <c>DirectMessage.ModuleLessonId</c> allaqachon mavjud, tekshiriladi,
/// DTO'da bor va IKKI EKRANDA chizilgan — u shunchaki hech qachon
/// to'ldirilmagan edi.
///
/// (i) NING NARXI, halol aytilgan:
///   • o'quvchida ikki suhbat bo'lishi mumkin — ro'yxat ikki qatorli;
///   • <c>ResolvePairAsync</c> "tenglik" dan "to'plamga tegishlilik" ga
///     o'tdi, ya'ni XAVFSIZLIK CHEGARASI o'zgardi (u yerda batafsil izoh
///     va integratsion testlar bor);
///   • biriktiruv o'zgarganda eski suhbat kirilmas bo'lib qoladi — bu
///     BUGUNGI xatti-harakat (kurator almashsa ham shunday) va ataylab
///     o'zgartirilmadi.
/// </summary>
public interface IDirectMessageService
{
    /// <summary>
    /// Suhbatlar ro'yxati. O'quvchida — 0, 1 yoki 2 ta (unga mas'ul
    /// xodimlar, mas'uliyat tartibida); xodimda — o'ziga biriktirilgan
    /// o'quvchilar.
    /// Hali yozishma boshlanmagan suhbat ham ro'yxatda bo'ladi
    /// (o'quvchi birinchi savolini yozishi uchun).
    /// </summary>
    Task<IReadOnlyList<ConversationDto>> ListConversationsAsync(
        long userId, CancellationToken ct = default);

    /// <summary>
    /// R40 — DARS savollari navbati (xodim uchun). Javobsizlar tepada,
    /// ular ichida eng uzoq kutgani birinchi.
    /// </summary>
    /// <param name="take">1..100, standart 50.</param>
    Task<IReadOnlyList<LessonQuestionDto>> ListLessonQuestionsAsync(
        long userId, int take, CancellationToken ct = default);

    /// <summary>Yozishma tarixi (kursorli sahifalash).</summary>
    /// <param name="beforeId">Shu Id'dan ESKIROQ xabarlar. <c>null</c> — eng yangi sahifa.</param>
    /// <param name="take">1..100, standart 50.</param>
    /// <param name="moduleLessonId">
    /// Berilsa — faqat SHU kurs darsidan yozilgan xabarlar (o'quvchi Dars
    /// Dashboard'idagi mini-chat uchun). <c>null</c> — butun yozishma
    /// (mavjud xatti-harakat, o'zgarishsiz).
    /// </param>
    Task<MessagePageDto> GetThreadAsync(
        long userId, long peerId, long? beforeId, int take, long? moduleLessonId = null,
        CancellationToken ct = default);

    /// <summary>Xabar yuboradi.</summary>
    Task<DirectMessageDto> SendAsync(
        long userId, long peerId, SendDirectMessageRequest request, CancellationToken ct = default);

    /// <summary>
    /// FAYL/RASM BILAN XABAR (2026-08-17) — `GroupChatService.SendWithAttachmentsAsync`
    /// bilan AYNI naqsh, sabab <see cref="Dtos.SendDirectMessageAttachmentRequest"/> izohida.
    /// </summary>
    Task<DirectMessageDto> SendWithAttachmentsAsync(
        long userId,
        long peerId,
        SendDirectMessageAttachmentRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Yozishma biriktirmasini OQIM bilan beradi (`Range` qo'llab-quvvatlanadi).
    /// Ruxsat — <see cref="GetThreadAsync"/> bilan AYNI (`ResolvePairAsync`).
    /// </summary>
    Task<LessonAssetDownload> OpenAttachmentAsync(
        long attachmentId, string? rangeHeader, long userId, CancellationToken ct = default);

    /// <summary>
    /// Suhbatdagi kiruvchi xabarlarni "o'qildi" deb belgilaydi (idempotent).
    ///
    /// ★ NIMA UCHUN ALOHIDA <c>POST</c>, GET ichida EMAS: eski tizimda
    /// tarixni O'QISH bazani O'ZGARTIRARDI. Bu ikki narsani buzadi —
    /// GET xavfsiz (safe) bo'lishi shart, va har prefetch/qayta yuklash
    /// o'qilmaganlar sanog'ini jimgina nolga tushirardi (o'quvchi
    /// bildirishnomani ko'rmay qolardi).
    /// </summary>
    Task<MarkReadResultDto> MarkReadAsync(
        long userId, long peerId, CancellationToken ct = default);
}
