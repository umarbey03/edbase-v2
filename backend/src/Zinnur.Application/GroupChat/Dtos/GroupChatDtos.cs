using Zinnur.Application.Courses.Services;
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
/// <param name="Body">
/// Matn. ⚠️ R16b DAN KEYIN BO'SH SATR BO'LISHI MUMKIN — izohsiz surat
/// (Telegram'dagi kabi). <c>null</c> HECH QACHON bo'lmaydi; sabab
/// <see cref="Zinnur.Domain.Common.MessageText.NormalizeOptional"/> izohida.
/// </param>
/// <param name="Attachments">
/// Biriktirilgan fayllar. Biriktirmasiz xabarda BO'SH RO'YXAT
/// (<c>null</c> emas) — klientning har xabarda null-tekshiruv yozishiga
/// hojat qolmasin.
/// </param>
public sealed record GroupChatMessageDto(
    long Id,
    long GroupId,
    GroupChatChannel Channel,
    long SenderId,
    string SenderName,
    UserRole SenderRole,
    string Body,
    DateTimeOffset SentAt,
    IReadOnlyList<GroupChatAttachmentDto> Attachments);

/// <summary>
/// Chat xabariga biriktirilgan bitta fayl (R16b).
///
/// 🔴 <c>ObjectKey</c> ATAYLAB YO'Q. Baytlar
/// <c>GET /api/v1/group-chat/attachments/{id}</c> orqali olinadi va o'sha
/// so'rov oqimni O'QISH ruxsatidan (AYNI <c>AuthorizeAsync</c>) qaytadan
/// o'tadi. Kalit javobga qo'yilsa, ombor tuzilishi oshkor bo'lardi va
/// "havolani bilgan ochadi" degan eski kamchilik (X-6) qaytib kelardi.
/// </summary>
/// <param name="FileName">
/// Ko'rsatiladigan nom (tozalangan). Rasm uchun odatda kerak emas, HUJJAT
/// uchun esa shart: foydalanuvchi "shartnoma.pdf" ni ko'rishi kerak.
/// </param>
/// <param name="DurationSec">Ovoz davomiyligi (bo'lsa) — pleyer uchun.</param>
public sealed record GroupChatAttachmentDto(
    long Id,
    AttachmentKind Kind,
    string ContentType,
    string? FileName,
    long SizeBytes,
    int? DurationSec);

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
/// <param name="GroupType">
/// Guruh turi (R38) — ro'yxatda YORLIQ sifatida ko'rsatiladi.
///
/// ⚠️ HECH QACHON <c>Curator</c> BO'LMAYDI: kurator TURIDAGI guruhning
/// alohida chati yo'q va u ro'yxatga umuman tushmaydi (qoida
/// <c>GroupChatService.AuthorizeAsync</c> va <c>AccessibleThreadsAsync</c> da,
/// to'rt joyda). Ya'ni amalda faqat <c>Group</c> yoki <c>Individual</c>.
/// KANAL (<paramref name="Channel"/>) BILAN ARALASHTIRILMASIN — u
/// suhbatdoshni bildiradi ("Ustoz chati" / "Kurator chati") va guruh turiga
/// umuman bog'liq emas.
/// </param>
/// <param name="CategoryId">O'quv yo'nalishi (R21b/R38). <c>null</c> — yorliqsiz guruh.</param>
/// <param name="CategoryName">Kategoriya nomi. <paramref name="CategoryId"/> <c>null</c> bo'lsa bu ham <c>null</c>.</param>
public sealed record GroupChatThreadDto(
    long GroupId,
    string GroupName,
    GroupChatChannel Channel,
    long? LastMessageId,
    string? LastMessagePreview,
    string? LastMessageSenderName,
    DateTimeOffset? LastMessageAt,
    int UnreadCount,
    GroupType GroupType,
    long? CategoryId,
    string? CategoryName);

/// <summary>
/// ============================================================================
/// "CHATLAR" RO'YXATI FILTRI (R38)
/// ============================================================================
///
/// Talab: *"chatlar qismga ham filter qo'shilishi kerak, guruh tur va
/// kategoriyalar bo'yicha"*.
///
/// 🔴 FILTR SERVERDA QO'LLANADI, MIJOZDA EMAS — VA BU MAJBURIY.
/// <c>GroupChatService.MaxThreads = 200</c> ro'yxatni SARALASHDAN KEYIN
/// kesadi. Mijozdagi filtr faqat SHU 200 qatorni ko'rardi, ya'ni 201-o'rindagi
/// guruh filtrga to'liq mos kelsa ham natijada UMUMAN chiqmasdi va
/// foydalanuvchi "bunday guruh yo'q" degan YOLG'ON javobni olardi. Bu — UX
/// nuqsoni emas, MA'LUMOT YO'QOLISHI.
/// </summary>
/// <param name="Type">
/// Guruh turi. <c>null</c> — filtrlanmaydi.
///
/// ⚠️ <c>Curator</c> QABUL QILINMAYDI (400): kurator turidagi guruhning
/// alohida chati yo'q, ya'ni bu qiymat DOIM bo'sh ro'yxat berardi. Jimgina
/// bo'sh natija o'rniga aniq xato — servisning umumiy falsafasi
/// (<c>GroupChatService</c> sinf izohi, 2-bo'lim).
/// </param>
/// <param name="CategoryId">
/// O'quv yo'nalishi. <c>null</c> — filtrlanmaydi. Mavjudligi
/// TEKSHIRILMAYDI: yo'q kategoriya bo'sh ro'yxat beradi (GET ro'yxat so'rovi
/// 404 bermasligi kerak — `GroupService.ListAsync` bilan AYNI qoida).
/// </param>
public sealed record GroupChatThreadQuery(
    GroupType? Type = null,
    long? CategoryId = null);

/// <summary>Xabar yuborish so'rovi.</summary>
/// <param name="Channel">
/// Qaysi oqimga. <c>null</c> — server foydalanuvchi roliga qarab tanlaydi
/// (ustozga <c>Teacher</c>, kuratorga <c>Curator</c>, o'quvchiga <c>Teacher</c>).
/// </param>
/// <param name="Body">Matn. Bo'sh bo'lmasin; 2000 belgidan uzuni kesiladi.</param>
public sealed record SendGroupChatMessageRequest(GroupChatChannel? Channel, string? Body);

/// <summary>
/// FAYL BIRIKTIRILGAN xabar yuborish (R16b) — <c>multipart/form-data</c>.
///
/// ★ NIMA UCHUN ALOHIDA SO'ROV TURI, <see cref="SendGroupChatMessageRequest"/>
/// KENGAYTIRILMADI: u JSON tanasi bilan keladi va SignalR hub'i ham AYNAN
/// shu turni ishlatadi. Fayl esa na JSON'ga, na SignalR chaqiruviga sig'adi
/// — ya'ni ikkalasini bitta turga qo'shish "ba'zi maydonlari faqat ba'zi
/// transportda ishlaydi" degan yashirin qoida yaratardi.
/// </summary>
/// <param name="Body">
/// IXTIYORIY izoh. Bo'sh bo'lishi MUMKIN — bu yerda kamida bitta fayl bor.
/// </param>
/// <param name="Files">Kamida 1 ta, ko'pi bilan <c>GroupChatAttachment.MaxPerMessage</c> ta.</param>
public sealed record SendGroupChatAttachmentRequest(
    GroupChatChannel? Channel,
    string? Body,
    IReadOnlyList<LessonAssetUpload> Files);

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
