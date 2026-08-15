using Zinnur.Application.Telegram.Dtos;

namespace Zinnur.Application.Telegram.Services;

/// <summary>
/// Telegram webhook orqali kelgan yangilanishni qayta ishlaydi.
///
/// ★ HTTP SO'ROVI ICHIDA TASHQI CHAQIRUV YO'Q. Javob xabari
/// <c>INotificationOutbox</c> ga YOZILADI, yuborishni esa fon worker'i
/// qiladi. Sabab: Telegram javobni bir necha soniyada kutadi va kechiksa
/// AYNI yangilanishni qayta yuboradi — natijada bitta hodisa ikki marta
/// ishlanardi. Eski tizimda webhook ichida <c>sendMessage</c> chaqirilardi
/// va aynan shu bo'lardi.
/// </summary>
public interface ITelegramUpdateHandler
{
    /// <summary>
    /// Yangilanishni ishlaydi va natijani qaytaradi.
    /// ISTISNO TASHLAMAYDI — har qanday xato
    /// <see cref="TelegramUpdateOutcome.Failed"/> ga aylanadi, chunki
    /// webhook Telegram'ga DOIM 200 qaytarishi kerak.
    /// </summary>
    Task<TelegramUpdateOutcome> HandleAsync(TelegramUpdateDto update, CancellationToken ct = default);
}

/// <summary>
/// Yangilanish natijasi — log va testlar uchun.
/// Foydalanuvchiga ko'rinadigan matn <see cref="TelegramTemplates"/> da.
/// </summary>
public enum TelegramUpdateOutcome
{
    /// <summary>Tushunilmagan yoki bizga tegishli bo'lmagan yangilanish.</summary>
    Ignored = 0,

    /// <summary>Bu <c>update_id</c> allaqachon ishlangan (Telegram qayta yubordi).</summary>
    Duplicate = 1,

    /// <summary><c>/start</c> ga javob berildi.</summary>
    Greeted = 2,

    /// <summary>Raqam tasdiqlandi va profil BOG'LANDI.</summary>
    Linked = 3,

    /// <summary>Allaqachon bog'langan edi — holat o'zgarmadi.</summary>
    AlreadyLinked = 4,

    /// <summary>Raqam ro'yxatda topilmadi.</summary>
    PhoneNotFound = 5,

    /// <summary>🔴 BOSHQA odamning kontakti yuborildi — RAD ETILDI.</summary>
    ContactMismatch = 6,

    /// <summary>
    /// ⚠️ ESKI NATIJA — 2026-08-13 dan boshlab HECH QACHON qaytmaydi.
    ///
    /// Ilgari xodim raqami bog'lanmasdi (audit X-1 mitigatsiyasi). Endi
    /// bog'lanadi: email va parol bilan kirish olib tashlanganidan keyin
    /// bu qoida xodimni tizimdan butunlay tashqarida qoldirardi.
    ///
    /// ★ RAQAM SAQLANADI, QAYTA ISHLATILMAYDI: qiymatlar logga va
    /// webhook javobiga SATR sifatida chiqadi, ya'ni eski yozuvlarni
    /// o'qiydigan har qanday hisobot bu nomni topishi kerak.
    /// </summary>
    StaffPhone = 7,

    /// <summary>Profil boshqa Telegram akkauntga bog'langan.</summary>
    ProfileTaken = 8,

    /// <summary>Bu Telegram akkaunt boshqa profilga bog'langan.</summary>
    TelegramTaken = 9,

    /// <summary>Profil faol emas.</summary>
    Inactive = 10,

    /// <summary>Oddiy matnga yordam javobi berildi.</summary>
    Helped = 11,

    /// <summary>Ishlashda xato bo'ldi (logga tushdi), Telegram'ga baribir 200.</summary>
    Failed = 12,

    /// <summary>
    /// TELEFON ALMASHTIRISH uchun kod yuborildi (2026-08-15).
    ///
    /// ★ `Linked` DAN FARQI: profil hali BOG'LANMADI va raqam hali
    /// ALMASHMADI — bu shunchaki "kod ketdi" degani. Haqiqiy o'zgarish
    /// foydalanuvchi kodni ILOVAGA kiritganda bo'ladi
    /// (`IProfileService.ConfirmPhoneChangeAsync`). Ikkalasi bir natija
    /// bilan belgilansa, "bog'landi" statistikasi yolg'on ko'rsatardi.
    /// </summary>
    PhoneChangeCodeSent = 13,
}
