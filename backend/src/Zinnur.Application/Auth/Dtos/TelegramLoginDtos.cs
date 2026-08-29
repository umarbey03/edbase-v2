namespace Zinnur.Application.Auth.Dtos;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// BOT ORQALI KIRISH — DEEP-LINK OQIMI (2026-08-28)
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN QURILDI. Telefon + kod oqimida foydalanuvchi raqamini
///   QO'LDA yozadi, keyin kodni Telegramdan ko'chiradi — ikki qadam, va
///   birinchisi eng ko'p xato qilinadigan joy: "+998" bormi, "0" tushdimi,
///   qaysi raqam bilan ro'yxatdan o'tgan edim? Bundan tashqari sayt botga
///   HAVOLA BERMASDI — foydalanuvchi uni Telegram qidiruvidan o'zi topishi
///   kerak edi.
///
///   Bu oqimda saytga hech narsa YOZILMAYDI: bitta tugma botni ochadi, bot
///   esa Telegram akkauntning O'ZIDAN kimligini biladi.
///
/// ══════════════════════════════════════════════════════════════════════
/// 🔴 NIMA UCHUN KOD BARIBIR SO'RALADI (bot allaqachon tanigan bo'lsa ham)
///
/// Deep-link payload OCHIQ matn: havolani hujumchi O'ZI yasab, qurbonga
/// yuborishi mumkin ("kirish uchun shu tugmani bosing"). Qurbon <c>/start</c>
/// bosgan zahoti bot uni TANIYDI — va agar shu daqiqada sessiya ochilsa,
/// sessiya HUJUMCHINING brauzerida ochilardi. Bu klassik hujum
/// (login CSRF / deep-link phishing) va u faqat bitta narsa bilan
/// to'xtatiladi: kod QURBONNING Telegramiga boradi, uni ko'chirib
/// saytga kiritadigan odam esa BRAUZER egasi bo'lishi shart.
///
/// Ya'ni: bot EGALIKNI tasdiqlaydi, kod esa BRAUZER bilan Telegram AYNI
/// odamda ekanini tasdiqlaydi. Ikkalasi ham kerak.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Token">
/// Bir martalik chipta identifikatori. Brauzer uni <c>sessionStorage</c> da
/// saqlaydi va holat so'rovlarida qaytaradi.
///
/// 🔴 BU SESSIYA TOKENI EMAS va u bilan HECH QANDAY himoyalangan
/// ma'lumotni ochib bo'lmaydi — u faqat "boshlangan kirish urinishi" ni
/// nomlaydi.
/// </param>
/// <param name="Link">
/// Botning to'liq deep-link havolasi (<c>https://t.me/...?start=...</c>).
/// Frontend uni O'ZI YASAMAYDI: bot nomi ish jarayonida sozlamadan
/// o'zgaradi, mijozdagi nusxa esa eskirib qolardi.
/// </param>
/// <param name="ExpiresInSeconds">Chipta qancha vaqt yaroqli (taymer uchun).</param>
public sealed record TelegramLoginStartResponse(string Token, string Link, int ExpiresInSeconds);

/// <summary>
/// Chipta holati: <c>GET /api/v1/auth/telegram/status</c>.
/// Frontend buni bir necha soniyada bir so'raydi.
/// </summary>
/// <param name="Status">
/// <see cref="TelegramLoginStatuses"/> dagi qiymatlardan biri. Enum EMAS,
/// SATR — sabab o'sha sinf izohida.
/// </param>
/// <param name="Hint">
/// Foydalanuvchiga ko'rsatiladigan bir qatorli izoh. MATN SERVERDA
/// turadi: holat qo'shilganda (yoki uning ma'nosi o'zgarganda) mijozdagi
/// <c>switch</c> jimgina eskirib qolardi va foydalanuvchi bo'sh ekran
/// ko'rardi.
/// </param>
/// <param name="ExpiresInSeconds">
/// Chipta muddati tugashiga qancha qolgani. <c>0</c> — muddat tugagan
/// yoki chipta umuman yo'q.
/// </param>
public sealed record TelegramLoginStatusResponse(string Status, string Hint, int ExpiresInSeconds);

/// <summary>Kodni tasdiqlash: <c>POST /api/v1/auth/telegram/verify</c>.</summary>
/// <param name="Token">Chipta identifikatori (<see cref="TelegramLoginStartResponse.Token"/>).</param>
/// <param name="Code">Bot yuborgan 6 xonali kod.</param>
public sealed record TelegramLoginVerifyRequest(string Token, string Code);

/// <summary>
/// Chipta holatlarining YAGONA ro'yxati.
///
/// ★ NIMA UCHUN ENUM EMAS, SATR KONSTANTALARI: qiymat JSON'ga chiqadi va
/// frontend uni AYNAN shu ko'rinishda taqqoslaydi. Enum bo'lsa uning JSON
/// shakli seriyalash sozlamasiga bog'liq bo'lardi (raqam? PascalCase?
/// camelCase?) — ya'ni mijozdagi taqqoslash serverning global JSON
/// sozlamasi o'zgarganda JIMGINA buzilardi.
///
/// ★ NOMLAR O'ZBEKCHA: ular kod bazasining qolgan qismidagi domen
/// atamalari bilan bir xil tilda va loglarda o'qilishi oson.
/// </summary>
public static class TelegramLoginStatuses
{
    /// <summary>Chipta ochildi, lekin botga hali <c>/start</c> kelmadi.</summary>
    public const string Waiting = "kutilmoqda";

    /// <summary>
    /// Bot ochildi, lekin bu Telegram akkaunt hech qaysi profilga
    /// bog'lanmagan — foydalanuvchi botda «📱 Raqamni ulashish» tugmasini
    /// bosishi kerak. Oqim SHU YERDA to'xtamaydi: raqam ulangach kod
    /// avtomatik yuboriladi.
    /// </summary>
    public const string ContactNeeded = "raqam-kerak";

    /// <summary>Kod yuborildi — endi uni saytga kiritish kerak.</summary>
    public const string CodeSent = "kod";

    /// <summary>
    /// Profil topildi, lekin FAOL EMAS (chiqarilgan/to'xtatilgan).
    /// Bu yerda oshkorlik xavfi yo'q: chiptani boshlagan va botda
    /// <c>/start</c> bosgan odam — profil egasining O'ZI.
    /// </summary>
    public const string Inactive = "nofaol";

    /// <summary>Chipta yo'q yoki muddati o'tgan — boshidan boshlash kerak.</summary>
    public const string Missing = "yoq";
}
