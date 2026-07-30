using Zinnur.Application.Messaging.Dtos;

namespace Zinnur.Application.Messaging.Services;

/// <summary>
/// Kurator ↔ o'quvchi shaxsiy yozishmasi.
///
/// ★ RUXSAT QOIDASI HAR METODDA BIR XIL va bitta yordamchida
/// (<c>ResolvePairAsync</c>) jamlangan:
///
///   • O'QUVCHI faqat O'Z kuratori bilan yozisha oladi. Boshqa
///     <c>peerId</c> — 403.
///   • XODIM faqat O'ZIGA biriktirilgan o'quvchilar bilan
///     (<see cref="ICuratorDirectory.StudentIdsAsync"/>). Boshqasi — 403.
///   • Admin ham istisno EMAS: shaxsiy yozishma ikki kishilik va uni
///     "hamma narsani ko'radigan" rol ham o'qiy olmasligi kerak.
///     Nazorat kerak bo'lsa alohida, oshkora audit endpointi bilan
///     qo'shiladi — jimgina emas.
/// </summary>
public interface IDirectMessageService
{
    /// <summary>
    /// Suhbatlar ro'yxati. O'quvchida — 0 yoki 1 ta (kuratori);
    /// kuratorda — o'ziga biriktirilgan o'quvchilar.
    /// Hali yozishma boshlanmagan suhbat ham ro'yxatda bo'ladi
    /// (o'quvchi birinchi savolini yozishi uchun).
    /// </summary>
    Task<IReadOnlyList<ConversationDto>> ListConversationsAsync(
        long userId, CancellationToken ct = default);

    /// <summary>Yozishma tarixi (kursorli sahifalash).</summary>
    /// <param name="beforeId">Shu Id'dan ESKIROQ xabarlar. <c>null</c> — eng yangi sahifa.</param>
    /// <param name="take">1..100, standart 50.</param>
    Task<MessagePageDto> GetThreadAsync(
        long userId, long peerId, long? beforeId, int take, CancellationToken ct = default);

    /// <summary>Xabar yuboradi.</summary>
    Task<DirectMessageDto> SendAsync(
        long userId, long peerId, SendDirectMessageRequest request, CancellationToken ct = default);

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
