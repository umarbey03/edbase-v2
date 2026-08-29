using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Telegram;
using Zinnur.Application.Telegram.Services;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.Auth.Services;

/// <summary>
/// <see cref="ITelegramLoginService"/> amalga oshirilishi.
/// HTTP haqida HECH NARSA bilmaydi — faqat Application xatolarini ko'taradi.
/// </summary>
public sealed class TelegramLoginService(
    IApplicationDbContext db,
    ITelegramLoginTicketStore tickets,
    ITelegramInitDataValidator telegram,
    ITelegramBotLink botLink,
    INotificationOutbox outbox,
    IAuthService auth,
    TimeProvider clock,
    ILogger<TelegramLoginService> logger) : ITelegramLoginService
{
    /// <inheritdoc />
    public async Task<TelegramLoginStartResponse> StartAsync(CancellationToken ct = default)
    {
        // ══════════════════════════════════════════════════════════════
        // 🔴 IKKALA SOZLAMA HAM TEKSHIRILADI — VA IKKALASI HAM SHART.
        //
        //   • bot TOKENI yo'q  -> kodni yuboradigan kanal yo'q;
        //   • bot NOMI yo'q    -> foydalanuvchini olib boradigan havola yo'q.
        //
        // Bittasi yetishmasa oqim BOSHLANMASLIGI kerak. Aks holda
        // foydalanuvchi "botni oching" ekranida, ishlamaydigan tugma
        // oldida qolib ketardi va sababini hech kim ko'rmasdi.
        // ══════════════════════════════════════════════════════════════
        if (!telegram.IsConfigured || !botLink.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Telegram integratsiyasi sozlanmagan — bot orqali kirib bo'lmaydi. "
                + "Telefon raqami bilan kiring yoki administrator bilan bog'laning.");
        }

        var token = NewToken();

        await tickets.CreateAsync(token, ct).ConfigureAwait(false);

        // `IsConfigured` yuqorida tekshirilgani uchun `null` bo'lishi mumkin emas.
        var link = botLink.DeepLink(token)!;

        TelegramLoginLog.TicketOpened(logger);

        return new TelegramLoginStartResponse(
            token,
            link,
            (int)TelegramLoginTicketStore.TicketTtl.TotalSeconds);
    }

    /// <inheritdoc />
    public async Task<TelegramLoginStatusResponse> StatusAsync(
        string? token, CancellationToken ct = default)
    {
        if (!IsTicketToken(token))
            return Missing;

        var ticket = await tickets.GetAsync(token!, ct).ConfigureAwait(false);

        if (ticket is null)
            return Missing;

        var left = ticket.CreatedAt + TelegramLoginTicketStore.TicketTtl - clock.GetUtcNow();

        // Muddati o'tgan, lekin Redis kaliti hali o'chmagan (soatlar
        // farqi, TTL yaxlitlanishi) — foydalanuvchi uchun bu AYNI "yo'q".
        if (left <= TimeSpan.Zero)
            return Missing;

        return new TelegramLoginStatusResponse(
            ticket.Status,
            HintFor(ticket.Status),
            (int)Math.Ceiling(left.TotalSeconds));
    }

    /// <inheritdoc />
    public async Task<AuthResponse> VerifyAsync(
        TelegramLoginVerifyRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsTicketToken(request.Token))
            throw InvalidCode();

        var (check, userId) = await tickets
            .ConsumeAsync(request.Token, request.Code ?? string.Empty, ct)
            .ConfigureAwait(false);

        if (check == PhoneCodeCheck.TooManyAttempts)
        {
            // ★ 429, 401 EMAS — sabab `PhoneLoginService.VerifyAsync` da:
            //   foydalanuvchi uchun bu "kod xato" emas, "bu chipta o'ldi".
            throw new TooManyRequestsException(
                "Juda ko'p noto'g'ri urinish. Botdan qaytadan kirishni boshlang.",
                (int)TelegramLoginTicketStore.TicketTtl.TotalSeconds);
        }

        if (check != PhoneCodeCheck.Ok || userId is null)
            throw InvalidCode();

        // ★ Redis'dagi `UserId` ga TAYANIB QOLMAYMIZ — profil shu 15
        //   daqiqada o'chirilgan yoki uzilgan bo'lishi mumkin. Yagona
        //   haqiqat manbai — baza (`PhoneLoginService` dagi AYNI qoida).
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId.Value, ct)
            .ConfigureAwait(false);

        if (user is null)
            throw InvalidCode();

        // ══════════════════════════════════════════════════════════════
        // 🔴 TELEGRAM BOG'LANISHI QAYTA TEKSHIRILADI.
        //
        // Kod berilgan paytda bog'lanish bor edi. Shu oraliqda o'quv
        // bo'limi uni UZGAN bo'lishi mumkin — "bog'lanishni uzish"
        // amalining butun ma'nosi esa kirish huquqini olib qo'yish.
        // ══════════════════════════════════════════════════════════════
        if (user.TelegramId is null)
            throw InvalidCode();

        TelegramLoginLog.Verified(logger, user.Id);

        // ★ TOKEN SHU YERDA YASALMAYDI — yagona yo'ldan olinadi.
        return await auth.LoginWithPhoneAsync(user.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TelegramLoginAttach> AttachAsync(
        string? payload, long telegramUserId, User? linked, long chatId, CancellationToken ct = default)
    {
        // Payload chiptaga o'xshamasa — bu oqim UMUMAN boshlanmagan.
        // Bot odatdagidek salomlashadi (eski xatti-harakat o'zgarmaydi).
        if (!IsTicketToken(payload))
            return TelegramLoginAttach.NotTicket;

        var ticket = await tickets.GetAsync(payload!, ct).ConfigureAwait(false);

        if (ticket is null)
        {
            TelegramLoginLog.TicketExpired(logger, telegramUserId);
            return TelegramLoginAttach.Expired;
        }

        // ══════════════════════════════════════════════════════════════
        // 🔴 SHAXSNI PAYLOAD EMAS, TELEGRAMNING O'ZI ANIQLAYDI.
        //
        // `TelegramUpdateHandler.HandleStartAsync` dagi eski qoida —
        // "payload HECH QACHON shaxsni aniqlamaydi" — KUCHIDA QOLADI va
        // bu yerda ham buzilmaydi: chipta faqat BRAUZER SESSIYASINI
        // nomlaydi, profil esa `sender.Id` bo'yicha topiladi (chaqiruvchi
        // uni `TelegramId` ustuni bo'yicha o'qigan).
        //
        // Ya'ni payloadni almashtirgan odam boshqa profilga emas, boshqa
        // BRAUZER OYNASIGA kod yubortiradi — kod esa baribir O'ZINING
        // Telegramiga keladi.
        // ══════════════════════════════════════════════════════════════
        if (linked is null)
        {
            // Raqam ulanishini kutamiz. Chipta o'lmaydi: foydalanuvchi
            // «📱 Raqamni ulashish» tugmasini bosgach kod AVTOMATIK ketadi
            // (`ContinueAfterLinkAsync`).
            await tickets.SetPendingAsync(telegramUserId, payload!, ct).ConfigureAwait(false);

            await tickets
                .SaveStatusAsync(payload!, TelegramLoginStatuses.ContactNeeded, null, ct)
                .ConfigureAwait(false);

            TelegramLoginLog.ContactNeeded(logger, telegramUserId);

            return TelegramLoginAttach.ContactNeeded;
        }

        if (!linked.IsActive)
        {
            await tickets
                .SaveStatusAsync(payload!, TelegramLoginStatuses.Inactive, linked.Id, ct)
                .ConfigureAwait(false);

            TelegramLoginLog.InactiveProfile(logger, linked.Id);

            return TelegramLoginAttach.Inactive;
        }

        await IssueCodeAsync(payload!, ticket.CreatedAt, linked, chatId, ct).ConfigureAwait(false);

        return TelegramLoginAttach.CodeSent;
    }

    /// <inheritdoc />
    public async Task<bool> ContinueAfterLinkAsync(
        User user, long chatId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Bog'lanmagan profil bilan bu yerga kelib bo'lmaydi, lekin
        // shartnoma buzilmasin: `TelegramId` yo'q bo'lsa kutuv kaliti ham
        // yo'q (u AYNAN shu qiymat bo'yicha yasalgan).
        if (user.TelegramId is not { } telegramUserId)
            return false;

        var token = await tickets.TakePendingAsync(telegramUserId, ct).ConfigureAwait(false);

        if (token is null)
            return false;

        var ticket = await tickets.GetAsync(token, ct).ConfigureAwait(false);

        // Odam raqamni 15 daqiqadan keyin ulagan — chipta o'lgan. Bu xato
        // emas: u botda "raqamingiz ulandi" xabarini oladi va saytdan
        // qaytadan boshlaydi.
        if (ticket is null)
            return false;

        // Faol emas — kod yuborilmaydi. (Amalda bu shoxga tushib
        // bo'lmaydi: `HandleContactAsync` faol bo'lmagan profilni
        // bog'lamaydi ham. Tekshiruv baribir turadi, chunki bu metodning
        // yagona chaqiruvchisi kelajakda o'zgarishi mumkin.)
        if (!user.IsActive)
            return false;

        await IssueCodeAsync(token, ticket.CreatedAt, user, chatId, ct).ConfigureAwait(false);

        return true;
    }

    // ================================================================ ichki

    /// <summary>
    /// Kodni yasaydi, chiptaga yozadi va xabarni NAVBATGA qo'yadi.
    /// </summary>
    /// <remarks>
    /// ★ <c>SaveChangesAsync</c> BU YERDA CHAQIRILMAYDI: yagona
    /// chaqiruvchi — webhook, u esa hamma o'zgarishni BITTA tranzaksiyada
    /// saqlaydi (<c>ITelegramUpdateHandler</c> izohidagi qoida).
    ///
    /// ⚠️ HALOL IZOH — REDIS TRANZAKSIYAGA KIRMAYDI. Chiptaga kod
    /// yozildi, lekin `SaveChanges` yiqildi degan holat MUMKIN: bunda kod
    /// foydalanuvchiga UMUMAN ketmaydi va u 15 daqiqa "kod kutilmoqda"
    /// ekranida qoladi. Buni to'liq yo'q qilish uchun taqsimlangan
    /// tranzaksiya kerak bo'lardi; narxi foydasidan katta, chunki
    /// foydalanuvchi uchun yechim bir bosishlik — «Boshidan boshlash».
    /// </remarks>
    private async Task IssueCodeAsync(
        string token, DateTimeOffset createdAt, User user, long chatId, CancellationToken ct)
    {
        // ★ KOD GENERATORI BITTA — `PhoneLoginService.GenerateCode`.
        //   Ikkinchi generator yozilsa uning kuchi (entropiya, uzunlik,
        //   boshidagi nol) birinchisidan asta ajralib ketardi va buni
        //   hech qanday test ko'rsatmasdi.
        var code = PhoneLoginService.GenerateCode();

        await tickets.SaveCodeAsync(token, user.Id, code, ct).ConfigureAwait(false);

        // Xabardagi muddat — CHIPTANING qolgan umri, qat'iy konstanta
        // emas: chipta 15 daqiqa yashaydi va kod undan uzoq yashay
        // olmaydi. "5 daqiqa" deb yozilsa, 12-daqiqada kelgan odam hali
        // yaroqli kodni ishlatmay tashlab ketardi.
        var left = createdAt + TelegramLoginTicketStore.TicketTtl - clock.GetUtcNow();

        if (left < TimeSpan.FromMinutes(1))
            left = TimeSpan.FromMinutes(1);

        var issuedAt = clock.GetUtcNow();

        // ★ TAKRORLANISHGA QARSHI KALITDA VAQT BOR — `PhoneLoginService`
        //   dagi AYNI istisno: har kod MUSTAQIL hodisa va ikkinchi kod
        //   birinchisidan farq qiladi. Barqaror kalit bo'lsa ikkinchi
        //   xabar JIMGINA tashlanardi va foydalanuvchi eskirgan kodni
        //   kutib o'tirardi.
        _ = await outbox.EnqueueAsync(
            new NotificationRequest
            {
                Channel = NotificationChannel.Telegram,
                RecipientUserId = user.Id,
                RecipientAddress = chatId.ToString(CultureInfo.InvariantCulture),
                TemplateKey = TelegramTemplates.LoginCode,
                Body = TelegramTemplates.LoginCodeText(code, left),
                IdempotencyKey = string.Create(
                    CultureInfo.InvariantCulture,
                    $"auth_tg_code:{user.Id}:{issuedAt.ToUnixTimeMilliseconds()}"),
            },
            ct).ConfigureAwait(false);

        TelegramLoginLog.CodeIssued(logger, user.Id);
    }

    /// <summary>
    /// Yangi chipta identifikatori — 16 bayt (128 bit) tasodif, hex.
    ///
    /// ★ NIMA UCHUN HEX, <c>Base64</c> EMAS: qiymat Telegram deep-link
    /// payloadiga tushadi, u yerda esa FAQAT <c>A-Z a-z 0-9 _ -</c>
    /// ruxsat etilgan. Base64 ning <c>+</c> va <c>/</c> belgilari havolani
    /// jimgina buzardi.
    ///
    /// ★ 32 BELGI — Telegramning 64 belgilik chegarasiga bemalol sig'adi.
    /// </summary>
    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>Chipta identifikatorining uzunligi (16 bayt hex).</summary>
    private const int TokenLength = 32;

    /// <summary>
    /// Qiymat chiptaga o'xshaydimi: AYNAN 32 ta kichik hex belgisi.
    ///
    /// ★ SHAKL TEKSHIRUVI KESHGA BORISHDAN OLDIN: botga har kuni
    /// kampaniya havolalari va tasodifiy matnlar bilan <c>/start</c>
    /// keladi, ularning har biri uchun Redis'ga so'rov yuborish bekor
    /// yuk bo'lardi.
    ///
    /// ★ REGEX EMAS, ODDIY SIKL: shakl o'zgarmas va juda sodda, regex
    /// esa bu yerga faqat manba-generator va qo'shimcha o'qish yukini
    /// olib kelardi.
    /// </summary>
    private static bool IsTicketToken(string? value)
    {
        if (value is null || value.Length != TokenLength)
            return false;

        foreach (var ch in value)
        {
            var isHex = char.IsAsciiDigit(ch) || (ch >= 'a' && ch <= 'f');

            if (!isHex)
                return false;
        }

        return true;
    }

    /// <summary>Noma'lum chipta uchun YAGONA javob.</summary>
    private static TelegramLoginStatusResponse Missing { get; } =
        new(TelegramLoginStatuses.Missing,
            "Havola eskirgan. Kirishni qaytadan boshlang.",
            0);

    /// <summary>
    /// Holat izohi — MATN SERVERDA (sabab
    /// <see cref="TelegramLoginStatusResponse.Hint"/> izohida).
    /// </summary>
    private static string HintFor(string status) => status switch
    {
        TelegramLoginStatuses.Waiting =>
            "Telegram botni oching va «Ishga tushirish» tugmasini bosing.",

        TelegramLoginStatuses.ContactNeeded =>
            "Botda «📱 Raqamni ulashish» tugmasini bosing — kod shundan keyin keladi.",

        TelegramLoginStatuses.CodeSent =>
            "Kod Telegramga yuborildi.",

        TelegramLoginStatuses.Inactive =>
            "Profilingiz faol emas. O'quv bo'limi bilan bog'laning.",

        _ => "Havola eskirgan. Kirishni qaytadan boshlang.",
    };

    /// <summary>
    /// Xato kod uchun YAGONA javob — "kod xato", "chipta yo'q" va "muddati
    /// o'tgan" holatlarini ATAYLAB ajratmaydi.
    /// </summary>
    private static UnauthorizedException InvalidCode() =>
        new("Kod noto'g'ri yoki muddati o'tgan. Kirishni qaytadan boshlang.");
}

/// <summary>
/// Manba-generatsiyali loglar (CA1848).
///
/// 🔴 CHIPTA TOKENI VA KODNING O'ZI LOGGA YOZILMAYDI: log Sentry'ga va
/// konteyner chiqishiga ketadi, u yerda ko'ringan token esa boshqa odamning
/// kirish oqimini o'g'irlash uchun yetarli bo'lardi.
/// </summary>
internal static partial class TelegramLoginLog
{
    [LoggerMessage(
        EventId = 6320,
        Level = LogLevel.Information,
        Message = "Bot orqali kirish: chipta ochildi.")]
    internal static partial void TicketOpened(ILogger logger);

    [LoggerMessage(
        EventId = 6321,
        Level = LogLevel.Information,
        Message = "Bot orqali kirish: chipta topilmadi yoki muddati o'tgan. telegram={TelegramUserId}")]
    internal static partial void TicketExpired(ILogger logger, long telegramUserId);

    [LoggerMessage(
        EventId = 6322,
        Level = LogLevel.Information,
        Message = "Bot orqali kirish: Telegram akkaunt bog'lanmagan, raqam kutilmoqda. telegram={TelegramUserId}")]
    internal static partial void ContactNeeded(ILogger logger, long telegramUserId);

    [LoggerMessage(
        EventId = 6323,
        Level = LogLevel.Information,
        Message = "Bot orqali kirish: profil faol emas. userId={UserId}")]
    internal static partial void InactiveProfile(ILogger logger, long userId);

    [LoggerMessage(
        EventId = 6324,
        Level = LogLevel.Information,
        Message = "Bot orqali kirish: kod navbatga qo'yildi. userId={UserId}")]
    internal static partial void CodeIssued(ILogger logger, long userId);

    [LoggerMessage(
        EventId = 6325,
        Level = LogLevel.Information,
        Message = "Bot orqali kirish: kod tasdiqlandi. userId={UserId}")]
    internal static partial void Verified(ILogger logger, long userId);
}
