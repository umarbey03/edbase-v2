using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// ILOVA ICHIDAGI BILDIRISHNOMA — qo'ng'iroqchadagi bitta qator
/// ========================================================================
///
/// Bitta qator = bitta foydalanuvchiga ko'rsatiladigan bitta hodisa.
///
/// ══════════════════════════════════════════════════════════════════════
/// 🔴 BU <c>MessageOutbox</c> EMAS VA U QAYTA ISHLATILMAYDI. Uch sabab:
///
///  1) <c>MessageOutbox</c> ATAYLAB Infrastructure ichida va
///     <c>IApplicationDbContext</c> da OCHILMAGAN — u YETKAZIB BERISH
///     mexanizmi, biznes ma'lumoti emas (aynan shu qoida
///     <c>TelegramUpdates</c> ga ham qo'llangan). Uni portga chiqarish
///     butun qatlam chegarasini buzardi.
///
///  2) Uning <c>Body</c> si — TELEGRAM HTML, ya'ni oldindan ekranlangan
///     matn (<c>&amp;lt;</c>, <c>&lt;b&gt;</c> teglari bilan). Vue ro'yxatida
///     u so'zma-so'z <c>&lt;b&gt;</c> bo'lib ko'rinardi yoki `v-html` ga
///     berilib XSS yo'lini ochardi. Bu yerdagi matn — SOF MATN.
///
///  3) Umri boshqa: navbat yozuvi yuborilgach o'z vazifasini bajaradi va
///     tozalanishi mumkin; qo'ng'iroqchadagi yozuv esa foydalanuvchi
///     o'qigunicha (va undan keyin ham, tarix sifatida) yashaydi.
/// ══════════════════════════════════════════════════════════════════════
///
/// ★ MATN YOZIB OLINADI (shablon kaliti + parametr JSON emas) — AYNI
/// sabab <c>NotificationRequest</c> da yozilgan: yozib olingan matn DALIL
/// bo'ladi va shablon keyin o'zgarsa eski qator qayta yasalib ketmaydi.
/// <see cref="Kind"/> baribir saqlanadi, lekin MATN uchun emas —
/// GURUHLASH, ikonka va bosilganda QAYERGA o'tish uchun.
///
/// ★ <see cref="EntityId"/> — "bosilganda qayerga" savolining javobi.
/// ATAYLAB tur bo'yicha ma'noli: <see cref="NotificationKind.SubmissionGraded"/>
/// uchun bu <c>Submission.Id</c>. Har tur uchun alohida ustun qo'shish
/// jadvalni bo'sh maydonlar bilan to'ldirardi; polimorf "EntityType +
/// EntityId" juftligi esa hech qachon FK bo'la olmaydigan yolg'on
/// bog'lanish yaratardi. Bitta <c>long?</c> — eng kam va'da beradigan shakl.
/// </summary>
public class Notification : BaseEntity
{
    /// <summary>Sarlavha uzunligi chegarasi (bir qatorga sig'adigan).</summary>
    public const int MaxTitleLength = 200;

    /// <summary>
    /// Tana uzunligi chegarasi.
    ///
    /// ★ 1000 — Telegram'ning 4096 sidan ANCHA kichik va bu ataylab:
    /// qo'ng'iroqcha ro'yxatidagi qator ikki-uch satrda ko'rinadi, undan
    /// uzun matn baribir qirqilib chizilardi. Chegarani past qo'yish
    /// "bazada bor, lekin ekranda ko'rinmaydi" holatini imkonsiz qiladi.
    /// </summary>
    public const int MaxBodyLength = 1000;

    /// <summary>Kimga. Qabul qiluvchi HAR DOIM bitta — ro'yxatga tarqatish yo'q.</summary>
    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Hodisa turi — ikonka, guruhlash va o'tish yo'li shundan aniqlanadi.</summary>
    public NotificationKind Kind { get; set; }

    /// <summary>SOF MATN sarlavha (belgilashsiz).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>SOF MATN tana (belgilashsiz).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Hodisa bog'langan obyekt Id'si — ma'nosi <see cref="Kind"/> ga bog'liq.
    /// FK EMAS (sabab sinf izohida).
    /// </summary>
    public long? EntityId { get; set; }

    /// <summary>
    /// Qachon o'qilgan. <c>null</c> — o'qilmagan.
    ///
    /// ★ NIMA UCHUN <c>bool IsRead</c> EMAS: "qachon" savoliga javob
    /// bo'lmasa, "o'quvchi xabarni ko'rdimi va qachon ko'rdi" degan
    /// shikoyatni tekshirib bo'lmasdi. Bo'sh joy narxi bir xil.
    /// </summary>
    public DateTimeOffset? ReadAt { get; set; }

    // ---------------------------------------------------------------- yasash

    /// <summary>
    /// Yangi bildirishnoma yasaydi.
    ///
    /// ★ NIMA UCHUN FABRIKA (oddiy obyekt initsializatori emas): uzunlik
    /// chegarasi VA bo'sh matn taqiqi bir joyda qoladi. Ikki chaqiruvchi
    /// paydo bo'lganda (kelajakdagi ikkinchi hodisa turi) ulardan biri
    /// tekshiruvni unutishi mumkin emas.
    ///
    /// ★ MATN QIRQILADI, RAD ETILMAYDI — <c>OutboxWriter</c> dan farqi.
    /// Sabab: u yerdagi matn Telegram HTML va oxiridan qirqish ochiq
    /// <c>&lt;b&gt;</c> qoldirib xabarni YAROQSIZ qilardi. Bu yerda matn
    /// sof, ya'ni qirqilgani ham to'g'ri ko'rinadi. Va eng muhimi: bu
    /// chaqiruv BAHOLASH tranzaksiyasi ichida turadi — uzun izoh tufayli
    /// istisno chiqsa, USTOZNING BAHOSI saqlanmay qolardi.
    /// </summary>
    public static Notification Create(
        long userId,
        NotificationKind kind,
        string title,
        string body,
        long? entityId,
        DateTimeOffset now)
    {
        if (userId <= 0)
            throw new DomainException("Bildirishnoma qabul qiluvchisi ko'rsatilmagan.");

        var safeTitle = Clamp(title, MaxTitleLength);

        if (safeTitle.Length == 0)
            throw new DomainException("Bildirishnoma sarlavhasi bo'sh bo'lishi mumkin emas.");

        return new Notification
        {
            UserId = userId,
            Kind = kind,
            Title = safeTitle,
            Body = Clamp(body, MaxBodyLength),
            EntityId = entityId,
            ReadAt = null,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// "O'qildi" deb belgilaydi.
    /// </summary>
    /// <returns>
    /// Holat haqiqatan o'zgardimi. <c>false</c> — allaqachon o'qilgan edi.
    ///
    /// ★ IDEMPOTENT VA VAQT QAYTA YOZILMAYDI: klient ro'yxatni ochganda
    /// bir necha so'rov parallel ketishi mumkin (ekran ochildi + hub
    /// hodisasi). Ikkinchi so'rov <c>ReadAt</c> ni qayta yozsa, "qachon
    /// ko'rdi" javobi HAR ochilishda yangilanib, o'z ma'nosini yo'qotardi
    /// (<c>GroupChatRead.Advance</c> dagi bilan bir xil qoida).
    /// </returns>
    public bool MarkRead(DateTimeOffset now)
    {
        if (ReadAt is not null) return false;

        ReadAt = now;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Bo'shliqni kesadi va surrogat juftlikni buzmasdan qirqadi.</summary>
    private static string Clamp(string? value, int maxLength)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length <= maxLength) return text;

        // Emoji (surrogat juftlik) o'rtasidan kesilsa yolg'iz surrogat
        // qoladi va u Postgres'ga yozilganda buziladi — sabab
        // `Zinnur.Domain.Common.MessageText` da batafsil.
        var cut = maxLength;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;

        return text[..cut];
    }
}
