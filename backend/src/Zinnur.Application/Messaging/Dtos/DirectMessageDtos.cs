using Zinnur.Application.Courses.Services;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Messaging.Dtos;

/// <summary>
/// Suhbat ro'yxatidagi bitta qator (Telegram uslubidagi ro'yxat).
/// </summary>
/// <param name="PeerId">Suhbatdoshning Id'si — thread endpointlariga SHU yuboriladi.</param>
/// <param name="PeerRole">Suhbatdosh roli (<c>Assistant</c>, <c>Student</c>, ...).</param>
/// <param name="GroupName">Suhbatdoshning guruhi. Kurator ro'yxatida ko'rsatiladi; o'quvchida <c>null</c>.</param>
/// <param name="LastMessagePreview">Oxirgi xabar matni (qisqartirilgan).</param>
/// <param name="LastMessageMine">Oxirgi xabarni O'ZIM yozdimmi. Xabar yo'q bo'lsa <c>null</c>.</param>
/// <param name="UnreadCount">MEN uchun o'qilmagan xabarlar soni.</param>
public sealed record ConversationDto(
    long PeerId,
    string PeerName,
    string PeerRole,
    string? GroupName,
    long? LastMessageId,
    string? LastMessagePreview,
    DateTimeOffset? LastMessageAt,
    bool? LastMessageMine,
    int UnreadCount);

/// <summary>Yozishmadagi bitta xabar.</summary>
/// <param name="Mine">Xabarni so'rov yuborgan foydalanuvchi yozgan.</param>
/// <param name="ModuleLessonId">Savol qaysi kurs darsidan yozilgan. <c>null</c> — umumiy.</param>
/// <param name="ModuleLessonName">O'sha darsning nomi (kontekst yorlig'i uchun).</param>
/// <param name="ReadByPeer">
/// Suhbatdosh MENING xabarimni o'qidimi ("ikki belgi"). O'zganing xabari
/// uchun ma'nosiz — u doim <c>true</c> (men uni ko'rib turibman).
/// </param>
/// <param name="Attachments">
/// Biriktirilgan fayllar (2026-08-17) — <c>GroupChatMessageDto.Attachments</c>
/// bilan AYNI naqsh. Biriktirmasiz xabarda BO'SH RO'YXAT (<c>null</c> emas).
/// </param>
public sealed record DirectMessageDto(
    long Id,
    long SenderId,
    string SenderName,
    bool Mine,
    string Body,
    long? ModuleLessonId,
    string? ModuleLessonName,
    DateTimeOffset SentAt,
    bool ReadByPeer,
    IReadOnlyList<DirectMessageAttachmentDto> Attachments);

/// <summary>
/// Shaxsiy yozishma xabariga biriktirilgan bitta fayl (2026-08-17) —
/// <c>GroupChatAttachmentDto</c> bilan AYNI naqsh (sabab o'sha turdagi
/// izohida: ombor kaliti javobga chiqmaydi, baytlar alohida so'rov bilan
/// olinadi).
/// </summary>
public sealed record DirectMessageAttachmentDto(
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
/// Ofsetli sahifalashda siz "2-sahifa"ni so'raguningizcha ikkita yangi
/// xabar kelsa, butun oyna surilib, allaqachon ko'rgan xabarlaringiz
/// qayta chiqadi (yoki aksincha — bittasi butunlay tushib qoladi).
/// Kursor (<c>Id</c> dan kichik) bunday siljishga umuman bog'liq emas.
/// </summary>
/// <param name="Items">Xabarlar ESKIDAN YANGIGA tartibda (ekranga shundayligicha chiziladi).</param>
/// <param name="HasMore">Yuqorida (eskiroq) yana xabar bormi.</param>
/// <param name="NextBeforeId">
/// Keyingi sahifa uchun <c>?beforeId=</c> qiymati. <c>HasMore=false</c> bo'lsa <c>null</c>.
/// </param>
/// <param name="UnreadCount">Shu suhbatda MEN uchun o'qilmaganlar soni.</param>
public sealed record MessagePageDto(
    long PeerId,
    string PeerName,
    IReadOnlyList<DirectMessageDto> Items,
    bool HasMore,
    long? NextBeforeId,
    int UnreadCount);

/// <summary>Xabar yuborish so'rovi.</summary>
/// <param name="Body">Matn. Bo'sh bo'lmasin; 2000 belgidan uzuni kesiladi.</param>
/// <param name="ModuleLessonId">Ixtiyoriy kontekst — savol qaysi dars sahifasidan yozilgan.</param>
public sealed record SendDirectMessageRequest(string? Body, long? ModuleLessonId);

/// <summary>
/// FAYL/RASM BILAN XABAR (2026-08-17) — `multipart/form-data`.
///
/// `SendGroupChatAttachmentRequest` bilan AYNI naqsh, KANAL YO'Q (shaxsiy
/// yozishma bitta oqim) — sabab batafsil <see cref="DirectMessageAttachmentDto"/>
/// izohida.
/// </summary>
/// <param name="Body">IXTIYORIY izoh. Bo'sh bo'lishi MUMKIN — kamida bitta fayl bor.</param>
/// <param name="ModuleLessonId">Ixtiyoriy kontekst — matnli xabardagi bilan bir xil ma'no.</param>
/// <param name="Files">Kamida 1 ta, ko'pi bilan <c>DirectMessageAttachment.MaxPerMessage</c> ta.</param>
public sealed record SendDirectMessageAttachmentRequest(
    string? Body,
    long? ModuleLessonId,
    IReadOnlyList<LessonAssetUpload> Files);

/// <summary>"O'qildi" belgilash natijasi.</summary>
/// <param name="MarkedCount">Nechta xabar o'qilgan deb belgilandi (idempotent: takrorda 0).</param>
public sealed record MarkReadResultDto(int MarkedCount, int UnreadCount);

/* ============================================================================
   R40 · DARS SAVOLLARI NAVBATI
   ============================================================================

   Loyiha egasi: *"savollar qismida darslarda video darslardan kelgan savollar
   bo'ladi, ularga javob berish mumkin bo'ladi, bunda ham ketma-ketlik bo'yicha
   bo'lsin"*.

   ★ BU YANGI XABAR TURI EMAS. Bu — mavjud yozishmaning FILTRLANGAN
     ko'rinishi: `DirectMessage.ModuleLessonId` to'ldirilgan xabarlar.
     Alohida jadval yaratilmadi — sabab `IDirectMessageService` izohida.
   ========================================================================= */

/// <summary>Navbatdagi bitta dars savoli (xodim ko'rinishi).</summary>
/// <param name="PeerId">
/// O'quvchi Id'si. Suhbatni ochish uchun MAVJUD endpointga shu yuboriladi
/// (<c>/conversations/{peerId}/messages</c>) — navbat alohida ekran emas,
/// yozishmaga KIRISH nuqtasi.
/// </param>
/// <param name="Answered">
/// Shu savoldan KEYIN xodim javob yozganmi. Navbat tartibi shunga tayanadi:
/// javobsizlar tepada.
/// </param>
public sealed record LessonQuestionDto(
    long MessageId,
    long PeerId,
    string PeerName,
    string? GroupName,
    long ModuleLessonId,
    string ModuleLessonName,
    string Body,
    DateTimeOffset SentAt,
    bool Answered,
    bool Read);
