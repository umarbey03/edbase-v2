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
    Task<IReadOnlyList<GroupChatThreadDto>> ListThreadsAsync(
        long userId, CancellationToken ct = default);

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

    /// <summary>Oqimni o'qilgan deb belgilaydi (idempotent, faqat oldinga).</summary>
    Task<GroupChatReadResultDto> MarkReadAsync(
        long userId,
        long groupId,
        MarkGroupChatReadRequest request,
        CancellationToken ct = default);
}
