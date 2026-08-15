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
/// <see cref="IPhoneLoginService"/> amalga oshirilishi.
/// HTTP haqida HECH NARSA bilmaydi — faqat Application xatolarini ko'taradi.
/// </summary>
public sealed class PhoneLoginService(
    IApplicationDbContext db,
    IPhoneLoginCodeStore codes,
    INotificationOutbox outbox,
    ITelegramInitDataValidator telegram,
    IAuthService auth,
    TimeProvider clock,
    ILogger<PhoneLoginService> logger) : IPhoneLoginService
{
    /// <inheritdoc />
    public async Task<PhoneCodeResponse> RequestCodeAsync(
        PhoneCodeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ══════════════════════════════════════════════════════════════
        // 🔴 KODNI YETKAZADIGAN KANAL BORMI — ENG BIRINCHI TEKSHIRUV.
        //
        // Bot tokeni bo'sh bo'lsa kod hech kimga bormaydi. Bunda "kod
        // yuborildi" deb 200 qaytarish eng yomon variant bo'lardi:
        // foydalanuvchi kod kutib o'tirardi, sabab esa hech qayerda
        // ko'rinmasdi. 503 — bu bizning bug'imiz emas, sozlanmagan xizmat
        // (`ITelegramMiniAppAuth` bilan AYNI qaror).
        //
        // ★ Bu tekshiruv hisob sanashga yo'l ochmaydi: u raqamga UMUMAN
        //   bog'liq emas, javob hamma uchun bir xil.
        // ══════════════════════════════════════════════════════════════
        if (!telegram.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Telegram integratsiyasi sozlanmagan — kirish kodini yuborib bo'lmaydi. "
                + "O'quv bo'limi yoki administrator bilan bog'laning.");
        }

        // ★ NORMALIZATSIYA MAVJUD QOIDA BILAN: `User.NormalizePhone` —
        //   `PhoneNormalized` ustunini to'ldiradigan AYNI metod. Ikkinchi
        //   nusxa yozilsa ikkalasi asta bir-biridan uzoqlashib, "raqam
        //   bazada bor, lekin kod kelmayapti" turkumidagi nosozlik
        //   berardi. (Bot oqimidagi AYNI ogohlantirish:
        //   `TelegramUpdateHandler.HandleContactAsync`.)
        var normalized = User.NormalizePhone(request.Phone);

        if (normalized is null)
        {
            // Raqamsiz matn ("qwerty") — kvota kalitlarini ham bekorga
            // yaratmaymiz. Javob baribir AYNI: klient "kod yuborildi"
            // ekranini ko'radi. Bu oshkorlik emas — matnda raqam yo'qligi
            // klientning O'ZIGA ham ko'rinib turibdi.
            return Response;
        }

        // ══════════════════════════════════════════════════════════════
        // ★ KVOTA RAQAM BO'YICHA — VA U PROFIL QIDIRUVIDAN OLDIN.
        //
        // Tartib ATAYLAB shunday: mavjud bo'lmagan raqam ham AYNI
        // hisoblagichlarni oshiradi. Aks holda "kvota ishladi" fakti
        // raqamning bazada borligini bildirib qo'yardi.
        //
        // ★ NIMA UCHUN HTTP rate-limit YETARLI EMAS: u IP bo'yicha
        //   bo'linadi va reverse-proxy ortida HAMMA bitta bo'limga
        //   tushadi (`Program.cs` dagi ogohlantirish). Bu esa RAQAM
        //   bo'yicha — bitta odamning telefoniga xabar yog'dirib, uni
        //   botni bloklashga majbur qilish yo'lini yopadi.
        // ══════════════════════════════════════════════════════════════
        var quota = await codes.TryReserveAsync(normalized, ct).ConfigureAwait(false);

        if (!quota.Allowed)
        {
            throw new TooManyRequestsException(
                "Kod yaqinda yuborilgan. Biroz kutib, qaytadan urinib ko'ring.",
                (int)Math.Ceiling(quota.RetryAfter.TotalSeconds));
        }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneNormalized == normalized, ct)
            .ConfigureAwait(false);

        // ══════════════════════════════════════════════════════════════
        // 🔴 UCHALA RAD ETISH SHOXI HAM JIMGINA — VA BU ATAYLAB.
        //
        //   • raqam topilmadi;
        //   • profil faol emas;
        //   • Telegram bog'lanmagan (kod yuboradigan manzil yo'q).
        //
        // Har biri uchun boshqacha javob berish "bu raqam bazada bor,
        // lekin Telegram ulanmagan" degan qimmatli ma'lumotni tekinga
        // berardi — ya'ni hujumchi avval mavjud raqamlarni ajratib olib,
        // keyin faqat ularga e'tibor qaratardi.
        //
        // ★ SABAB LOGDA QOLADI: qo'llab-quvvatlash "nega kod kelmadi?"
        //   degan savolga javob bera olishi kerak. Log ichkarida, javob
        //   esa tashqarida — ikkalasi ham o'z vazifasini bajaradi.
        // ══════════════════════════════════════════════════════════════
        if (user is null)
        {
            PhoneLoginLog.UnknownPhone(logger);
            return Response;
        }

        if (!user.IsActive)
        {
            PhoneLoginLog.InactiveProfile(logger, user.Id);
            return Response;
        }

        if (user.TelegramId is not { } telegramId)
        {
            PhoneLoginLog.NotLinked(logger, user.Id);
            return Response;
        }

        var code = GenerateCode();
        var issuedAt = clock.GetUtcNow();

        await codes.SaveAsync(normalized, user.Id, code, ct).ConfigureAwait(false);

        // ══════════════════════════════════════════════════════════════
        // ★ TAKRORLANISHGA QARSHI KALITDA VAQT BOR — VA BU ISTISNO.
        //
        // `NotificationRequest.IdempotencyKey` kelishuvi "vaqt yoki
        // tasodifiy qism QO'SHILMASIN" deydi, chunki u REJALASHTIRILGAN
        // eslatmalar uchun yozilgan: ular har hisoblashda qayta yasaladi
        // va kalit o'zgarsa himoya ishlamay qolardi.
        //
        // Bu yerda holat TESKARI: har kod so'rovi MUSTAQIL hodisa va
        // ikkinchi kod BIRINCHISIDAN FARQ QILADI. Kalit barqaror bo'lsa
        // ikkinchi so'rovda xabar JIMGINA tashlanardi (`Enqueue` -> false)
        // va foydalanuvchi eskirgan, allaqachon almashtirilgan kodni
        // kutib o'tirardi — ya'ni "qayta yuborish" tugmasi ishlamasdi.
        //
        // Takrorga qarshi himoya bu oqimda BOSHQA joyda: `TryReserveAsync`
        // 60 sekundlik oynani ATOMAR yopadi, ya'ni ikkita parallel so'rov
        // ikkita xabar yasay olmaydi.
        // ══════════════════════════════════════════════════════════════
        _ = await outbox.EnqueueAsync(
            new NotificationRequest
            {
                Channel = NotificationChannel.Telegram,
                RecipientUserId = user.Id,
                RecipientAddress = telegramId.ToString(CultureInfo.InvariantCulture),
                TemplateKey = TelegramTemplates.LoginCode,
                Body = TelegramTemplates.LoginCodeText(code, PhoneLoginCodeStore.CodeTtl),
                IdempotencyKey = string.Create(
                    CultureInfo.InvariantCulture,
                    $"auth_code:{user.Id}:{issuedAt.ToUnixTimeMilliseconds()}"),
            },
            ct).ConfigureAwait(false);

        // Navbat yozuvi SHU YERDA saqlanadi: `INotificationOutbox`
        // `SaveChanges` ni ATAYLAB chaqirmaydi (commit-then-send), va bu
        // oqimda saqlaydigan boshqa biznes o'zgarishi yo'q.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        PhoneLoginLog.CodeIssued(logger, user.Id);

        return Response;
    }

    /// <inheritdoc />
    public async Task<AuthResponse> VerifyAsync(
        PhoneVerifyRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalized = User.NormalizePhone(request.Phone);

        // Raqamsiz matn — kodni tekshirishga arzimaydi, lekin javob
        // xato koddagi bilan AYNI bo'lishi kerak.
        if (normalized is null)
            throw InvalidCode();

        var check = await codes
            .ConsumeAsync(normalized, request.Code ?? string.Empty, ct)
            .ConfigureAwait(false);

        if (check == PhoneCodeCheck.TooManyAttempts)
        {
            // ★ 429, 401 EMAS: foydalanuvchi uchun bu "kod xato" emas,
            //   "bu kod endi o'lgan, yangisini so'rang" degani. 401
            //   bo'lsa u to'g'ri kodni ham qayta-qayta kiritib, sababini
            //   tushunmasdi.
            throw new TooManyRequestsException(
                "Juda ko'p noto'g'ri urinish. Yangi kod so'rang.",
                (int)PhoneLoginCodeStore.CodeTtl.TotalSeconds);
        }

        if (check != PhoneCodeCheck.Ok)
            throw InvalidCode();

        // Kod TASDIQLANDI. Endi profilni raqam bo'yicha topamiz.
        //
        // ★ NIMA UCHUN Redis'dagi `UserId` ga TAYANMAYMIZ: kod berilgandan
        //   keyingi 5 daqiqada profil o'chirilgan yoki raqam boshqa odamga
        //   berilgan bo'lishi mumkin. Yagona haqiqat manbai — baza.
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneNormalized == normalized, ct)
            .ConfigureAwait(false);

        // Kod to'g'ri edi, lekin profil yo'q — amalda faqat poyga holati
        // (kod berilgandan keyin o'chirilgan). 401: aytadigan boshqa
        // haqiqat yo'q.
        if (user is null)
            throw InvalidCode();

        // ══════════════════════════════════════════════════════════════
        // 🔴 TELEGRAM BOG'LANISHI QAYTA TEKSHIRILADI.
        //
        // Kod BERILGAN paytda bog'lanish bor edi. Shu 5 daqiqa ichida
        // o'quv bo'limi uni UZGAN bo'lishi mumkin — "bog'lanishni uzish"
        // amalining butun ma'nosi esa kirish huquqini olib qo'yish
        // (`User.UnlinkTelegram` izohi). Bu tekshiruvsiz uzilgan hisob
        // qo'lidagi kod bilan yana bir marta kirib olardi.
        // ══════════════════════════════════════════════════════════════
        if (user.TelegramId is null)
            throw InvalidCode();

        PhoneLoginLog.Verified(logger, user.Id);

        // ★ TOKEN SHU YERDA YASALMAYDI — yagona yo'ldan olinadi.
        return await auth.LoginWithPhoneAsync(user.Id, ct).ConfigureAwait(false);
    }

    // ================================================================ ichki

    /// <summary>
    /// Har so'rovga qaytadigan YAGONA javob (sabab: <see cref="IPhoneLoginService"/>).
    /// Qiymatlar konstanta, ya'ni javob hech qanday holatga bog'liq emas.
    /// </summary>
    private static PhoneCodeResponse Response { get; } =
        new((int)PhoneLoginCodeStore.CodeTtl.TotalSeconds,
            (int)PhoneLoginCodeStore.ResendCooldown.TotalSeconds);

    /// <summary>Kod uzunligi — 6 xona (SMS/messenjer uchun sanoat odati).</summary>
    public const int CodeLength = 6;

    /// <summary>
    /// Kriptografik jihatdan kuchli 6 xonali kod.
    ///
    /// ★ <c>Random</c> EMAS, <see cref="RandomNumberGenerator"/>: oddiy
    /// generator urug'i (seed) taxmin qilinadigan bo'lib, ketma-ket
    /// berilgan kodlardan keyingisini hisoblab chiqarish mumkin edi.
    ///
    /// ★ Boshida nol bo'lishi MUMKIN (<c>004521</c>) — shuning uchun
    /// qiymat satr sifatida, chapdan nol bilan to'ldirilib yasaladi.
    /// Aks holda kodlar makoni 10 barobar kichrayardi.
    /// </summary>
    /// ⚠️ `internal` (2026-08-15): telefon ALMASHTIRISH oqimida bot ham
    /// aynan shu generatordan kod olishi kerak
    /// (`TelegramUpdateHandler`). Ikkinchi generator yozilsa, uning
    /// kuchi (entropiya, uzunlik, boshidagi nol) birinchisidan asta
    /// ajralib ketardi — va bu farq hech qanday testda ko'rinmasdi.
    internal static string GenerateCode() =>
        RandomNumberGenerator
            .GetInt32(0, 1_000_000)
            .ToString("D" + CodeLength.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>
    /// Xato kod uchun YAGONA javob.
    ///
    /// 🔴 Matn "kod xato", "kod muddati o'tgan" va "bunday raqam yo'q"
    /// holatlarini AJRATMAYDI — ajratilsa hisob sanash yo'li kod
    /// tekshirish endpointi orqali qaytib kelardi.
    /// </summary>
    private static UnauthorizedException InvalidCode() =>
        new("Kod noto'g'ri yoki muddati o'tgan. Yangi kod so'rang.");
}

/// <summary>
/// Manba-generatsiyali loglar (CA1848).
///
/// 🔴 TELEFON RAQAMI VA KODNING O'ZI LOGGA YOZILMAYDI. Log Sentry'ga va
/// konteyner chiqishiga ketadi; kod esa u yerda ko'ringan zahoti butun
/// himoya ma'nosini yo'qotardi. Profil <c>Id</c> si yetarli — qo'llab-
/// quvvatlash undan foydalanuvchini topa oladi.
/// </summary>
internal static partial class PhoneLoginLog
{
    [LoggerMessage(
        EventId = 6300,
        Level = LogLevel.Information,
        Message = "Telefon bo'yicha kirish: raqam ro'yxatda topilmadi (kod yuborilmadi).")]
    internal static partial void UnknownPhone(ILogger logger);

    [LoggerMessage(
        EventId = 6301,
        Level = LogLevel.Information,
        Message = "Telefon bo'yicha kirish: profil faol emas. userId={UserId}")]
    internal static partial void InactiveProfile(ILogger logger, long userId);

    [LoggerMessage(
        EventId = 6302,
        Level = LogLevel.Warning,
        Message = "Telefon bo'yicha kirish: Telegram bog'lanmagan — kod yuboriladigan manzil yo'q. userId={UserId}")]
    internal static partial void NotLinked(ILogger logger, long userId);

    [LoggerMessage(
        EventId = 6303,
        Level = LogLevel.Information,
        Message = "Kirish kodi navbatga qo'yildi. userId={UserId}")]
    internal static partial void CodeIssued(ILogger logger, long userId);

    [LoggerMessage(
        EventId = 6304,
        Level = LogLevel.Information,
        Message = "Kirish kodi tasdiqlandi. userId={UserId}")]
    internal static partial void Verified(ILogger logger, long userId);
}
