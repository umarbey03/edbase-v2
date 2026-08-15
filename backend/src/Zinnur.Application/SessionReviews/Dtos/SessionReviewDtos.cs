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
/// <param name="Plus">Ijobiy tomonlar. <c>null</c> — kiritilmagan (ixtiyoriy).</param>
/// <param name="Minus">Kamchiliklar. <c>null</c> — kiritilmagan (ixtiyoriy).</param>
/// <param name="Conclusion">Xulosa va yechimlar — YAKUNIY, MAJBURIY qism.</param>
/// <param name="SessionScheduledStart">
/// DARSNING jadval bo'yicha boshlanish vaqti (tahlil YOZILGAN/YANGILANGAN
/// vaqt EMAS — <c>CreatedAt</c>/<c>UpdatedAt</c> allaqachon bor).
/// </param>
/// <param name="GroupName">Dars qaysi guruhga tegishli.</param>
/// <param name="SessionTitle">
/// Darsning mavzu nomi. <c>null</c> — ustoz sarlavha kiritmagan (bunday
/// holatda frontend `GroupName`ga tushadi, `sessionTitle()` bilan AYNI
/// zaxira qoidasi — `entities/session`).
/// </param>
/// <param name="TeacherName">
/// Darsni OLIB BORISHI kerak bo'lgan xodim — <c>Type</c>ga qarab guruhning
/// ustozi yoki kuratori (<c>LiveSessionDto.HostName</c> bilan AYNI qoida).
/// <c>null</c> — guruhga hali xodim biriktirilmagan.
/// </param>
/// <param name="Scores">
/// Mezon asosidagi ballar (R29/R30 kengaytmasi). Bo'sh massiv — hali
/// ballanmagan yoki eski, ballashsiz tahlil (orqaga mos: erkin matn
/// yagona bo'lgan davrdan qolgan yozuvlar shunday bo'ladi).
/// </param>
/// <param name="TotalScore">Yig'ilgan ball. <c>Scores</c> bo'sh bo'lsa — 0.</param>
/// <param name="TotalMaxScore">Maksimal ball. <c>Scores</c> bo'sh bo'lsa — 0.</param>
/// <param name="ScorePercent">
/// Foiz, yoki <c>null</c> — hali BIRORTA ham mezon bo'yicha ball
/// qo'yilmagan (0% bilan aralashmasin: bittasi "hali baholanmagan",
/// ikkinchisi "hammasiga 0 qo'yilgan").
/// </param>
public sealed record SessionReviewDto(
    long Id,
    long SessionId,
    string Verdict,
    string? Plus,
    string? Minus,
    string Conclusion,
    DateTimeOffset SessionScheduledStart,
    string GroupName,
    string? SessionTitle,
    string? TeacherName,
    long AuthorId,
    string AuthorName,
    bool CanEdit,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<SessionReviewScoreDto> Scores,
    decimal TotalScore,
    decimal TotalMaxScore,
    decimal? ScorePercent);

/// <summary>Bitta mezon bo'yicha qo'yilgan ball — yozish vaqtidagi nom/maksimal ball bilan.</summary>
public sealed record SessionReviewScoreDto(
    long? CriterionId,
    string CriterionName,
    decimal MaxScore,
    decimal Score);

/// <summary>
/// Tahlilni yozish yoki yangilash (UPSERT).
///
/// ★ NIMA UCHUN <c>POST</c> + <c>PUT</c> AJRATILMADI: bitta darsda BITTA
/// tahlil bo'ladi (unikal indeks), ya'ni klient yozishdan oldin "bormi?"
/// deb so'rashga majbur bo'lardi va ikki so'rov orasida boshqa xodim
/// yozib qo'ysa 409 olardi. Upsert bu poygani butunlay yo'q qiladi.
/// </summary>
/// <param name="Plus">Ijobiy tomonlar. Ixtiyoriy.</param>
/// <param name="Minus">Kamchiliklar. Ixtiyoriy.</param>
/// <param name="Conclusion">Xulosa va yechimlar — MAJBURIY (bo'sh bo'lsa 409).</param>
/// <param name="Scores">
/// Mezon asosidagi ballar. Bo'sh yoki <c>null</c> — faqat erkin matn bilan
/// baholash (ballash ixtiyoriy, majburiy emas).
///
/// ★ NULLABLE VA STANDART QIYMATLI ATAYLAB: eski klient (yoki integratsion
/// test) bu maydonni umuman yubormasa ham so'rov 400 BILAN RAD ETILMASLIGI
/// kerak — mezon bilan ballash R29/R30 ustiga QO'SHILGAN, majburiy emas.
/// </param>
public sealed record SaveSessionReviewRequest(
    SessionReviewVerdict Verdict,
    string Conclusion,
    string? Plus = null,
    string? Minus = null,
    IReadOnlyList<SaveSessionReviewScoreRequest>? Scores = null);

/// <summary>Bitta mezon uchun yuborilgan ball. <c>CriterionId</c> SERVERDAGI katalogdan tekshiriladi.</summary>
public sealed record SaveSessionReviewScoreRequest(
    long CriterionId,
    decimal Score);
