using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// O'QUVCHINING BITTA DARS UCHUN BAHOSI (R24)
/// ========================================================================
///
/// (SessionId, StudentId) — UNIKAL, ya'ni bitta darsda bitta o'quvchiga
/// BITTA baho.
///
/// ── 🔴 NIMA UCHUN `Submission` QAYTA ISHLATILMADI ──────────────────────
///
/// v2 da baho HAR DOIM topshiriqqa bog'langan edi: <c>Submission.Score</c>,
/// va <c>Submission</c> — O'QUVCHI TOPSHIRGAN ISH. Uni yaratish
/// <c>SubmittedAt</c>, <c>AttemptNumber</c> va <c>IsLate</c> ni to'ldirishni
/// TALAB qiladi. Ustoz "bugungi darsga 5" deb qo'yganda esa topshirilgan
/// ISH YO'Q. Soxta topshiriq yasash quyidagilarni jimgina buzardi:
///
///   • <c>AssignmentDto.SubmissionCount</c> / <c>GradedCount</c> —
///     "25 topshirdi" deyilardi, aslida 25 ta baho qo'yilgan;
///   • baholash navbatidagi "kutayotganlar" sanog'i;
///   • reytingdagi <c>AssignmentPercent</c> — dars bahosi vazifa bahosi
///     bo'lib ko'rinardi.
///
/// ── NIMA UCHUN AYNAN `Attendance` SHAKLI ──────────────────────────────
///
/// Dars bahosi <see cref="Attendance"/> bilan bir xil savolga javob
/// beradi: "BITTA dars × BITTA o'quvchi". Shakl bir xil bo'lgani uchun
/// ustoz paneli ham bir xil: matritsa (qator — o'quvchi, ustun — dars),
/// katakni bosish -> oyna, tuzatish izi audit jadvalida. Ustoz ikkita
/// BOSHQA-BOSHQA jadvalni o'rganmaydi.
///
/// ── FARQI ─────────────────────────────────────────────────────────────
///
/// Davomatni PLATFORMA o'lchaydi, odam esa faqat TUZATADI — shuning uchun
/// u yerda <c>IsManual</c> bayrog'i bor. Bu yerda o'lchov UMUMAN YO'Q:
/// har bir qiymat — odamning qarori. Shu sababli <c>IsManual</c> ham
/// yo'q, uning o'rniga har qatorda <see cref="GradedById"/> va
/// <see cref="GradedAt"/> turadi (OXIRGI qaror), to'liq tarix esa
/// <see cref="LessonGradeAudit"/> da.
/// </summary>
public class LessonGrade : BaseEntity
{
    /// <summary>
    /// <see cref="MaxScore"/> ko'rsatilmaganda ishlatiladigan shkala.
    ///
    /// 5 — o'quv markazlarining kundalik shkalasi ("bugungi darsga 5").
    /// Ustoz oynada maksimal ballni umuman tanlamasligi mumkin va shunda
    /// baho AYNAN shu shkalada o'qiladi.
    ///
    /// 🔴 BU SON BAZADAGI `CK_LessonGrades_Score` CHECK'ida HAM yozilgan
    /// (<c>LessonGradeConfiguration</c>) — Infrastructure Domain doimiysiga
    /// SQL satri ichidan havola qila olmaydi. Ikkalasi BIRGA o'zgartiriladi,
    /// aks holda baza kod ruxsat bergan qiymatni rad etadi.
    /// </summary>
    public const decimal DefaultMaxScore = 5m;

    /// <summary>
    /// Izoh uzunligi — bir-ikki jumla ("darsda faol qatnashdi", "uy ishini
    /// qilmagan"). Uzun matn uchun joy emas: izoh matritsadagi katakning
    /// maslahatida va oynada ko'rsatiladi.
    /// </summary>
    public const int MaxCommentLength = 500;

    public long SessionId { get; set; }

    public LiveSession? Session { get; set; }

    public long StudentId { get; set; }

    public User? Student { get; set; }

    /// <summary>Qo'yilgan ball. 0 — HAQIQIY baho ("bajarmadi"), "yozuv yo'q" emas.</summary>
    public decimal Score { get; set; }

    /// <summary>
    /// Shu darsning maksimal bali. <c>null</c> — standart shkala
    /// (<see cref="DefaultMaxScore"/>).
    ///
    /// NIMA UCHUN NULLABLE: ustozlarning aksariyati 5 ballik shkalada
    /// ishlaydi va oynada har safar "maksimal ball" ni tanlash ortiqcha
    /// qadam bo'lardi. Imtihon darsida esa 100 ballik shkala kerak —
    /// shuning uchun maydon bor, lekin MAJBURIY emas.
    ///
    /// ★ HAR QATORDA SAQLANADI, darsda emas: ustoz bitta darsda ham
    /// turli mezon ishlatishi mumkin va eng muhimi — shkala keyin
    /// o'zgartirilsa ALLAQACHON QO'YILGAN baholarning ma'nosi
    /// o'zgarmasligi kerak (5/5 keyinchalik 5/100 ga aylanib qolmasin).
    /// </summary>
    public decimal? MaxScore { get; set; }

    /// <summary>Ustozning izohi (ixtiyoriy).</summary>
    public string? Comment { get; set; }

    /// <summary>Bahoni OXIRGI marta qo'ygan xodim.</summary>
    public long GradedById { get; set; }

    public User? GradedBy { get; set; }

    /// <summary>OXIRGI baholash vaqti (UTC).</summary>
    public DateTimeOffset GradedAt { get; set; }

    // ---------------------------------------------------------------- hisoblanuvchi

    /// <summary>Amaldagi maxraj — <see cref="MaxScore"/> yoki standart shkala.</summary>
    public decimal EffectiveMaxScore => MaxScore ?? DefaultMaxScore;

    /// <summary>
    /// Baho foizi (reyting uchun) — <c>Submission.ScorePercent</c> bilan
    /// AYNI yaxlitlash qoidasida, ya'ni ikki mezon bir xil o'qiladi.
    /// </summary>
    public decimal Percent =>
        EffectiveMaxScore > 0 ? Math.Round(Score / EffectiveMaxScore * 100m, 1) : 0m;

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// Bahoni QO'YADI yoki QAYTA yozadi (upsert'ning ikkinchi yarmi).
    ///
    /// ★ TO'LIQ ALMASHTIRISH: izoh berilmasa avvalgi izoh O'CHADI. Sabab
    /// <c>Attendance.ApplyManual</c> dagi bilan bir xil — "saqlab qol"
    /// ma'nosi bo'lsa, noto'g'ri izohni olib tashlashning yo'li bo'lmasdi.
    ///
    /// 🔴 INVARIANT SHU YERDA HAM TEKSHIRILADI, servisda ham. Servisdagi
    /// tekshiruv foydalanuvchiga aniq 400 va maydon nomini beradi; bu
    /// yerdagi esa BOSHQA yo'ldan (ma'lumot ko'chirish, kelajakdagi yangi
    /// servis) kelgan buzuq qiymatni to'xtatadi. Foiz 100 dan oshib ketsa
    /// reytingdagi "0..100" invarianti buzilardi va yakuniy ball 100 dan
    /// katta chiqardi.
    /// </summary>
    /// <exception cref="DomainException">Ball manfiy, maxraj musbat emas yoki ball maxrajdan katta.</exception>
    public void Apply(
        decimal score, decimal? maxScore, string? comment, long graderId, DateTimeOffset now)
    {
        if (maxScore is { } max && max <= 0)
            throw new DomainException("Maksimal ball noldan katta bo'lishi kerak.");

        if (score < 0)
            throw new DomainException("Baho manfiy bo'lmaydi.");

        var effective = maxScore ?? DefaultMaxScore;

        if (score > effective)
            throw new DomainException($"Baho maksimal balldan ({effective}) oshmasin.");

        if (comment?.Length > MaxCommentLength)
            throw new DomainException($"Izoh {MaxCommentLength} belgidan oshmasin.");

        Score = score;
        MaxScore = maxScore;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        GradedById = graderId;
        GradedAt = now;
        UpdatedAt = now;
    }
}
