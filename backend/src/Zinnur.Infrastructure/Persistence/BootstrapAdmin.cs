using System.Globalization;
using Microsoft.Extensions.Configuration;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence;

/// <summary>
/// ========================================================================
/// BO'SH BAZAGA YOZILADIGAN BIRINCHI ADMINISTRATORNING KIRISH MA'LUMOTI
/// ========================================================================
///
/// 🔴 NIMA UCHUN BU SINF UMUMAN PAYDO BO'LDI (2026-08-13)
///
/// Email va parol bilan kirish olib tashlangach, kirishning yagona yo'li —
/// telefon raqami + o'sha raqamga bog'langan Telegram hisobiga keladigan
/// kod. Ilgari <c>DbInitializer</c> administratorni TELEFONSIZ va
/// TELEGRAM'SIZ yaratardi.
///
/// Ya'ni yangi o'rnatish quyidagi holatga tushardi:
///   • administrator bazada BOR;
///   • unga kod yuboradigan raqam YO'Q;
///   • raqamni kiritish uchun tizimga kirish kerak;
///   • tizimga kirish uchun raqam kerak.
///
/// Bu — o'zini o'zi qulflagan o'rnatish, va uni faqat <c>psql</c> bilan
/// ochish mumkin bo'lardi. Shuning uchun raqam MUHITDAN keladi va u
/// bo'lmasa seeding TO'XTAYDI (Development'dan tashqarida).
/// </summary>
/// <param name="AdminPhone">
/// Boshlang'ich administratorning telefoni. <c>User.SetPhone</c> ga
/// XOM holda beriladi — normalizatsiyani AYNI metod bajaradi.
/// </param>
/// <param name="AdminTelegramId">
/// Ixtiyoriy: oldindan ma'lum Telegram ID. <c>null</c> bo'lsa
/// administrator botga raqamini bir marta ulashadi.
/// </param>
/// <param name="IsDevelopment">
/// Development muhitida standart qiymatlarga ruxsat beriladi — dev
/// mashinasida <c>docker compose up</c> hech qanday qo'shimcha sozlamasiz
/// ishlashi kerak.
/// </param>
public sealed record BootstrapAdmin(
    string? AdminPhone,
    long? AdminTelegramId,
    bool IsDevelopment)
{
    /// <summary>Konfiguratsiyadan (muhit o'zgaruvchilari, appsettings) o'qiydi.</summary>
    public static BootstrapAdmin Read(IConfiguration configuration, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var phone = configuration[DbInitializer.AdminPhoneKey];

        // Development'da standart qiymat — sabab yuqorida. Prod'da esa
        // ATAYLAB standart YO'Q: "hammaga ma'lum raqam" administrator
        // hisobini istalgan odamga ochib qo'yardi.
        if (string.IsNullOrWhiteSpace(phone) && isDevelopment)
            phone = DbInitializer.DevAdminPhone;

        var rawTelegramId = configuration[DbInitializer.AdminTelegramIdKey];

        // ★ XATO YOZILGAN ID JIMGINA `null` GA AYLANMAYDI — u
        //   `EnsureUsable` da xatoga aylanadi. Sabab: "0" yoki "abc"
        //   yozgan operator ID qo'yganiga ISHONIB qolardi, tizim esa
        //   uni tashlab yuborardi. Bu jimgina yolg'on bo'lardi.
        long? telegramId = null;
        var telegramIdBroken = false;

        if (!string.IsNullOrWhiteSpace(rawTelegramId))
        {
            if (long.TryParse(rawTelegramId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0)
            {
                telegramId = parsed;
            }
            else
            {
                telegramIdBroken = true;
            }
        }

        return new BootstrapAdmin(phone, telegramId, isDevelopment)
        {
            TelegramIdBroken = telegramIdBroken,
            RawTelegramId = rawTelegramId,
        };
    }

    /// <summary>Berilgan Telegram ID o'qib bo'lmaydigan qiymat edimi.</summary>
    public bool TelegramIdBroken { get; init; }

    /// <summary>Diagnostika xabari uchun xom qiymat.</summary>
    public string? RawTelegramId { get; init; }

    /// <summary>
    /// Boshlang'ich administrator HAQIQATAN kira oladigan holatdami.
    /// Bo'lmasa — BALAND OVOZDA yiqiladi.
    /// </summary>
    /// <remarks>
    /// ★ NIMA UCHUN ISTISNO, OGOHLANTIRISH EMAS: log satri konteyner
    /// chiqishida yuzlab boshqa satr orasida yo'qoladi va deploy
    /// "muvaffaqiyatli" deb ko'rinardi. Nosozlik esa faqat birinchi
    /// kirish urinishida — odatda ish vaqti tugagandan keyin —
    /// aniqlanardi. Ishga tushmagan konteyner esa DARHOL ko'rinadi va
    /// ma'lumot hali yozilmagan bo'ladi, ya'ni tuzatish arzon.
    /// </remarks>
    public void EnsureUsable()
    {
        if (TelegramIdBroken)
        {
            throw new InvalidOperationException(
                $"`{DbInitializer.AdminTelegramIdKey}` qiymati musbat butun son bo'lishi kerak "
                + $"(berilgan: '{RawTelegramId}'). Uni to'g'rilang yoki umuman olib tashlang — "
                + "u ixtiyoriy.");
        }

        // ★ TEKSHIRUV `NormalizePhone` ORQALI, "bo'sh emasmi" bo'yicha
        //   EMAS. `Bootstrap__AdminPhone="-"` yoki `"kiritilmagan"` kabi
        //   qiymat bo'sh emas, lekin `SetPhone` uni RAQAMSIZ deb
        //   `PhoneNormalized = null` qilib qo'yardi — ya'ni tekshiruvdan
        //   o'tgan, lekin baribir kira olmaydigan administrator.
        var normalized = User.NormalizePhone(AdminPhone);

        if (normalized is not null)
            return;

        throw new InvalidOperationException(
            $"""
            Bo'sh bazaga birinchi administrator yozilmoqda, lekin uning TELEFON RAQAMI yo'q.

            2026-08-13 dan tizimga kirish FAQAT telefon raqami orqali bo'ladi (email va parol
            olib tashlangan). Raqamsiz administrator yaratilsa, u hech qachon kira olmaydi va
            o'rnatishni faqat `psql` bilan tuzatish mumkin bo'ladi.

            Yechim — muhit o'zgaruvchisini qo'yib, konteynerni qaytadan ishga tushiring:

                Bootstrap__AdminPhone=+998901234567

            Raqam administratorning Telegram hisobiga ro'yxatdan o'tgan raqami bo'lishi SHART —
            kirish kodi aynan o'sha hisobga yuboriladi. Batafsil: docs/DEPLOY_UBUNTU.md.

            (Berilgan qiymat: '{AdminPhone ?? "<yo'q>"}')
            """);
    }
}
