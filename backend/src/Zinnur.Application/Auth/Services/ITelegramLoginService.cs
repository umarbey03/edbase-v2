using Zinnur.Application.Auth.Dtos;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.Auth.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// BOT ORQALI KIRISH — DEEP-LINK OQIMI (2026-08-28)
/// ════════════════════════════════════════════════════════════════════════
///
/// Uch qadam, uchtasi ham SHU servisda:
///
///   1) <see cref="StartAsync"/>  — brauzer chipta oladi va bot havolasini
///      ko'radi. Foydalanuvchidan HECH NARSA so'ralmaydi.
///   2) <see cref="AttachAsync"/> — bot <c>/start &lt;chipta&gt;</c> ni
///      oladi, Telegram akkaunt orqali profilni topadi va 6 xonali kod
///      yuboradi. Bu metodni FAQAT webhook chaqiradi.
///   3) <see cref="VerifyAsync"/> — brauzer kodni yuboradi, sessiya
///      ochiladi.
///
/// ══════════════════════════════════════════════════════════════════════
/// 🔴 NIMA UCHUN 2-QADAMDA SESSIYA OCHILMAYDI
///
/// Botga <c>/start</c> yozgan odam — chiptani BOSHLAGAN odam BILAN AYNI
/// bo'lishi shart emas. Deep-link havolasini hujumchi o'zi yasab, qurbonga
/// yuborishi mumkin ("kirish uchun shu tugmani bosing"). Qurbon tugmani
/// bosgani zahoti sessiya ochilsa, u HUJUMCHINING brauzerida ochilardi.
///
/// Kod aynan shu bo'shliqni yopadi: u qurbonning Telegramiga boradi, uni
/// saytga kiritish uchun esa BRAUZERGA kirish kerak. Ya'ni bot EGALIKNI,
/// kod esa "brauzer va Telegram BIR ODAMDA" ekanini tasdiqlaydi.
/// ══════════════════════════════════════════════════════════════════════
///
/// ★ TOKEN BU YERDA YASALMAYDI. Kod tasdiqlangach ish
/// <see cref="IAuthService.LoginWithPhoneAsync"/> ga o'tadi — AYNI
/// <see cref="IPhoneLoginService"/> naqshi: modul EGALIKNI tekshiradi,
/// tokenni esa yagona joydan oladi. Ikkinchi, parallel token yo'li
/// yozilishi <see cref="IAuthService"/> izohida QAT'IY taqiqlangan.
///
/// ⚠️ TELEFON OQIMI OLIB TASHLANMADI. U zaxira yo'l bo'lib qoladi:
/// bot bloklangan, havola ochilmagan yoki foydalanuvchi Telegramni boshqa
/// qurilmada ishlatadigan holatlar bor. Ikkala oqim ham AYNI kodni
/// (6 xona) va AYNI yakuniy metodni ishlatadi.
/// </summary>
public interface ITelegramLoginService
{
    /// <summary>
    /// 1-QADAM: chipta ochadi va bot havolasini qaytaradi.
    /// </summary>
    /// <remarks>
    /// <c>ServiceUnavailableException</c> — bot tokeni yoki bot nomi
    /// sozlanmagan. Bu 500 emas: bizning bug'imiz emas, sozlanmagan xizmat
    /// (<see cref="IPhoneLoginService"/> dagi AYNI qaror).
    /// </remarks>
    Task<TelegramLoginStartResponse> StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Chipta holati (brauzer bir necha soniyada bir so'raydi).
    /// </summary>
    /// <remarks>
    /// ISTISNO TASHLAMAYDI: noma'lum yoki eskirgan chipta ham 200 bilan,
    /// <see cref="TelegramLoginStatuses.Missing"/> holatida qaytadi.
    /// Sabab — bu so'rov SEKUNDIGA takrorlanadi va uning xato bo'lishi
    /// mijozda qayta urinish/xato ko'rsatish mantig'ini ikkilantirardi.
    /// </remarks>
    Task<TelegramLoginStatusResponse> StatusAsync(string? token, CancellationToken ct = default);

    /// <summary>
    /// 3-QADAM: kodni tasdiqlaydi va SESSIYA ochadi.
    /// </summary>
    /// <remarks>
    /// <c>401</c> — kod xato yoki chipta muddati o'tgan (ikkalasi uchun
    /// AYNI matn) · <c>403</c> — profil faol emas · <c>429</c> —
    /// urinishlar tugadi.
    /// </remarks>
    Task<AuthResponse> VerifyAsync(TelegramLoginVerifyRequest request, CancellationToken ct = default);

    /// <summary>
    /// 2-QADAM: botga kelgan <c>/start &lt;payload&gt;</c> ni chiptaga
    /// bog'laydi va kerak bo'lsa kod yuboradi.
    /// </summary>
    /// <remarks>
    /// 🔴 FAQAT WEBHOOK CHAQIRADI (<c>ITelegramUpdateHandler</c>).
    /// <paramref name="telegramUserId"/> Telegramning O'ZIDAN, imzolangan
    /// webhook tanasidan keladi; uni HTTP so'rov tanasidan olib chaqirish
    /// eski tizimning X-1 zaifligini qaytarardi.
    ///
    /// ★ XABAR NAVBATGA YOZILADI, YUBORILMAYDI — chaqiruvchining
    /// <c>SaveChangesAsync</c> i uni bitta tranzaksiyada saqlaydi
    /// (<c>ITelegramUpdateHandler</c> izohidagi qoida).
    /// </remarks>
    /// <param name="payload"><c>/start</c> dan keyingi qism (chipta bo'lishi SHART emas).</param>
    /// <param name="telegramUserId">Xabar yuboruvchining Telegram ID'si.</param>
    /// <param name="linked">
    /// Shu Telegram akkauntga bog'langan profil (chaqiruvchi allaqachon
    /// topgan) yoki <c>null</c>. ATAYLAB argument: chaqiruvchi uni yuqorida
    /// baribir o'qiydi va ikkinchi so'rov faqat ortiqcha yuk bo'lardi.
    /// </param>
    /// <param name="chatId">Kod yuboriladigan suhbat.</param>
    Task<TelegramLoginAttach> AttachAsync(
        string? payload, long telegramUserId, User? linked, long chatId, CancellationToken ct = default);

    /// <summary>
    /// Raqam ulangandan KEYIN kutayotgan chiptani davom ettiradi
    /// (<see cref="TelegramLoginStatuses.ContactNeeded"/> shoxining oxiri).
    /// </summary>
    /// <returns>
    /// <c>true</c> — kutayotgan chipta bor edi va kod yuborildi.
    /// <c>false</c> — bu odam saytdan kelmagan, botga o'zi yozgan.
    /// </returns>
    Task<bool> ContinueAfterLinkAsync(User user, long chatId, CancellationToken ct = default);
}

/// <summary>
/// <see cref="ITelegramLoginService.AttachAsync"/> natijasi — bot qanday
/// javob berishini SHU qiymat hal qiladi.
/// </summary>
public enum TelegramLoginAttach
{
    /// <summary>
    /// Payload chipta emas (bo'sh, boshqa kampaniya havolasi yoki
    /// shunchaki tasodifiy matn). Bot ODATDAGI salomlashuvni beradi.
    /// </summary>
    NotTicket = 0,

    /// <summary>Profil topildi, kod navbatga qo'yildi.</summary>
    CodeSent = 1,

    /// <summary>
    /// Bu Telegram akkaunt hech qaysi profilga bog'lanmagan — bot
    /// «📱 Raqamni ulashish» tugmasini ko'rsatadi, chipta esa KUTIB
    /// turadi (raqam ulangach kod avtomatik ketadi).
    /// </summary>
    ContactNeeded = 2,

    /// <summary>Profil topildi, lekin faol emas.</summary>
    Inactive = 3,

    /// <summary>Chipta yo'q yoki muddati o'tgan — saytdan qayta boshlash kerak.</summary>
    Expired = 4,
}
