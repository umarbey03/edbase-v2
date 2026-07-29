using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Infrastructure.Persistence;

/// <summary>
/// Bazani ishga tayyorlaydi: sxemani qo'llaydi va (agar baza BO'SH bo'lsa)
/// boshlang'ich ma'lumotlarni yozadi.
///
/// IDEMPOTENT: istalgancha marta chaqirish mumkin. Konteyner qayta ishga
/// tushganda ham dublikat yozuv yaratilmaydi — shart bitta:
/// "Users jadvalida hech kim yo'q bo'lsa".
/// </summary>
public static class DbInitializer
{
    /// <summary>Birinchi ishga tushirishdagi admin. Kirgandan keyin parol ALMASHTIRILSIN.</summary>
    public const string AdminEmail = "admin@zinnur.uz";

    /// <summary>
    /// Faqat BO'SH bazadagi birinchi kirish uchun. Bu "sir" emas —
    /// SPEC 9.8 dagi "kodda sir bo'lmasin" qoidasi ishlab turgan tizim
    /// kalitlariga tegishli; bu esa ataylab ommaviy, bir martalik parol.
    /// </summary>
    public const string AdminPassword = "Admin!2345";

    private const string DemoPassword = "Demo!2345";

    /// <summary>DI konteyneridan kerakli xizmatlarni olib to'liq initsializatsiya qiladi.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        // DbContext — Scoped, shuning uchun root provider'dan to'g'ridan-to'g'ri
        // olinmaydi (aks holda u singleton'ga aylanib, ilova umri davomida
        // ochiq ulanish ushlab turardi).
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DbInitializer));

        await InitializeAsync(db, hasher, logger, ct).ConfigureAwait(false);
    }

    /// <summary>Testlar va migratsiya vositalari uchun to'g'ridan-to'g'ri variant.</summary>
    public static async Task InitializeAsync(
        ApplicationDbContext db,
        IPasswordHasher hasher,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(hasher);

        await ApplySchemaAsync(db, logger, ct).ConfigureAwait(false);
        await SeedAsync(db, hasher, logger, ct).ConfigureAwait(false);
    }

    private static async Task ApplySchemaAsync(
        ApplicationDbContext db, ILogger logger, CancellationToken ct)
    {
        // FAQAT migratsiyalar. `EnsureCreated` ATAYLAB ISHLATILMAYDI.
        //
        // NIMA UCHUN: `EnsureCreated` sxemani modeldan bir marta yaratadi va
        // `__EFMigrationsHistory` jadvalini YOZMAYDI. Natijada keyinchalik
        // birinchi migratsiya qo'llanganda EF "jadval allaqachon mavjud" deb
        // yiqiladi, yoki yomoni — sxema va migratsiya tarixi bir-biriga mos
        // kelmay qoladi. Ishlab chiqarish bazasida bu tuzatib bo'lmaydigan holat.
        //
        // Endi yagona yo'l: migratsiya. Sxema o'zgarsa yangi migratsiya
        // yaratiladi (docs/MIGRATIONS.md).
        var pending = (await db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).ToList();

        DbInitializerLog.PreparingSchema(logger, pending.Count > 0);

        if (pending.Count > 0)
            await db.Database.MigrateAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedAsync(
        ApplicationDbContext db, IPasswordHasher hasher, ILogger logger, CancellationToken ct)
    {
        // YAGONA shart: baza bo'sh bo'lsa. Bitta ham foydalanuvchi bo'lsa
        // demo ma'lumot yozilmaydi — prod bazasiga demo guruh tushib qolmasin.
        if (await db.Users.AnyAsync(ct).ConfigureAwait(false))
        {
            DbInitializerLog.SeedSkipped(logger);
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // BCrypt ~250 ms. Demo foydalanuvchilar uchun bitta hash qayta
        // ishlatiladi — ishga tushish vaqtini uch barobar cho'zmaslik uchun.
        var adminHash = await hasher.HashAsync(AdminPassword, ct).ConfigureAwait(false);
        var demoHash = await hasher.HashAsync(DemoPassword, ct).ConfigureAwait(false);

        var admin = new User
        {
            FullName = "Bosh administrator",
            Email = AdminEmail,
            PasswordHash = adminHash,
            Role = UserRole.Admin,
        };

        var teacher = new User
        {
            FullName = "Demo Ustoz",
            Email = "teacher@zinnur.uz",
            PasswordHash = demoHash,
            Role = UserRole.Teacher,
        };

        var student = new User
        {
            FullName = "Demo O'quvchi",
            Email = "student@zinnur.uz",
            PasswordHash = demoHash,
            Role = UserRole.Student,
        };

        db.Users.AddRange(admin, teacher, student);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var course = new Course
        {
            Name = "ATF",
            Description = "Demo kurs — boshlang'ich ma'lumot.",
            Position = 1,
            Modules =
            {
                new CourseModule
                {
                    Name = "Harf moduli",
                    Position = 1,
                    Lessons =
                    {
                        new ModuleLesson { Name = "Alif", Position = 1, DurationMin = 45 },
                    },
                },
            },
        };

        db.Courses.Add(course);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var group = new Group
        {
            Name = "ATF-1 (demo)",
            CourseId = course.Id,
            TeacherId = teacher.Id,
            StartDate = DateOnly.FromDateTime(now.UtcDateTime),
        };

        db.Groups.Add(group);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = group.Id,
            StudentId = student.Id,
            Status = MemberStatus.Active,
            JoinedAt = now,
        });

        // Dars 2 daqiqadan keyinga rejalashtiriladi.
        // Domain qoidasi: darsni `ScheduledStart - StartLeadMinutes(5)` dan
        // boshlab ochish mumkin. 2 < 5 bo'lgani uchun demo darsni SHU ONDA
        // boshlash mumkin — seeding'dan keyin tizimni darhol sinab ko'rish uchun.
        var start = now.AddMinutes(2);

        db.LiveSessions.Add(new LiveSession
        {
            GroupId = group.Id,
            HostId = teacher.Id,
            Title = "Demo dars",
            Type = SessionType.Teacher,
            Status = SessionStatus.Scheduled,
            ScheduledStart = start,
            ScheduledEnd = start.AddMinutes(80),
            RoomName = LiveSession.GenerateRoomName(),
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        DbInitializerLog.Seeded(logger, AdminEmail);
    }
}

/// <summary>
/// Manbadan generatsiya qilinadigan log metodlari (CA1848).
/// Klassik <c>logger.LogInformation($"...")</c> har chaqiruvda satr yasaydi
/// va bokslash qiladi; bu yerda esa log o'chirilgan bo'lsa hech qanday
/// allokatsiya bo'lmaydi.
/// </summary>
internal static partial class DbInitializerLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Baza sxemasi tayyorlanmoqda (migratsiyalar mavjud: {HasMigrations}).")]
    internal static partial void PreparingSchema(ILogger logger, bool hasMigrations);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Bazada ma'lumot bor — boshlang'ich yozuvlar o'tkazib yuborildi.")]
    internal static partial void SeedSkipped(ILogger logger);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Boshlang'ich ma'lumotlar yozildi. Admin: {Email} — PAROLNI DARHOL ALMASHTIRING.")]
    internal static partial void Seeded(ILogger logger, string email);
}
