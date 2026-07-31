using NpgsqlTypes;
using Zinnur.Migration.Pipeline;

namespace Zinnur.Migration.Plan;

/// <summary>
/// ========================================================================
/// KO'CHIRISH REJASI — QAYSI JADVAL QAYERGA, QANDAY TARTIBDA
/// ========================================================================
///
/// ★ TARTIB TASODIFIY EMAS, BOG'LIQLIK GRAFI:
///     foydalanuvchi -> balans
///     kurs -> modul -> kurs darsi
///     guruh -> a'zolik
///     jonli dars -> davomat
///     chat (guruh oqimi, shaxsiy yozishma)
///     vazifa -> javob -> fayl
///     test -> savol -> variant -> urinish -> javob
///     tarif / chegirma -> to'lov -> moliya jurnali -> audit
/// Har bola jadval otasining ID'sini <see cref="MigrationState"/> dan
/// tekshiradi: ota ko'chmagan bo'lsa bola ham o'tkazib yuboriladi va
/// sababi hisobotga tushadi. Shu tufayli FK xatosi bilan yiqilish
/// PRINSIPIAL ravishda mumkin emas.
///
/// ★ ESKI ID'LAR SAQLANADI. Sabablari (batafsil hujjatda):
///   1. Tashqi havolalar ishlashda davom etadi — R2 obyekt kalitlari,
///      Telegram chuqur havolalari, chop etilgan kvitansiyalar;
///   2. Idempotentlik BEPUL bo'ladi: "bu qator ko'chganmi?" degan savolga
///      birlamchi kalitning o'zi javob beradi (<c>ON CONFLICT DO NOTHING</c>),
///      alohida xarita jadvali kerak emas;
///   3. Tekshiruv oddiylashadi: manba va maqsaddagi ID to'plamlari AYNAN
///      solishtiriladi, ya'ni "qaysi qator yo'qolgan" degan savolga
///      aniq javob bor.
/// Narxi: identity ketma-ketliklarini to'g'rilash SHART
/// (<see cref="IdentitySequences"/>).
/// </summary>
internal static class MigrationPlan
{
    /// <summary>
    /// Eski <c>submissions.file_url</c> ustunidan hosil qilinadigan
    /// <c>SubmissionFiles</c> qatorlari uchun ID siljishi.
    ///
    /// NIMA UCHUN: bu qatorlarning eski jadvalda O'Z ID'si yo'q — ular
    /// <c>submissions</c> qatorining bir ustunidan tug'iladi. ID sifatida
    /// <c>submission_id</c> ni olsa <c>submission_files</c> ning haqiqiy
    /// ID'lari bilan URIShardi. Siljish (10^12) ikki fazoni ajratadi va
    /// hisoblash DETERMINISTIK bo'lgani uchun qayta yurgizishda AYNI
    /// ID hosil bo'ladi — ya'ni idempotentlik saqlanadi.
    /// </summary>
    public const long FileUrlIdOffset = 1_000_000_000_000L;

    /// <summary>
    /// Eski bazada MAVJUD BO'LMAGAN <c>created_at</c> qiymatlari uchun
    /// yagona zaxira vaqt (butun yurish davomida BIR XIL).
    ///
    /// Bu TAXMIN QILINGAN qiymat va hisobotda shunday belgilanadi:
    /// <c>modules</c>, <c>module_lessons</c>, <c>test_questions</c>,
    /// <c>test_options</c>, <c>group_members</c> (qisman) jadvallarida
    /// yaratilish vaqti umuman saqlanmagan.
    /// </summary>
    public static DateTimeOffset Fallback { get; } = DateTimeOffset.UtcNow;

    // ---------------------------------------------------------------- ustun qisqartmalari

    public static TargetColumn Id(string name = "Id") => new(name, NpgsqlDbType.Bigint);

    public static TargetColumn Ref(string name) => new(name, NpgsqlDbType.Bigint);

    public static TargetColumn Num(string name) => new(name, NpgsqlDbType.Integer);

    public static TargetColumn Str(string name) => new(name, NpgsqlDbType.Varchar);

    public static TargetColumn Flag(string name) => new(name, NpgsqlDbType.Boolean);

    public static TargetColumn Money(string name) => new(name, NpgsqlDbType.Numeric);

    /// <summary>Pul BO'LMAGAN o'nlik son (ball, foiz). Turi ayni, ma'nosi boshqa.</summary>
    public static TargetColumn Dec(string name) => new(name, NpgsqlDbType.Numeric);

    /// <summary>Havola bo'lmagan <c>bigint</c> (masalan fayl hajmi).</summary>
    public static TargetColumn Big(string name) => new(name, NpgsqlDbType.Bigint);

    public static TargetColumn Moment(string name) => new(name, NpgsqlDbType.TimestampTz);

    public static TargetColumn Day(string name) => new(name, NpgsqlDbType.Date);

    public static TargetColumn Clock(string name) => new(name, NpgsqlDbType.Time);

    public static TargetColumn IntArray(string name) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Integer);

    /// <summary>Ko'chirish tartibidagi to'liq reja.</summary>
    public static IReadOnlyList<TableSpec> Build() =>
    [
        // --- 1-halqa: mustaqil ma'lumot -----------------------------------
        Core.Users(),
        Core.StudentAccounts(),
        Core.Courses(),
        Core.Modules(),
        Core.ModuleLessons(),
        Core.Groups(),
        Core.GroupMembers(),

        // --- 2-halqa: jonli dars va davomat -------------------------------
        Core.LiveSessions(),
        Core.Attendances(),

        // --- 3-halqa: yozishmalar -----------------------------------------
        Core.GroupChatMessages(),
        Core.DirectMessages(),

        // --- 4-halqa: o'quv jarayoni --------------------------------------
        Learning.Assignments(),
        Learning.Submissions(),
        Learning.SubmissionFiles(),
        Learning.SubmissionLegacyFileUrls(),
        Learning.LessonProgress(),
        Learning.Tests(),
        Learning.TestQuestions(),
        Learning.TestOptions(),
        Learning.TestAttempts(),
        Learning.TestAnswers(),

        // --- 5-halqa: moliya ----------------------------------------------
        Finance.Tariffs(),
        Finance.StudentDiscounts(),
        Finance.Payments(),
        Finance.PaymentTransactions(),
        Finance.PaymentAudits(),
    ];
}
