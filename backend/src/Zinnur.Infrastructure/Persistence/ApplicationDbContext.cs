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

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<CourseModule> Modules => Set<CourseModule>();

    public DbSet<ModuleLesson> ModuleLessons => Set<ModuleLesson>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();

    public DbSet<LiveSession> LiveSessions => Set<LiveSession>();

    public DbSet<Attendance> Attendances => Set<Attendance>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    // ---------------------------------------------------------------- FAZA 3: o'quv jarayoni

    public DbSet<Assignment> Assignments => Set<Assignment>();

    public DbSet<Submission> Submissions => Set<Submission>();

    public DbSet<SubmissionFile> SubmissionFiles => Set<SubmissionFile>();

    public DbSet<Test> Tests => Set<Test>();

    public DbSet<TestQuestion> TestQuestions => Set<TestQuestion>();

    public DbSet<TestOption> TestOptions => Set<TestOption>();

    public DbSet<TestAttempt> TestAttempts => Set<TestAttempt>();

    public DbSet<TestAnswer> TestAnswers => Set<TestAnswer>();

    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();

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

        // PUL: hozircha domain'da pul maydoni yo'q, lekin qachon paydo bo'lsa —
        // `double` emas, `numeric(18,2)` bo'lishi kafolatlansin (SPEC 9.6).
        // Konvensiya sifatida bir marta yozildi, har joyda takrorlanmaydi (DRY).
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
