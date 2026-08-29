using Zinnur.Application.Auth.Dtos;

namespace Zinnur.Application.Auth.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// BOT ORQALI KIRISH CHIPTASINING SAQLASH JOYI (Redis)
/// ════════════════════════════════════════════════════════════════════════
///
/// Chipta — BRAUZERDAGI kirish urinishi bilan TELEGRAMDAGI <c>/start</c>
/// ni bog'laydigan yagona narsa. U ikki jarayon (HTTP so'rovi va webhook)
/// orasida yashaydi, ya'ni jarayon xotirasida TURA OLMAYDI: ikkinchi
/// instance qo'shilgan zahoti webhook bir konteynerga, holat so'rovi esa
/// boshqasiga tushib, oqim JIMGINA ishlamay qolardi.
///
/// ★ NIMA UCHUN BAZADA EMAS: umri 15 daqiqa, va u faqat oqim davomida
///   kerak. Baza jadvali bo'lsa migratsiya, indeks va tozalash vazifasi
///   qo'shilardi (<see cref="IPhoneLoginCodeStore"/> dagi AYNI mulohaza).
///
/// ══════════════════════════════════════════════════════════════════════
/// 🔴 KALIT — TOKENNING O'ZI EMAS, HASH'I
///
/// Token brauzerda saqlanadi va u bilan <c>/verify</c> chaqiriladi, ya'ni
/// u AMALDA maxfiy qiymat. Redis kaliti esa loglarda, <c>SCAN</c>
/// chiqishida va monitoring panellarida ochiq ko'rinadi. Kalit tokenning
/// O'ZI bo'lsa, Redis'ni ko'ra olgan odam boshqalarning ochiq chiptalarini
/// o'qib, ular kod kiritishidan oldin sessiyani egallab olardi.
///
/// KOD ham xuddi shunday — faqat tuzlangan hash'i yoziladi
/// (<see cref="IPhoneLoginCodeStore"/> izohidagi sabab bilan AYNI).
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public interface ITelegramLoginTicketStore
{
    /// <summary>
    /// Yangi chipta ochadi (<see cref="TelegramLoginStatuses.Waiting"/>).
    /// </summary>
    Task CreateAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Chiptani o'qiydi. <c>null</c> — yo'q yoki muddati o'tgan
    /// (ikkalasi AYNI holat: chaqiruvchi ularni ajratmasligi kerak).
    /// </summary>
    Task<TelegramLoginTicket?> GetAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Holatni yangilaydi — kodsiz shoxlar uchun
    /// (<c>raqam-kerak</c>, <c>nofaol</c>).
    /// </summary>
    /// <remarks>
    /// Chipta yo'q bo'lsa HECH NIMA qilinmaydi va istisno TASHLANMAYDI:
    /// bu poyga holati (foydalanuvchi 15 daqiqadan keyin <c>/start</c>
    /// bosdi) va u xato emas.
    /// </remarks>
    Task SaveStatusAsync(string token, string status, long? userId, CancellationToken ct = default);

    /// <summary>
    /// Kodni chiptaga yozadi va holatni <see cref="TelegramLoginStatuses.CodeSent"/>
    /// ga o'tkazadi. Urinishlar hisoblagichi nolga qaytariladi.
    /// </summary>
    /// <param name="code">Ochiq kod — SAQLANMAYDI, faqat tuzlangan hash'i.</param>
    Task SaveCodeAsync(string token, long userId, string code, CancellationToken ct = default);

    /// <summary>
    /// Kodni tekshiradi. TO'G'RI bo'lsa chiptani DARHOL o'chiradi
    /// (bir martalik) va profil identifikatorini qaytaradi.
    /// </summary>
    /// <remarks>
    /// ★ TAQQOSLASH DOIMIY VAQTDA — sabab
    /// <see cref="IPhoneLoginCodeStore.ConsumeAsync"/> izohida.
    /// </remarks>
    Task<(PhoneCodeCheck Check, long? UserId)> ConsumeAsync(
        string token, string code, CancellationToken ct = default);

    /// <summary>
    /// «Raqam kutilyapti» belgisini qo'yadi: bu Telegram akkaunt kontaktini
    /// ulagan zahoti kod AYNAN shu chiptaga yuboriladi.
    /// </summary>
    /// <remarks>
    /// ★ KALIT TELEGRAM ID BO'YICHA, chipta bo'yicha emas: kontakt kelganda
    /// bizda faqat yuboruvchining Telegram ID'si bo'ladi — u qaysi chiptadan
    /// kelganini xabarning o'zi AYTMAYDI.
    ///
    /// ⚠️ BITTA AKKAUNTGA BITTA KUTUV: ikkinchi chipta birinchisining
    /// ustiga yoziladi. Bu to'g'ri xatti-harakat — odam ikki brauzerda
    /// oqim boshlagan bo'lsa, kod OXIRGI urinishga tegishli bo'ladi.
    /// </remarks>
    Task SetPendingAsync(long telegramUserId, string token, CancellationToken ct = default);

    /// <summary>
    /// Kutayotgan chiptani OLADI va belgini o'chiradi (bir martalik).
    /// <c>null</c> — bu akkaunt hech qanday oqim boshlamagan (botga o'zi
    /// kelgan, saytdan emas).
    /// </summary>
    Task<string?> TakePendingAsync(long telegramUserId, CancellationToken ct = default);
}

/// <summary>
/// Chiptaning tashqariga ko'rinadigan qismi.
///
/// 🔴 KOD HASH'I VA TUZI BU YERDA YO'Q: ularni o'qishning yagona to'g'ri
/// yo'li — <see cref="ITelegramLoginTicketStore.ConsumeAsync"/>. Ular
/// DTO'ga chiqarilsa, ertami-kechmi kimdir taqqoslashni servis ichida
/// "qo'lda" yozardi va doimiy vaqtli taqqoslash yo'qolardi.
/// </summary>
/// <param name="Status"><see cref="TelegramLoginStatuses"/> qiymatlaridan biri.</param>
/// <param name="UserId">Bot tanigan profil (hali tanilmagan bo'lsa <c>null</c>).</param>
/// <param name="CreatedAt">Chipta ochilgan payt — qolgan muddatni hisoblash uchun.</param>
public sealed record TelegramLoginTicket(string Status, long? UserId, DateTimeOffset CreatedAt);
