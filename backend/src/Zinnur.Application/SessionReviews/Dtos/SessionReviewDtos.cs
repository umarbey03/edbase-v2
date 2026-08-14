using Zinnur.Domain.Enums;

namespace Zinnur.Application.SessionReviews.Dtos;

/// <summary>
/// Dars sifati tahlili — modal oynaning to'liq mazmuni (R29 / R30).
///
/// 🔴 BU JAVOB O'QUVCHIGA HECH QACHON BERILMAYDI. Rad etish
/// <c>SessionReviewService</c> ning BIRINCHI qatorida, ya'ni tugmani
/// yashirish yoki controller atributi bilan emas — sabab
/// <c>ISessionReviewService</c> izohida.
/// </summary>
/// <param name="Verdict">
/// <c>SessionReviewVerdict</c> nomi (<c>NotReviewed</c>, <c>Approved</c>,
/// <c>HasIssue</c>). ATAYLAB SATR — <c>RecordingDto.Status</c> bilan AYNI
/// sabab: enum raqami klientga hech narsa anglatmaydi va tartibi
/// o'zgarsa jimgina noto'g'ri nishon chizilardi.
/// </param>
/// <param name="AuthorName">
/// Xulosani yozgan xodimning ismi. ⚠️ MAJBURIY QISM, bezak emas: ustoz
/// o'z darsi haqidagi bahoni o'qiyotganda "kim aytdi" savoliga javob
/// bo'lmasa, e'tiroz bildirish yoki tushuntirish so'rash yo'li yopiq
/// bo'lardi (<c>StudentNote.Author</c> dagi AYNI dalil).
/// </param>
/// <param name="CanEdit">
/// Chaqiruvchi bu tahlilni tahrirlay oladimi. ★ Bu — QULAYLIK, RUXSAT
/// EMAS: haqiqiy qoida servisda va u har yozishda qaytadan tekshiriladi.
/// Bayroq faqat ustozga foydasiz tugma ko'rsatilmasligi uchun.
/// </param>
public sealed record SessionReviewDto(
    long Id,
    long SessionId,
    string Verdict,
    string Body,
    long AuthorId,
    string AuthorName,
    bool CanEdit,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Tahlilni yozish yoki yangilash (UPSERT).
///
/// ★ NIMA UCHUN <c>POST</c> + <c>PUT</c> AJRATILMADI: bitta darsda BITTA
/// tahlil bo'ladi (unikal indeks), ya'ni klient yozishdan oldin "bormi?"
/// deb so'rashga majbur bo'lardi va ikki so'rov orasida boshqa xodim
/// yozib qo'ysa 409 olardi. Upsert bu poygani butunlay yo'q qiladi.
/// </summary>
public sealed record SaveSessionReviewRequest(
    SessionReviewVerdict Verdict,
    string Body);
