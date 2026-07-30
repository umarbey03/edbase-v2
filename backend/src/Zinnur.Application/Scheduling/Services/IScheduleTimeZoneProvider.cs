namespace Zinnur.Application.Scheduling.Services;

/// <summary>
/// Jadval qaysi vaqt zonasida tuzilishini beradigan PORT.
///
/// NIMA UCHUN INTERFEYS (va nega <c>TimeZoneInfo.Local</c> EMAS):
///
///  1) Konteyner UTC'da ishlaydi. <c>TimeZoneInfo.Local</c> ishlatilsa guruh
///     soati "19:00" 14:00 Toshkent (ya'ni 09:00Z) o'rniga 19:00Z ga
///     aylanardi — butun jadval BESH SOATGA siljib ketardi va buni faqat
///     birinchi dars o'tib ketganda sezish mumkin bo'lardi.
///
///  2) Zona KONFIGURATSIYADAN keladi (<c>App:TimeZone</c>, default
///     <c>Asia/Tashkent</c>). Kodda qotib qolsa boshqa mintaqaga
///     ochilganda manba kodini o'zgartirish kerak bo'lardi.
///
/// Amalga oshirilishi Infrastructure qatlamida (<c>ConfiguredScheduleTimeZone</c>).
/// </summary>
public interface IScheduleTimeZoneProvider
{
    /// <summary>Guruh <c>StartTime</c> qiymati SHU zonaning devor-vaqti sifatida o'qiladi.</summary>
    TimeZoneInfo TimeZone { get; }
}
