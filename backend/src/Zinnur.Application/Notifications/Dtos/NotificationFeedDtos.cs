using Zinnur.Domain.Enums;

namespace Zinnur.Application.Notifications.Dtos;

/// <summary>
/// Qo'ng'iroqchadagi bitta qator.
///
/// ★ <c>Body</c> — SOF MATN. Navbat yozuvining (<c>OutboxMessage</c>)
/// tanasi Telegram HTML edi; bu yerdagi matn esa hech qanday belgilashsiz
/// va klientda <c>v-html</c> siz chiziladi. Ikkalasi bitta turdan
/// oziqlanmasligining ASOSIY sababi shu.
/// </summary>
/// <param name="Read">
/// O'qilganmi. Sanani EMAS, BAYROQNI uzatamiz: "qachon o'qigan" ichki
/// savol (shikoyat tekshirish uchun) va uni klientga berish hech qanday
/// ekranga kerak emas.
/// </param>
/// <param name="EntityId">
/// Bosilganda qayerga o'tish. Ma'nosi <paramref name="Kind"/> ga bog'liq:
/// <see cref="NotificationKind.SubmissionGraded"/> uchun — javob Id'si.
/// </param>
public sealed record NotificationDto(
    long Id,
    NotificationKind Kind,
    string Title,
    string Body,
    long? EntityId,
    bool Read,
    DateTimeOffset CreatedAt);

/// <summary>
/// Bildirishnomalar sahifasi — KEYSET (kursorli) sahifalash.
///
/// ★ SHAKL <see cref="Zinnur.Application.Messaging.Dtos.MessagePageDto"/>
/// DAN AYNAN KO'CHIRILGAN (<c>Items</c> + <c>HasMore</c> +
/// <c>NextBeforeId</c> + sanoq) va bu ataylab: klientda "sahifani davom
/// ettirish" mantig'i ikki xil bo'lsa, ulardan biri albatta chala qoladi.
///
/// ★ NIMA UCHUN <c>page/pageSize</c> EMAS: ro'yxat TEPASIDAN o'sadi.
/// Ofsetli sahifalashda ikkinchi sahifani so'raguningizcha yangi
/// bildirishnoma kelsa, butun oyna suriladi va allaqachon ko'rilgan qator
/// yana chiqadi (yoki bittasi butunlay tushib qoladi). Ustoz 50 ta ishni
/// ketma-ket baholayotganda bu nazariy holat emas.
/// </summary>
/// <param name="Items">YANGIDAN ESKIGA tartibda (ekranga shundayligicha chiziladi).</param>
/// <param name="HasMore">Pastda (eskiroq) yana qator bormi.</param>
/// <param name="NextBeforeId">
/// Keyingi sahifa uchun <c>?beforeId=</c> qiymati. <c>HasMore=false</c> bo'lsa <c>null</c>.
/// </param>
/// <param name="UnreadCount">
/// O'QILMAGANLARNING UMUMIY soni — SAHIFADAGI emas. Qo'ng'iroqchadagi
/// nishon shu raqamni ko'rsatadi, ya'ni u ochilgan sahifaga bog'liq
/// bo'lmasligi kerak.
/// </param>
public sealed record NotificationPageDto(
    IReadOnlyList<NotificationDto> Items,
    bool HasMore,
    long? NextBeforeId,
    int UnreadCount);

/// <summary>Faqat sanoq (qo'ng'iroqcha nishoni uchun eng arzon so'rov).</summary>
public sealed record NotificationUnreadDto(int UnreadCount);

/// <summary>
/// "O'qildi" belgilash natijasi.
/// </summary>
/// <param name="MarkedCount">
/// Nechta qator o'zgardi. IDEMPOTENT: takroriy so'rovda <c>0</c>
/// (<c>MarkReadResultDto</c> bilan bir xil kelishuv).
/// </param>
/// <param name="UnreadCount">Amaldan KEYINGI o'qilmaganlar soni.</param>
public sealed record NotificationReadResultDto(int MarkedCount, int UnreadCount);
