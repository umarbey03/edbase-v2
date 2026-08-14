using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// O'QUV BO'LIMINING DARS SIFATI TAHLILI (talab R29 va R30)
/// ════════════════════════════════════════════════════════════════════════
///
/// Loyiha egasi (R29): *"video recording yozuvlar bo'limidagi videolarda
/// o'quv bo'limi sifat nazorati tahlili xulosalari ham bo'lsin, uni
/// bosganda modal window orqali ochilsin"*.
/// Loyiha egasi (R30): *"darslarim bo'limida qo'shimcha button orqali
/// teacher o'zining dars tahlilini ko'ra olsin modal window orqali"*.
///
/// ★ IKKALA TALAB — BITTA MA'LUMOT, IKKI OYNA. R29 uni yozuvlar
/// ro'yxatidan, R30 esa ustozning "Darslarim" jadvalidan ochadi. Shuning
/// uchun entity BIR MARTA loyihalandi va ikkala yo'l ham AYNI qatorni
/// o'qiydi — aks holda ustoz ko'rgan xulosa bilan o'quv bo'limi yozgan
/// xulosa vaqt o'tib ajralib ketardi.
///
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 ENG MUHIM QAROR: TAHLIL DARSGA BOG'LANADI, YOZUVGA EMAS
/// ════════════════════════════════════════════════════════════════════════
///
/// <c>SessionRecording</c> BITTA DARS UCHUN BIR NECHTA QATORGA ATAYLAB
/// ruxsat beradi — bu uning butun mavjudlik sababi (o'sha sinf izohi:
/// "eski tizimda bitta darsga bitta yozuv edi va birinchi urinish yiqilsa
/// tarix yo'qolardi"). Shundan uchta oqibat kelib chiqadi:
///
///  1) QAYTA YOZILGAN DARSDA IKKITA TAHLIL QILINADIGAN OBYEKT bo'lardi va
///     "qaysi biri SHU darsning xulosasi?" degan savolga javob yo'q edi.
///     Ro'yxatda ikkita "Muammo bor" nishoni chiqib, ustoz ikki marta
///     jazolangandek ko'rinardi.
///  2) YIQILGAN YOZUV TAHLILNI O'ZI BILAN OLIB KETARDI: watchdog qatorni
///     <c>Failed</c> qilgach yangi urinish YANGI qator ochadi — tahlil
///     esa eskisida qolib, hech qayerda ko'rinmasdi.
///  3) YOZUVSIZ DARSNI TAHLIL QILIB BO'LMASDI. O'quv bo'limi xodimi jonli
///     darsda O'ZI o'tirgan bo'lishi mumkin (eng ishonchli sifat nazorati
///     aynan shu) — bunday darsda yozuv qatori umuman bo'lmaydi.
///
/// ★ R30 BU QARORNI MUSTAQIL RAVISHDA TASDIQLAYDI: "Darslarim" jadvali
/// <c>SessionStatsDto</c> bilan ishlaydi va u yerda yozuv Id'si UMUMAN
/// YO'Q. Yozuvga bog'langan bo'lsa, ustoz oynasi avval dars -> yozuv
/// izlashga majbur bo'lardi va yozuvi chiqmagan darsda tugma o'lik
/// bo'lardi.
///
/// ⚠️ NARXI OCHIQ AYTILADI: BITTA DARSGA BITTA TAHLIL (unikal indeks
/// <c>SessionId</c> bo'yicha). "Ikkinchi fikr" alohida qator sifatida
/// saqlanmaydi — mavjud tahlil TAHRIRLANADI. Bu ONGLI soddalashtirish:
/// eski ilovadagi nishon ham bitta uch holatli xulosa edi, muhokama
/// zanjiri emas. Zanjir kerak bo'lsa u ALOHIDA talab va alohida jadval.
///
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 BU YOZUV O'QUVCHIDAN TO'LIQ YOPIQ
/// ════════════════════════════════════════════════════════════════════════
///
/// Mazmuni — ustoz haqidagi ichki baho ("tushuntirish sust", "vaqtni
/// noto'g'ri taqsimlagan"). O'quvchi uni KO'RMAYDI va bu tugmani
/// yashirish bilan emas, SERVIS qatlamida ta'minlanadi
/// (<c>SessionReviewService</c> <c>Student</c> uchun har yo'lda 403).
/// <see cref="StudentNote"/> dagi AYNI qoida va AYNI sabab.
/// </summary>
public class SessionReview : BaseEntity
{
    /// <summary>
    /// Tahlil matnining chegarasi.
    ///
    /// ★ <see cref="StudentNote.MaxBodyLength"/> (2000) DAN KATTA VA BU
    /// ATAYLAB: o'quvchi izohi — bir-ikki jumlalik eslatma, dars tahlili
    /// esa tuzilgan xulosa (kirish, kuchli tomonlar, tavsiyalar). 2000
    /// belgida xodim matnni qisqartirishga majbur bo'lardi va eng
    /// foydali qismi — tavsiyalar — tushib qolardi.
    ///
    /// ⚠️ Cheksiz ham EMAS: chegarasiz matn ustuni bir kun kimningdir
    /// nusxa-joylashtirgan transkripti bilan to'lardi.
    /// </summary>
    public const int MaxBodyLength = 4000;

    /// <summary>Tahlil QAYSI dars haqida.</summary>
    public long SessionId { get; set; }

    /// <summary>
    /// Dars — navigatsiya. Ruxsat tekshiruvi guruh (ustoz/kurator) orqali
    /// o'tadi, ya'ni dars deyarli har o'qishda kerak bo'ladi.
    /// </summary>
    public LiveSession? Session { get; set; }

    /// <summary>
    /// Tahlilni KIM yozgan. Faqat o'quv bo'limi yoki administrator —
    /// qoida <c>SessionReviewService</c> da (rol entity'da tekshirilmaydi:
    /// Domain <c>User.Role</c> ga qarab qaror qilsa, rol o'zgarganda eski
    /// qatorlar "noto'g'ri" bo'lib qolardi).
    /// </summary>
    public long AuthorId { get; set; }

    /// <summary>
    /// Muallif — navigatsiya SHART: ustoz oynasida xulosa ostida uni
    /// yozgan xodimning ismi turadi. Anonim baho ustoz uchun javobsiz
    /// savol bo'lardi (<see cref="StudentNote.Author"/> bilan AYNI dalil).
    /// </summary>
    public User? Author { get; set; }

    /// <summary>Yakuniy xulosa (eski ilovadagi uch holatli nishon).</summary>
    public SessionReviewVerdict Verdict { get; set; } = SessionReviewVerdict.NotReviewed;

    /// <summary>Tahlil matni.</summary>
    public required string Body { get; set; }

    // ---------------------------------------------------------------- hisoblanuvchi

    /// <summary>
    /// Xulosa chiqarilganmi (qoralama emasmi). Ro'yxatdagi nishon shunga
    /// qarab "Ko'rilmagan" yoki haqiqiy holatni ko'rsatadi.
    /// </summary>
    public bool IsDecided => Verdict != SessionReviewVerdict.NotReviewed;

    // ---------------------------------------------------------------- xatti-harakat

    public static SessionReview Create(
        long sessionId,
        long authorId,
        SessionReviewVerdict verdict,
        string? body,
        DateTimeOffset now) =>
        new()
        {
            SessionId = sessionId,
            AuthorId = authorId,
            Verdict = verdict,
            Body = RequireBody(body),
            CreatedAt = now,
        };

    /// <summary>
    /// Mazmunni almashtiradi.
    ///
    /// ★ <see cref="AuthorId"/> ATAYLAB O'ZGARMAYDI — <see cref="StudentNote.Edit"/>
    /// dagi AYNI qoida: tahrirlash "boshqa odam yozgan" qilib ko'rsatish
    /// yo'li bo'lmasligi kerak. ⚠️ Bu shuni ham anglatadi: ikkinchi xodim
    /// tahrirlaganda ism BIRINCHISINIKI bo'lib qoladi. Bu ONGLI tanlov —
    /// "kim boshladi" javobgarlikning asosi, "kim oxirgi tahrirladi" esa
    /// <see cref="BaseEntity.UpdatedAt"/> bilan birga hech kimga
    /// kerak bo'lmagan tafsilot edi.
    /// </summary>
    public void Edit(SessionReviewVerdict verdict, string? body, DateTimeOffset now)
    {
        // ★ AVVAL TEKSHIRUV, KEYIN O'ZGARTIRISH. Tartib teskari bo'lsa
        //   bo'sh matn bilan yuborilgan so'rov istisno tashlab, LEKIN
        //   xulosani allaqachon o'zgartirgan bo'lardi — ya'ni rad etilgan
        //   so'rov obyektni yarim o'zgargan holda qoldirardi.
        var value = RequireBody(body);

        Verdict = verdict;
        Body = value;
        UpdatedAt = now;
    }

    private static string RequireBody(string? body)
    {
        var value = body?.Trim();

        if (string.IsNullOrEmpty(value))
            throw new DomainException("Tahlil matni bo'sh bo'lishi mumkin emas.");

        if (value.Length > MaxBodyLength)
            throw new DomainException($"Tahlil {MaxBodyLength} belgidan oshmasin.");

        return value;
    }
}
