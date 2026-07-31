using Zinnur.Domain.Enums;

namespace Zinnur.Application.GroupChat.Dtos;

/// <summary>
/// Guruh chatidagi bitta xabar.
///
/// ★ <c>Mine</c> MAYDONI ATAYLAB YO'Q (<see cref="Messaging.Dtos.DirectMessageDto"/>
/// dan farqi shu). Sabab: AYNI shu obyekt SignalR orqali guruhdagi
/// HAMMAGA bir nusxada yuboriladi. "Mine" ko'ruvchiga bog'liq qiymat, ya'ni
/// 30 kishilik guruhda u 29 tasi uchun NOTO'G'RI bo'lardi. Klient buni
/// <c>SenderId</c> ni o'z Id'si bilan solishtirib o'zi hisoblaydi —
/// bu ma'lumot unda allaqachon bor.
/// </summary>
/// <param name="SenderRole">
/// Yuboruvchining YOZGAN PAYTDAGI roli (yorliq uchun): <c>Student</c>,
/// <c>Teacher</c>, <c>Assistant</c>, <c>Academic</c>, <c>Admin</c>.
/// </param>
public sealed record GroupChatMessageDto(
    long Id,
    long GroupId,
    GroupChatChannel Channel,
    long SenderId,
    string SenderName,
    UserRole SenderRole,
    string Body,
    DateTimeOffset SentAt);

/// <summary>
/// Xabarlar sahifasi — KEYSET (kursorli) sahifalash.
///
/// ★ NIMA UCHUN <c>page/pageSize</c> EMAS: chat oqimi o'sib turadi.
/// Ofsetli sahifalashda "2-sahifa"ni so'raguningizcha ikkita yangi xabar
/// kelsa, butun oyna suriladi va allaqachon ko'rgan xabarlaringiz qayta
/// chiqadi (yoki bittasi butunlay tushib qoladi). Kursor (<c>Id</c> dan
/// kichik) bunday siljishga umuman bog'liq emas.
/// </summary>
/// <param name="Channel">Sahifa QAYSI oqimdan olingani — so'rovda kanal berilmasa server o'zi tanlaydi.</param>
/// <param name="AvailableChannels">Foydalanuvchi shu guruhda KO'RA oladigan oqimlar (UI tab'lari uchun).</param>
/// <param name="Items">Xabarlar ESKIDAN YANGIGA tartibda (ekranga shundayligicha chiziladi).</param>
/// <param name="HasMore">Yuqorida (eskiroq) yana xabar bormi.</param>
/// <param name="NextBeforeId">Keyingi sahifa uchun <c>?beforeId=</c>. <c>HasMore=false</c> bo'lsa <c>null</c>.</param>
/// <param name="UnreadCount">Shu oqimda MEN uchun o'qilmaganlar soni.</param>
public sealed record GroupChatPageDto(
    long GroupId,
    string GroupName,
    GroupChatChannel Channel,
    IReadOnlyList<GroupChatChannel> AvailableChannels,
    IReadOnlyList<GroupChatMessageDto> Items,
    bool HasMore,
    long? NextBeforeId,
    int UnreadCount);

/// <summary>
/// Foydalanuvchining bitta guruh chatidagi huquqi.
///
/// ★ SignalR hub'i AYNAN shuni ishlatadi: obuna bo'lishdan oldin
/// "shu odam shu oqimni ko'ra oladimi" savoliga javob shu yerdan keladi.
/// Hub o'z tekshiruvini YOZMAYDI — aks holda REST va realtime yo'llari
/// vaqt o'tib bir-biridan ajralib ketardi va bittasida kanal izolyatsiyasi
/// buzilardi.
/// </summary>
/// <param name="Channel">Aniqlangan (yoki so'ralgan va ruxsat etilgan) oqim.</param>
/// <param name="AvailableChannels">Shu guruhda umuman ko'ra oladigan oqimlar.</param>
public sealed record GroupChatAccessDto(
    long GroupId,
    string GroupName,
    GroupChatChannel Channel,
    IReadOnlyList<GroupChatChannel> AvailableChannels);

/// <summary>
/// "Chatlar" hubidagi bitta qator — bitta <c>(guruh, kanal)</c> oqimi.
///
/// ★ O'QUVCHIDA BITTA GURUH IKKI QATOR beradi (Ustoz va Kurator oqimlari):
/// bular ikki xil suhbatdosh va ikki xil o'qilmaganlar sanog'i. Ularni
/// bitta qatorga qo'shish "12 ta o'qilmagan" degan sonni ochganda faqat
/// bittasini ko'rsatardi.
/// </summary>
/// <param name="LastMessagePreview">Oxirgi xabar matni (qisqartirilgan). Xabar yo'q bo'lsa <c>null</c>.</param>
/// <param name="UnreadCount">MEN uchun o'qilmaganlar soni (o'z xabarim hisoblanmaydi).</param>
public sealed record GroupChatThreadDto(
    long GroupId,
    string GroupName,
    GroupChatChannel Channel,
    long? LastMessageId,
    string? LastMessagePreview,
    string? LastMessageSenderName,
    DateTimeOffset? LastMessageAt,
    int UnreadCount);

/// <summary>Xabar yuborish so'rovi.</summary>
/// <param name="Channel">
/// Qaysi oqimga. <c>null</c> — server foydalanuvchi roliga qarab tanlaydi
/// (ustozga <c>Teacher</c>, kuratorga <c>Curator</c>, o'quvchiga <c>Teacher</c>).
/// </param>
/// <param name="Body">Matn. Bo'sh bo'lmasin; 2000 belgidan uzuni kesiladi.</param>
public sealed record SendGroupChatMessageRequest(GroupChatChannel? Channel, string? Body);

/// <summary>"O'qildi" belgilash so'rovi.</summary>
/// <param name="Channel">Qaysi oqim. <c>null</c> — standart oqim.</param>
/// <param name="UpToMessageId">
/// Shu Id gacha o'qilgan deb belgilanadi. <c>null</c> — oqimdagi ENG OXIRGI
/// xabargacha. Oqimning haqiqiy oxiridan katta qiymat berilsa oxiriga
/// QIRQILADI (kelajakdagi xabar o'qilgan bo'lib qolmasin).
/// </param>
public sealed record MarkGroupChatReadRequest(GroupChatChannel? Channel, long? UpToMessageId);

/// <summary>"O'qildi" belgilash natijasi.</summary>
/// <param name="LastReadMessageId">Yangi chegara.</param>
/// <param name="UnreadCount">Belgilashdan KEYINGI o'qilmaganlar soni.</param>
/// <param name="Changed">Chegara haqiqatan surildimi (idempotent: takrorda <c>false</c>).</param>
public sealed record GroupChatReadResultDto(
    long GroupId,
    GroupChatChannel Channel,
    long LastReadMessageId,
    int UnreadCount,
    bool Changed);
