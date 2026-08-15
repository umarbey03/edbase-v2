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
    /// Har bir bo'lim (<see cref="Plus"/>/<see cref="Minus"/>/
    /// <see cref="Conclusion"/>) uchun chegara.
    ///
    /// ★ <see cref="StudentNote.MaxBodyLength"/> BILAN AYNI (2000): eski
    /// yagona <c>Body</c> maydoni (4000) endi UCHTA aniq maqsadli bo'limga
    /// bo'lingani uchun har biriga o'sha yagona hajmning yarmi yetarli —
    /// "Ijobiy tomonlar" yoki "Kamchiliklar" 2000 belgidan uzun bo'lsa,
    /// bu allaqachon tuzilmasiz oqim, alohida bo'lim emas.
    /// </summary>
    public const int MaxSectionLength = 2000;

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

    /// <summary>
    /// Ijobiy tomonlar — kuchli jihatlar. IXTIYORIY: har tahlilda ijobiy
    /// yozadigan narsa bo'lavermaydi.
    /// </summary>
    public string? Plus { get; set; }

    /// <summary>
    /// Kamchiliklar — yaxshilash kerak jihatlar. IXTIYORIY, <see cref="Plus"/>
    /// bilan AYNI sabab.
    /// </summary>
    public string? Minus { get; set; }

    /// <summary>
    /// Xulosa va yechimlar — YAKUNIY, MAJBURIY qism (eski yagona <c>Body</c>
    /// maydonining vorisi): "Ijobiy"/"Kamchilik" ixtiyoriy ro'yxat bo'lsa,
    /// bu — ustozga yo'naltirilgan aniq tavsiya, tahlilning o'zagi.
    /// </summary>
    public required string Conclusion { get; set; }

    /// <summary>
    /// Mezon asosidagi ballar (R29/R30 kengaytmasi). Erkin matn
    /// (<see cref="Verdict"/>/<see cref="Plus"/>/<see cref="Minus"/>/
    /// <see cref="Conclusion"/>) ustiga QO'SHILADI — ularni ALMASHTIRMAYDI,
    /// shuning uchun bo'sh bo'lishi ham NORMAL (eski, ballashsiz tahlillar
    /// shunday qoladi).
    /// </summary>
    public ICollection<SessionReviewScore> Scores { get; set; } = new List<SessionReviewScore>();

    // ---------------------------------------------------------------- hisoblanuvchi

    /// <summary>
    /// Xulosa chiqarilganmi (qoralama emasmi). Ro'yxatdagi nishon shunga
    /// qarab "Ko'rilmagan" yoki haqiqiy holatni ko'rsatadi.
    /// </summary>
    public bool IsDecided => Verdict != SessionReviewVerdict.NotReviewed;

    /// <summary>Barcha mezonlar bo'yicha yig'ilgan ball.</summary>
    public decimal TotalScore => Scores.Sum(s => s.Score);

    /// <summary>Barcha mezonlar bo'yicha maksimal ball.</summary>
    public decimal TotalMaxScore => Scores.Sum(s => s.MaxScore);

    /// <summary>
    /// Foiz (0 mezon bo'yicha ballansa — <c>null</c>, "0%" bilan
    /// aralashtirmaslik uchun: ikkalasi ham UI'da BOSHQA-BOSHQA ko'rinishi
    /// kerak, "ballanmagan" va "hamma narsaga 0 qo'yilgan" bir xil emas).
    /// </summary>
    public decimal? ScorePercent =>
        TotalMaxScore > 0 ? Math.Round(TotalScore / TotalMaxScore * 100m, 1) : null;

    // ---------------------------------------------------------------- xatti-harakat

    public static SessionReview Create(
        long sessionId,
        long authorId,
        SessionReviewVerdict verdict,
        string? plus,
        string? minus,
        string? conclusion,
        DateTimeOffset now) =>
        new()
        {
            SessionId = sessionId,
            AuthorId = authorId,
            Verdict = verdict,
            Plus = NormalizeOptional(plus),
            Minus = NormalizeOptional(minus),
            Conclusion = RequireConclusion(conclusion),
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
    ///
    /// ★ TO'LIQ ALMASHTIRISH: <paramref name="plus"/>/<paramref name="minus"/>
    /// berilmasa avvalgi qiymat O'CHADI (`LessonGrade.Apply`/
    /// `SessionReview.Edit`(eski) dagi "saqlab qol" emas, "to'liq yozib
    /// qo'y" qoidasi) — noto'g'ri yozilgan bo'limni olib tashlashning
    /// yagona yo'li shu bo'lsin.
    /// </summary>
    public void Edit(
        SessionReviewVerdict verdict, string? plus, string? minus, string? conclusion,
        DateTimeOffset now)
    {
        // ★ AVVAL TEKSHIRUV, KEYIN O'ZGARTIRISH. Tartib teskari bo'lsa
        //   bo'sh matn bilan yuborilgan so'rov istisno tashlab, LEKIN
        //   xulosani allaqachon o'zgartirgan bo'lardi — ya'ni rad etilgan
        //   so'rov obyektni yarim o'zgargan holda qoldirardi.
        var conclusionValue = RequireConclusion(conclusion);
        var plusValue = NormalizeOptional(plus);
        var minusValue = NormalizeOptional(minus);

        Verdict = verdict;
        Plus = plusValue;
        Minus = minusValue;
        Conclusion = conclusionValue;
        UpdatedAt = now;
    }

    /// <summary>
    /// Mezon ballarini TO'LIQ ALMASHTIRADI (<c>LessonGrade.Apply</c> dagi
    /// AYNI "saqlab qol" emas, "to'liq yozib qo'y" qoidasi): oldingi
    /// baholashda bo'lgan-u yangisida yo'q mezon jimgina qolib ketmasin.
    ///
    /// ★ HAR BIR YOZUV KATALOGDAN <paramref name="catalog"/> orqali
    /// hal qilinadi — klient yuborgan nom/maksimal ballga ISHONILMAYDI,
    /// aks holda o'quv bo'limi mezon katalogini chetlab o'tib ixtiyoriy
    /// shkalada ball qo'ya olardi.
    /// </summary>
    /// <exception cref="DomainException">
    /// Noma'lum <c>criterionId</c> yuborilgan yoki ball chegaradan chiqqan.
    /// </exception>
    public void SetScores(
        IReadOnlyCollection<(long CriterionId, decimal Score)> scores,
        IReadOnlyDictionary<long, AnalysisCriterion> catalog,
        DateTimeOffset now)
    {
        Scores.Clear();

        foreach (var (criterionId, score) in scores)
        {
            if (!catalog.TryGetValue(criterionId, out var criterion))
                throw new DomainException("Noma'lum mezon.");

            Scores.Add(SessionReviewScore.Create(criterion.Id, criterion.Name, criterion.MaxScore, score));
        }

        UpdatedAt = now;
    }

    private static string RequireConclusion(string? conclusion)
    {
        var value = conclusion?.Trim();

        if (string.IsNullOrEmpty(value))
            throw new DomainException("Xulosa va yechimlar bo'sh bo'lishi mumkin emas.");

        if (value.Length > MaxSectionLength)
            throw new DomainException($"Xulosa {MaxSectionLength} belgidan oshmasin.");

        return value;
    }

    /// <summary><see cref="Plus"/>/<see cref="Minus"/> uchun: bo'sh matn — <c>null</c> (maydon yo'q).</summary>
    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed)) return null;

        if (trimmed.Length > MaxSectionLength)
            throw new DomainException($"Matn {MaxSectionLength} belgidan oshmasin.");

        return trimmed;
    }
}
