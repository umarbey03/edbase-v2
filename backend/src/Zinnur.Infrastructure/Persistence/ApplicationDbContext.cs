using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence;

/// <summary>
/// <see cref="IApplicationDbContext"/> port'ining EF Core amalga oshirilishi.
/// Application qatlami bu sinfni KO'RMAYDI — faqat interfeysni biladi.
/// </summary>
/// <remarks>
/// Jadval/ustun nomlari EF standarti bo'yicha PascalCase'da qoldirilgan
/// (snake_case majburiy emas). Postgres identifikatorlarni tirnoq ichida
/// yozgani uchun bu to'liq ishlaydi; muhimi — BIR XIL uslub.
/// </remarks>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users => Set<User>();

    // ---------------------------------------------------------------- WAVE 1: o'quvchi profili

    public DbSet<StudentNote> StudentNotes => Set<StudentNote>();

    public DbSet<TelegramUnlinkAudit> TelegramUnlinkAudits => Set<TelegramUnlinkAudit>();

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<CourseModule> Modules => Set<CourseModule>();

    public DbSet<ModuleLesson> ModuleLessons => Set<ModuleLesson>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();

    public DbSet<LiveSession> LiveSessions => Set<LiveSession>();

    public DbSet<Attendance> Attendances => Set<Attendance>();

    public DbSet<AttendanceAudit> AttendanceAudits => Set<AttendanceAudit>();

    public DbSet<LessonGrade> LessonGrades => Set<LessonGrade>();

    public DbSet<LessonGradeAudit> LessonGradeAudits => Set<LessonGradeAudit>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();

    public DbSet<DirectMessageAttachment> DirectMessageAttachments => Set<DirectMessageAttachment>();

    // ---------------------------------------------------------------- FAZA 6: guruh chati

    public DbSet<GroupChatMessage> GroupChatMessages => Set<GroupChatMessage>();

    public DbSet<GroupChatRead> GroupChatReads => Set<GroupChatRead>();

    public DbSet<GroupChatAttachment> GroupChatAttachments => Set<GroupChatAttachment>();

    // ---------------------------------------------------------------- FAZA 3: o'quv jarayoni

    public DbSet<Assignment> Assignments => Set<Assignment>();

    public DbSet<Submission> Submissions => Set<Submission>();

    public DbSet<SubmissionFile> SubmissionFiles => Set<SubmissionFile>();

    public DbSet<SubmissionFeedbackFile> SubmissionFeedbackFiles => Set<SubmissionFeedbackFile>();

    public DbSet<Test> Tests => Set<Test>();

    public DbSet<TestQuestion> TestQuestions => Set<TestQuestion>();

    public DbSet<TestOption> TestOptions => Set<TestOption>();

    public DbSet<TestAttempt> TestAttempts => Set<TestAttempt>();

    public DbSet<TestAnswer> TestAnswers => Set<TestAnswer>();

    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();

    // ---------------------------------------------------------------- WAVE 1: dars mediasi

    public DbSet<LessonAsset> LessonAssets => Set<LessonAsset>();

    public DbSet<AssignmentAttachment> AssignmentAttachments => Set<AssignmentAttachment>();

    // ---------------------------------------------------------------- FAZA 4: moliya

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Tariff> Tariffs => Set<Tariff>();

    public DbSet<StudentDiscount> StudentDiscounts => Set<StudentDiscount>();

    public DbSet<StudentAccount> StudentAccounts => Set<StudentAccount>();

    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    public DbSet<PaymentAudit> PaymentAudits => Set<PaymentAudit>();

    public DbSet<LessonCharge> LessonCharges => Set<LessonCharge>();

    public DbSet<TeacherRate> TeacherRates => Set<TeacherRate>();

    public DbSet<SessionPayout> SessionPayouts => Set<SessionPayout>();

    public DbSet<PayrollApproval> PayrollApprovals => Set<PayrollApproval>();

    public DbSet<PayrollAdjustment> PayrollAdjustments => Set<PayrollAdjustment>();

    // ---------------------------------------------------------------- FAZA 5.3: dars yozuvi

    public DbSet<SessionRecording> SessionRecordings => Set<SessionRecording>();

    /// <summary>
    /// Ish jarayonida o'zgartiriladigan sozlamalar (bloklash chegarasi va
    /// qamrovi). <c>IApplicationDbContext</c> da ATAYLAB YO'Q: Application
    /// qatlami bu jadvalni bilmaydi va sozlamani port orqali oladi
    /// (<c>IFinanceSettingsStore</c>) — sabab <see cref="AppSetting"/> da.
    /// </summary>
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    /* ===== R35/R36 · ILOVA ICHIDAGI BILDIRISHNOMA =====

       ★ NEGA ALOHIDA BLOK: bu faylga bir necha tarmoq AYNI vaqtda qo'shadi
       (R24, R5/R29/R30, R21b/R38). Mavjud bo'limlar orasiga qistirilgan
       qator merge paytida to'qnashuv beradi, uzluksiz blok esa bermaydi. */

    /// <summary>
    /// Qo'ng'iroqcha ro'yxati. <see cref="MessageOutbox"/> DAN FARQLI o'laroq
    /// <c>IApplicationDbContext</c> DA OCHIQ — sabab <see cref="Notification"/>
    /// sinfi izohida: bu YETKAZIB BERISH mexanizmi emas, o'quvchi ekranda
    /// ko'radigan BIZNES ma'lumoti (<see cref="SessionRecordings"/> bilan
    /// bir xil mezon).
    /// </summary>
    public DbSet<Notification> Notifications => Set<Notification>();

    /* ===== /R35/R36 ===== */

    /* ===== R29/R30 · DARS SIFATI TAHLILI =====

       Yuqoridagi blok qoidasi bilan AYNI sabab (parallel tarmoqlar). */

    /// <summary>
    /// O'quv bo'limining dars sifati xulosasi. DARSGA bog'langan, yozuvga
    /// emas — sabab <see cref="SessionReview"/> sinfi izohida.
    /// </summary>
    public DbSet<SessionReview> SessionReviews => Set<SessionReview>();

    public DbSet<SessionReviewScore> SessionReviewScores => Set<SessionReviewScore>();

    public DbSet<AnalysisCriterion> AnalysisCriteria => Set<AnalysisCriterion>();

    /* ===== /R29/R30 ===== */

    /* ===== R21b · GURUH KATEGORIYASI =====

       Yuqoridagi bloklar qoidasi bilan AYNI sabab (parallel tarmoqlar). */

    /// <summary>
    /// O'quv yo'nalishlari lug'ati ("ATF", "Grammatika", "CEFR", "IELTS").
    /// <see cref="Course"/> bilan chegarasi — <see cref="GroupCategory"/>
    /// sinfi izohida.
    /// </summary>
    public DbSet<GroupCategory> GroupCategories => Set<GroupCategory>();

    /* ===== /R21b ===== */

    /* ===== 2026-08-16: "Xabarlar" paneli ===== */

    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();

    public DbSet<GroupBroadcast> GroupBroadcasts => Set<GroupBroadcast>();

    /* ===== /2026-08-16 ===== */

    /* ===== 2026-08-16: To'lov (dars-asosida) + bayram kalendari ===== */

    public DbSet<Holiday> Holidays => Set<Holiday>();

    /* ===== /2026-08-16 (to'lov) ===== */

    // ---------------------------------------------------------------- FAZA 5.2: notifikatsiya

    /// <summary>
    /// Yuborilishi kerak bo'lgan xabarlar navbati (transactional outbox).
    ///
    /// <c>IApplicationDbContext</c> da ATAYLAB YO'Q — <see cref="AppSetting"/>
    /// bilan bir xil sabab: use-case'lar navbatga <c>INotificationOutbox</c>
    /// porti orqali yozadi va jadval borligini bilmaydi. Yozuv esa AYNI
    /// kuzatuvchida to'planadi, ya'ni biznes o'zgarishi bilan BITTA
    /// tranzaksiyada saqlanadi (commit-then-send).
    /// </summary>
    public DbSet<MessageOutbox> MessageOutbox => Set<MessageOutbox>();

    // ---------------------------------------------------------------- FAZA 5.1: Telegram

    /// <summary>
    /// Ishlangan Telegram yangilanishlari (takrorga qarshi jurnal).
    ///
    /// <c>IApplicationDbContext</c> da ATAYLAB YO'Q — <see cref="MessageOutbox"/>
    /// bilan bir xil sabab: bu biznes ma'lumoti emas, yetkazib berish
    /// mexanizmi. Use-case'lar unga <c>ITelegramUpdateLog</c> porti orqali
    /// tegadi, yozuv esa AYNI kuzatuvchida to'planib, bog'lash va javob
    /// xabari bilan BITTA tranzaksiyada saqlanadi.
    /// </summary>
    public DbSet<TelegramUpdate> TelegramUpdates => Set<TelegramUpdate>();

    // ---------------------------------------------------------------- FAZA 5.3: dars yozuvi

    /// <summary>
    /// Ishlangan LiveKit hodisalari (takrorga qarshi jurnal).
    ///
    /// <c>IApplicationDbContext</c> da ATAYLAB YO'Q — <see cref="TelegramUpdates"/>
    /// bilan AYNI sabab: bu yetkazib berish mexanizmi. Use-case unga
    /// <c>ILiveKitWebhookLog</c> porti orqali tegadi, yozuv esa yozuv
    /// holatining o'zgarishi bilan BITTA tranzaksiyada saqlanadi.
    /// </summary>
    public DbSet<RecordingWebhookEvent> RecordingWebhookEvents => Set<RecordingWebhookEvent>();

    // ---------------------------------------------------------------- 2026-08-17: ustoz kunlik tasdiqlash + o'rinbosar

    public DbSet<TeacherDailyCheckin> TeacherDailyCheckins => Set<TeacherDailyCheckin>();

    public DbSet<TeacherCheckinAffectedSession> TeacherCheckinAffectedSessions => Set<TeacherCheckinAffectedSession>();

    public DbSet<SessionCoverageRequest> SessionCoverageRequests => Set<SessionCoverageRequest>();

    public DbSet<SubstituteOffer> SubstituteOffers => Set<SubstituteOffer>();

    public DbSet<GroupMembershipEvent> GroupMembershipEvents => Set<GroupMembershipEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Har entity uchun alohida IEntityTypeConfiguration<T> — bitta ulkan
        // OnModelCreating o'rniga (SRP). Yangi entity qo'shilsa fayl qo'shiladi,
        // shu metod o'zgarmaydi.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // PUL: har `decimal` ustun `numeric(18,2)` bo'ladi, `double` EMAS (SPEC 9.6).
        // Konvensiya sifatida bir marta yozildi — yangi pul maydoni qo'shilganda
        // aniqlikni belgilash UNUTILSA ham tur to'g'ri qoladi. Moliya
        // konfiguratsiyalari buni `HasPrecision` bilan OSHKOR takrorlaydi:
        // pul ustunining turi konfiguratsiya faylida ko'rinib tursin.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // VAQT: barcha DateTimeOffset -> `timestamptz`. Npgsql bu turga faqat
        // UTC (offset = 0) qiymatni qabul qiladi — mahalliy vaqt yozilsa
        // darhol xato beradi. Bu ataylab: SPEC 9.6 UTC talab qiladi.
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamptz");
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// <c>CreatedAt</c> / <c>UpdatedAt</c> ni AVTOMATIK to'ldiradi.
    ///
    /// NIMA UCHUN SHU YERDA: eski tizimda bu har servisda qo'lda yozilardi va
    /// yarim joyda unutilardi — natijada "oxirgi o'zgarish" ustuni ishonchsiz edi.
    /// Bitta joyda bo'lgani uchun endi unutish IMKONSIZ.
    ///
    /// <c>SaveChangesAsync(ct)</c> ichkarida <c>SaveChangesAsync(true, ct)</c> ni
    /// chaqiradi, shuning uchun faqat shu ikkita overload yetarli.
    /// </summary>
    private void ApplyAuditTimestamps()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                    entry.Entity.CreatedAt = now;

                entry.Entity.UpdatedAt = null;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;

                // CreatedAt tarix — klient uni yuborsa ham bazadagi qiymat qoladi.
                entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
            }
        }
    }
}
