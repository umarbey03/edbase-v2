using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Telegram.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Telegram.Services;

/// <summary>
/// <see cref="ITelegramUpdateHandler"/> ning amalga oshirilishi — bot mantig'i.
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★ ESKI TIZIMNING X-1 ZAIFLIGI VA UNING YECHIMI
///
/// Eski tizimda Telegram orqali kirishda telefon raqam SO'ROV TANASIDAN
/// olinardi (`/api/auth/telegram/link`, `phone: str = Body(...)`). Ya'ni
/// istalgan odam admin yoki ustozning raqamini yozib, uning akkauntini
/// egallab olardi.
///
/// Bu yerda telefon FAQAT bitta joydan keladi — Telegram'ning
/// `message.contact` hodisasidan, ya'ni foydalanuvchi «Raqamni ulashish»
/// tugmasini bosganda. Qo'lda kiritish yo'li UMUMAN YO'Q: bot matnli
/// xabarni telefon deb qabul QILMAYDI.
///
/// ★ VA BU HAM YETARLI EMAS: Telegram'da BOSHQA odamning kontaktini ilova
/// qilib yuborish mumkin. Shuning uchun `contact.user_id` (kontakt EGASI)
/// `from.id` (xabar YUBORUVCHISI) bilan solishtiriladi. Bu tekshiruvsiz
/// himoya butunlay qulaydi: hujumchi jabrlanuvchining kontakt kartasini
/// yuborib, uning profilini o'ziga bog'lab olardi.
///
/// ★ ROL: bog'lash FAQAT `Student` uchun. Xodim raqami topilsa profil
/// bog'lanMAYDI va Telegram orqali kirish taklif qilinmaydi — xodimlar
/// email+parol bilan kiradi. Shu tufayli Telegram kanali orqali xodim
/// huquqini olishning ARXITEKTURA darajasida yo'li yo'q.
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class TelegramUpdateHandler(
    IApplicationDbContext db,
    ITelegramUpdateLog updateLog,
    INotificationOutbox outbox,
    ILogger<TelegramUpdateHandler> logger) : ITelegramUpdateHandler
{
    /// <summary>Faqat shaxsiy suhbat bilan ishlaymiz (guruhda bot hech nima bog'lamaydi).</summary>
    private const string PrivateChat = "private";

    /// <summary>Buyruq prefiksi.</summary>
    private const string StartCommand = "/start";

    /// <summary>
    /// <c>/start</c> dan keyingi qismning (deep-link payload) eng katta
    /// uzunligi — Telegram'ning o'z chegarasi 64 belgi.
    /// </summary>
    private const int MaxPayloadLength = 64;

    /// <inheritdoc />
    public async Task<TelegramUpdateOutcome> HandleAsync(
        TelegramUpdateDto update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.UpdateId <= 0)
            return TelegramUpdateOutcome.Ignored;

        // ── IDEMPOTENTLIK: bu yangilanish avval ko'rilganmi ──────────────
        if (!await updateLog.TryBeginAsync(update.UpdateId, ct).ConfigureAwait(false))
        {
            TelegramBotLog.DuplicateUpdate(logger, update.UpdateId);
            return TelegramUpdateOutcome.Duplicate;
        }

        // ★ `edited_message` ATAYLAB ISHLATILMAYDI: tahrirlangan xabar
        //   yangi hodisa emas. Kontaktni tahrirlab qayta yuborish orqali
        //   bog'lash oqimini ikkinchi marta ishga tushirish yo'lini
        //   ochib bermaymiz.
        var message = update.Message;

        var outcome = message is null
            ? TelegramUpdateOutcome.Ignored
            : await HandleMessageAsync(update.UpdateId, message, ct).ConfigureAwait(false);

        try
        {
            // ★ BITTA TRANZAKSIYA: "yangilanish ishlangan" belgisi, profil
            //   bog'lanishi va javob xabari birga saqlanadi. Yarim holat
            //   (bog'landi, lekin belgilanmadi) IMKONSIZ.
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Ikki nusxa AYNI paytda kelgan bo'lsa unikal kalit (update_id
            // yoki Users.TelegramId) ikkinchisini rad etadi. Bu KUTILGAN
            // poyga natijasi: hech narsa saqlanmadi, Telegram'ga baribir
            // 200 qaytadi va foydalanuvchi kerak bo'lsa qayta urinadi.
            TelegramBotLog.SaveConflict(logger, ex, update.UpdateId);
            return TelegramUpdateOutcome.Failed;
        }

        TelegramBotLog.UpdateHandled(logger, update.UpdateId, outcome.ToString());

        return outcome;
    }

    private async Task<TelegramUpdateOutcome> HandleMessageAsync(
        long updateId, TelegramMessageDto message, CancellationToken ct)
    {
        var chat = message.Chat;
        var sender = message.From;

        if (chat is null || sender is null || sender.Id <= 0)
            return TelegramUpdateOutcome.Ignored;

        // ★ FAQAT SHAXSIY SUHBAT. Guruhga qo'shilgan bot orqali bog'lash
        //   yo'li ochiq qolsa, guruhdagi istalgan a'zo boshqasining
        //   kontaktini tashlab yuborishi mumkin bo'lardi.
        if (!string.Equals(chat.Type, PrivateChat, StringComparison.Ordinal))
            return TelegramUpdateOutcome.Ignored;

        // Botlar bilan ishlamaymiz.
        if (sender.IsBot)
            return TelegramUpdateOutcome.Ignored;

        if (message.Contact is not null)
            return await HandleContactAsync(updateId, chat.Id, sender, message.Contact, ct).ConfigureAwait(false);

        var text = message.Text?.Trim();

        if (!string.IsNullOrEmpty(text) && IsStartCommand(text, out var payload))
            return await HandleStartAsync(updateId, chat.Id, sender, payload, ct).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(text))
        {
            await ReplyAsync(updateId, chat.Id, recipientUserId: null,
                TelegramTemplates.Help, TelegramTemplates.HelpText(), ct).ConfigureAwait(false);

            return TelegramUpdateOutcome.Helped;
        }

        // Rasm, stiker, ovoz — bizga tegishli emas, JIMGINA tashlanadi.
        return TelegramUpdateOutcome.Ignored;
    }

    // ---------------------------------------------------------------- /start

    private async Task<TelegramUpdateOutcome> HandleStartAsync(
        long updateId, long chatId, TelegramUserDto sender, string? payload, CancellationToken ct)
    {
        // ★ PAYLOAD HECH QACHON SHAXSNI ANIQLAMAYDI.
        //
        // `/start <payload>` — Telegram'ning deep-link mexanizmi va u
        // OCHIQ matn: havolani ko'rgan yoki uni ulashib yuborgan istalgan
        // odam AYNI payload bilan botga kira oladi. Agar payload profilni
        // aniqlasa, bu eski tizimning X-1 zaifligining aynan o'zi bo'lardi,
        // faqat boshqa niqobda. Shuning uchun u faqat LOGGA yoziladi
        // (kampaniya manbasini bilish uchun) va oqimga TA'SIR QILMAYDI:
        // shaxsni faqat `contact_shared` aniqlaydi.
        if (!string.IsNullOrEmpty(payload))
            TelegramBotLog.StartPayload(logger, updateId, Shorten(payload));

        // ★ `AsTracking()` — ATAYLAB (avval `AsNoTracking()` edi): quyida
        //   username yangilanadi. Telegram username'ni foydalanuvchi istalgan
        //   payt o'zgartiradi, shuning uchun u HAR muloqotda qayta yozib
        //   boriladi — aks holda xodim profilda eskirgan nomni ko'rib, BOSHQA
        //   odamga yozib qo'yishi mumkin (bo'shatilgan username Telegram'da
        //   qayta band qilinadi).
        var linked = await db.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.TelegramId == sender.Id, ct)
            .ConfigureAwait(false);

        // Natija ATAYLAB e'tiborsiz qoldiriladi: metod o'zi "haqiqatan
        // o'zgardimi" ni hisoblaydi va o'zgarmasa hech nima qilmaydi (har
        // `/start` bekorga `UPDATE` yozmasin). Saqlash `HandleAsync` dagi
        // YAGONA `SaveChangesAsync` da — bitta tranzaksiya qoidasi buzilmaydi.
        _ = linked?.RefreshTelegramUsername(sender.Username);

        if (linked is { Role: UserRole.Student, IsActive: true })
        {
            await ReplyAsync(updateId, chatId, linked.Id,
                TelegramTemplates.StartLinked,
                TelegramTemplates.StartLinkedText(linked.FullName), ct).ConfigureAwait(false);
        }
        else
        {
            await ReplyAsync(updateId, chatId, linked?.Id,
                TelegramTemplates.StartUnlinked,
                TelegramTemplates.StartUnlinkedText(), ct).ConfigureAwait(false);
        }

        return TelegramUpdateOutcome.Greeted;
    }

    // ---------------------------------------------------------------- kontakt

    private async Task<TelegramUpdateOutcome> HandleContactAsync(
        long updateId, long chatId, TelegramUserDto sender, TelegramContactDto contact, CancellationToken ct)
    {
        // ══════════════════════════════════════════════════════════════
        // ★★ ENG MUHIM TEKSHIRUV: KONTAKT EGASI = XABAR YUBORUVCHI ★★
        //
        // Telegram'da boshqa odamning kontakt kartasini yuborish MUMKIN.
        // `user_id` bo'lmasa — bu umuman Telegram foydalanuvchisi emas
        // (qo'lda kiritilgan telefon kitobi yozuvi), ya'ni raqam egaligi
        // TASDIQLANMAGAN. Ikkala holat ham RAD ETILADI.
        // ══════════════════════════════════════════════════════════════
        if (contact.UserId is null || contact.UserId.Value != sender.Id)
        {
            TelegramBotLog.ContactMismatch(logger, updateId, sender.Id, contact.UserId);

            await ReplyAsync(updateId, chatId, recipientUserId: null,
                TelegramTemplates.ContactMismatch,
                TelegramTemplates.ContactMismatchText(), ct).ConfigureAwait(false);

            return TelegramUpdateOutcome.ContactMismatch;
        }

        // ★ NORMALIZATSIYA MAVJUD QOIDA BILAN: `User.NormalizePhone` —
        //   `PhoneNormalized` ustunini to'ldiradigan AYNI metod. Ikkinchi
        //   nusxa yozilsa, ikkalasi asta bir-biridan uzoqlashib, "raqam
        //   bazada bor, lekin bot topmayapti" turkumidagi nosozlik
        //   berardi.
        var normalized = User.NormalizePhone(contact.PhoneNumber);

        if (normalized is null)
        {
            await ReplyAsync(updateId, chatId, recipientUserId: null,
                TelegramTemplates.ContactUnknown,
                TelegramTemplates.ContactUnknownText(), ct).ConfigureAwait(false);

            return TelegramUpdateOutcome.PhoneNotFound;
        }

        // Bu Telegram akkaunt allaqachon kimgadir bog'langanmi.
        //
        // ★ `AsTracking()` — ATAYLAB (avval `AsNoTracking()` edi): pastdagi
        //   idempotent shoxda (ayni profil qayta kontakt yubordi) username
        //   yangilanadi. Bu `candidate` bilan AYNI qatorga tushishi mumkin —
        //   EF identifikatorlar bo'yicha bitta obyekt qaytaradi, ya'ni ikki
        //   nusxa va qarama-qarshi o'zgarish holati yuzaga kelmaydi.
        var alreadyLinked = await db.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.TelegramId == sender.Id, ct)
            .ConfigureAwait(false);

        // Raqam bo'yicha profil — FILTRLI UNIKAL indeks tufayli ko'pi bilan bitta.
        var candidate = await db.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.PhoneNormalized == normalized, ct)
            .ConfigureAwait(false);

        if (alreadyLinked is not null)
        {
            // Ayni profil — bog'lanish o'zgarmaydi (idempotent), faqat
            // username yangilanib qo'yiladi.
            if (candidate is not null && candidate.Id == alreadyLinked.Id)
            {
                _ = alreadyLinked.RefreshTelegramUsername(sender.Username);

                await ReplyAsync(updateId, chatId, alreadyLinked.Id,
                    TelegramTemplates.ContactLinked,
                    TelegramTemplates.ContactLinkedText(alreadyLinked.FullName), ct).ConfigureAwait(false);

                return TelegramUpdateOutcome.AlreadyLinked;
            }

            // Bitta Telegram — bitta profil. Boshqa raqam bilan kelgan
            // bog'langan akkaunt RAD ETILADI (unikal indeks ham to'sardi,
            // lekin foydalanuvchi tushunarli xabar olishi kerak).
            TelegramBotLog.TelegramTaken(logger, updateId, sender.Id, alreadyLinked.Id);

            await ReplyAsync(updateId, chatId, alreadyLinked.Id,
                TelegramTemplates.ContactTelegramTaken,
                TelegramTemplates.ContactTelegramTakenText(), ct).ConfigureAwait(false);

            return TelegramUpdateOutcome.TelegramTaken;
        }

        if (candidate is null)
        {
            // ★ AKKAUNT YARATILMAYDI. O'quvchini faqat o'quv bo'limi
            //   qo'shadi — aks holda bot orqali istalgan odam o'ziga
            //   profil ochib olardi.
            TelegramBotLog.PhoneNotFound(logger, updateId, sender.Id);

            await ReplyAsync(updateId, chatId, recipientUserId: null,
                TelegramTemplates.ContactUnknown,
                TelegramTemplates.ContactUnknownText(), ct).ConfigureAwait(false);

            return TelegramUpdateOutcome.PhoneNotFound;
        }

        // ★ FAQAT O'QUVCHI. Xodim raqami topilsa bog'lash BAJARILMAYDI.
        if (candidate.Role != UserRole.Student)
        {
            TelegramBotLog.StaffPhone(logger, updateId, sender.Id, candidate.Role.ToString());

            await ReplyAsync(updateId, chatId, recipientUserId: null,
                TelegramTemplates.ContactStaff,
                TelegramTemplates.ContactStaffText(), ct).ConfigureAwait(false);

            return TelegramUpdateOutcome.StaffPhone;
        }

        if (!candidate.IsActive)
        {
            await ReplyAsync(updateId, chatId, candidate.Id,
                TelegramTemplates.ContactInactive,
                TelegramTemplates.ContactInactiveText(), ct).ConfigureAwait(false);

            return TelegramUpdateOutcome.Inactive;
        }

        // ══════════════════════════════════════════════════════════════
        // ★ QAYTA BOG'LASH — AVTOMATIK BAJARILMAYDI (qaror).
        //
        // Profilda BOSHQA Telegram ID tursa, biz uni JIMGINA almashtirib
        // qo'ymaymiz. Sabab: O'zbekistonda operator ishlatilmagan raqamni
        // qayta sotadi. Yangi egasi Telegram'ni o'sha raqamga ro'yxatdan
        // o'tkazsa, «Raqamni ulashish» tugmasi orqali BOSHQA o'quvchining
        // profiliga kirib olardi — Telegram raqam egaligini tasdiqlaydi,
        // lekin raqam KIMGA tegishli ekanini emas.
        //
        // Shuning uchun eski bog'lanishni faqat ODAM (o'quv bo'limi) bekor
        // qiladi. Bu qo'shimcha ish, lekin akkauntni egallab olish uchun
        // endi INSAYDER harakati kerak — ya'ni hujum "jimgina" bo'lolmaydi.
        // ══════════════════════════════════════════════════════════════
        if (candidate.TelegramId is not null)
        {
            TelegramBotLog.ProfileTaken(logger, updateId, candidate.Id);

            await ReplyAsync(updateId, chatId, candidate.Id,
                TelegramTemplates.ContactProfileTaken,
                TelegramTemplates.ContactProfileTakenText(), ct).ConfigureAwait(false);

            return TelegramUpdateOutcome.ProfileTaken;
        }

        // Bog'lanish — Domain metodi orqali: `TelegramId`, `TelegramUsername`
        // va `TelegramLinkedAt` uchligi BIRGA yoziladi. Qo'lda yozilsa
        // ulardan bittasi unutilib, "bog'langan, lekin sanasi yo'q" holati
        // paydo bo'lardi.
        candidate.LinkTelegram(sender.Id, sender.Username, DateTimeOffset.UtcNow);

        TelegramBotLog.Linked(logger, updateId, candidate.Id);

        await ReplyAsync(updateId, chatId, candidate.Id,
            TelegramTemplates.ContactLinked,
            TelegramTemplates.ContactLinkedText(candidate.FullName), ct).ConfigureAwait(false);

        return TelegramUpdateOutcome.Linked;
    }

    // ---------------------------------------------------------------- javob

    /// <summary>
    /// Javobni NAVBATGA yozadi (yubormaydi).
    ///
    /// ★ TAKRORLANISHGA QARSHI KALIT <c>update_id</c> DAN yasaladi: bitta
    /// yangilanishga ko'pi bilan bitta javob. Telegram yangilanishni qayta
    /// yuborsa ham (masalan biz javob berishdan oldin qulasak), ikkinchi
    /// javob navbatga TUSHMAYDI.
    /// </summary>
    private async Task ReplyAsync(
        long updateId,
        long chatId,
        long? recipientUserId,
        string templateKey,
        string body,
        CancellationToken ct)
    {
        // Natija ATAYLAB e'tiborsiz qoldiriladi: `false` — "bunday kalitli
        // xabar allaqachon navbatda", ya'ni AYNAN kutilgan himoya ishladi
        // (`INotificationOutbox` shartnomasi: buni xato deb qaramaslik kerak).
        _ = await outbox.EnqueueAsync(
            new NotificationRequest
            {
                Channel = NotificationChannel.Telegram,
                RecipientUserId = recipientUserId,
                RecipientAddress = chatId.ToString(CultureInfo.InvariantCulture),
                TemplateKey = templateKey,
                Body = body,
                IdempotencyKey = string.Create(CultureInfo.InvariantCulture, $"tg_update:{updateId}"),
            },
            ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- yordamchi

    /// <summary>
    /// Matn <c>/start</c> buyrug'imi. Telegram guruhda buyruqni
    /// <c>/start@bot_nomi</c> ko'rinishida yuboradi — shu ham qabul qilinadi.
    /// </summary>
    private static bool IsStartCommand(string text, out string? payload)
    {
        payload = null;

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        var command = space < 0 ? text : text[..space];

        var at = command.IndexOf('@', StringComparison.Ordinal);
        if (at > 0) command = command[..at];

        if (!string.Equals(command, StartCommand, StringComparison.OrdinalIgnoreCase))
            return false;

        if (space > 0)
        {
            var rest = text[(space + 1)..].Trim();

            if (rest.Length > 0)
                payload = rest.Length <= MaxPayloadLength ? rest : rest[..MaxPayloadLength];
        }

        return true;
    }

    private static string Shorten(string value) =>
        value.Length <= MaxPayloadLength ? value : value[..MaxPayloadLength];
}

/// <summary>
/// Manba-generatsiyali log metodlari (CA1848).
///
/// ★ LOGGA TELEFON RAQAM YOZILMAYDI — faqat Telegram ID va profil ID'si.
/// Log Sentry'ga va konteyner oqimiga tushadi; shaxsiy ma'lumot u yerda
/// kerak emas va uni keyin o'chirib bo'lmaydi.
/// </summary>
internal static partial class TelegramBotLog
{
    [LoggerMessage(
        EventId = 6200,
        Level = LogLevel.Debug,
        Message = "Telegram yangilanishi ishlandi: update={UpdateId} natija={Outcome}")]
    internal static partial void UpdateHandled(ILogger logger, long updateId, string outcome);

    [LoggerMessage(
        EventId = 6201,
        Level = LogLevel.Information,
        Message = "Telegram yangilanishi TAKROR keldi, tashlab yuborildi: update={UpdateId}")]
    internal static partial void DuplicateUpdate(ILogger logger, long updateId);

    [LoggerMessage(
        EventId = 6202,
        Level = LogLevel.Warning,
        Message = "XAVFSIZLIK: begona kontakt yuborildi. update={UpdateId} "
                  + "yuboruvchi={SenderId} kontakt_egasi={ContactUserId}")]
    internal static partial void ContactMismatch(
        ILogger logger, long updateId, long senderId, long? contactUserId);

    [LoggerMessage(
        EventId = 6203,
        Level = LogLevel.Information,
        Message = "Telegram profilga bog'landi: update={UpdateId} foydalanuvchi={UserId}")]
    internal static partial void Linked(ILogger logger, long updateId, long userId);

    [LoggerMessage(
        EventId = 6204,
        Level = LogLevel.Information,
        Message = "Raqam ro'yxatda topilmadi: update={UpdateId} telegram={SenderId}")]
    internal static partial void PhoneNotFound(ILogger logger, long updateId, long senderId);

    [LoggerMessage(
        EventId = 6205,
        Level = LogLevel.Warning,
        Message = "Xodim raqami Telegram orqali bog'lanmoqchi bo'ldi (rad etildi): "
                  + "update={UpdateId} telegram={SenderId} rol={Role}")]
    internal static partial void StaffPhone(ILogger logger, long updateId, long senderId, string role);

    [LoggerMessage(
        EventId = 6206,
        Level = LogLevel.Warning,
        Message = "Profil boshqa Telegram akkauntga bog'langan: update={UpdateId} foydalanuvchi={UserId}")]
    internal static partial void ProfileTaken(ILogger logger, long updateId, long userId);

    [LoggerMessage(
        EventId = 6207,
        Level = LogLevel.Warning,
        Message = "Telegram akkaunt boshqa profilga bog'langan: update={UpdateId} "
                  + "telegram={SenderId} foydalanuvchi={UserId}")]
    internal static partial void TelegramTaken(ILogger logger, long updateId, long senderId, long userId);

    [LoggerMessage(
        EventId = 6208,
        Level = LogLevel.Error,
        Message = "Telegram yangilanishini saqlashda to'qnashuv: update={UpdateId}")]
    internal static partial void SaveConflict(ILogger logger, Exception exception, long updateId);

    [LoggerMessage(
        EventId = 6209,
        Level = LogLevel.Debug,
        Message = "/start payload (shaxsni ANIQLAMAYDI): update={UpdateId} payload={Payload}")]
    internal static partial void StartPayload(ILogger logger, long updateId, string payload);
}
