using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Media;
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
    /// <summary>Birinchi ishga tushirishdagi admin (kontakt/identifikator sifatida).</summary>
    public const string AdminEmail = "admin@zinnur.uz";

    /// <summary>
    /// ⚠️ ENDI KIRISH VOSITASI EMAS. 2026-08-13 dan email va parol bilan
    /// kirish olib tashlandi; bu qiymat faqat <c>PasswordHash</c>
    /// ustunini to'ldirish uchun qoldi (ustun <c>required</c>, sabab
    /// <c>User.PasswordHash</c> izohida). Uni bilgan odam HECH QAYERGA
    /// kira olmaydi — kirish endi telefon + Telegram kodi bilan.
    /// </summary>
    public const string AdminPassword = "Admin!2345";

    private const string DemoPassword = "Demo!2345";

    // ════════════════════════════════════════════════════════════════════
    // 🔴 BOSHLANG'ICH ADMIN TELEFONI — YANGI O'RNATISHNING YAGONA KALITI
    //
    // Email va parol bilan kirish olib tashlangach, telefonsiz va
    // Telegram'siz yaratilgan admin BUTUNLAY ERISHIB BO'LMAYDIGAN bo'lib
    // qoladi: kirish uchun raqam kerak, raqamni kiritish uchun esa
    // tizimga kirish kerak. Ya'ni bo'sh bazaga qurilgan yangi deploy
    // HECH QANDAY administratorsiz ishga tushardi va uni faqat `psql`
    // bilan tuzatish mumkin bo'lardi.
    //
    // Shuning uchun raqam MUHITDAN olinadi va u yo'q bo'lsa seeding
    // TO'XTATILADI (Development'dan tashqarida).
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Muhit o'zgaruvchisi: <c>Bootstrap__AdminPhone</c>.</summary>
    public const string AdminPhoneKey = "Bootstrap:AdminPhone";

    /// <summary>
    /// Muhit o'zgaruvchisi: <c>Bootstrap__AdminTelegramId</c> — IXTIYORIY.
    ///
    /// ★ NIMA UCHUN IXTIYORIY: raqam yetarli. Admin botga
    /// «📱 Raqamni ulashish» tugmasi orqali bog'lanadi va Telegram ID
    /// o'sha yerda yoziladi. Oldindan berish faqat bitta qadamni
    /// tejaydi — LEKIN xato ID berilsa admin hisobi BOSHQA odamga
    /// bog'lanib qolardi, shuning uchun u majburiy emas va hujjatda
    /// tavsiya ham etilmaydi.
    /// </summary>
    public const string AdminTelegramIdKey = "Bootstrap:AdminTelegramId";

    /// <summary>
    /// Development uchun standart raqam. Prod'da ISHLATILMAYDI —
    /// u yerda o'zgaruvchi majburiy.
    /// </summary>
    public const string DevAdminPhone = "+998900000001";

    private const string DevTeacherPhone = "+998900000002";
    private const string DevStudentPhone = "+998900000003";

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
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DbInitializer));

        var bootstrap = BootstrapAdmin.Read(configuration, environment.IsDevelopment());

        await InitializeAsync(db, hasher, bootstrap, logger, ct).ConfigureAwait(false);

        // ════════════════════════════════════════════════════════════════
        // NAMUNAVIY (DEMO) MA'LUMOT — ALOHIDA, OSHKOR KALIT ORTIDA
        //
        // ★ NIMA UCHUN SHU YERDA, `InitializeAsync` ICHIDA EMAS: yuqoridagi
        //   overload testlar va migratsiya vositalari uchun ochiq. Demo
        //   ma'lumot esa TO'LIQ ilova kontekstini talab qiladi (ombor
        //   servislari, konfiguratsiya) va u yerda umuman kerak emas.
        //
        // 🔴 Kalit va uning uch qatlamli himoyasi — `DemoDataSeeder` izohida.
        // ════════════════════════════════════════════════════════════════
        if (!DemoDataSeeder.IsEnabled(configuration))
            return;

        // Ombor servislari IXTIYORIY: ular ro'yxatdan o'tmagan bo'lsa ham
        // namunaviy ma'lumotning 95% i (faylsiz qismi) baribir yoziladi.
        var media = scope.ServiceProvider.GetService<IMediaStorage>();
        var submissions = scope.ServiceProvider.GetService<ISubmissionStorage>();

        await DemoDataSeeder
            .SeedAsync(db, hasher, media, submissions, logger, ct)
            .ConfigureAwait(false);
    }

    /// <summary>Testlar va migratsiya vositalari uchun to'g'ridan-to'g'ri variant.</summary>
    public static async Task InitializeAsync(
        ApplicationDbContext db,
        IPasswordHasher hasher,
        BootstrapAdmin bootstrap,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(bootstrap);

        await ApplySchemaAsync(db, logger, ct).ConfigureAwait(false);
        await SeedAsync(db, hasher, bootstrap, logger, ct).ConfigureAwait(false);
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
        ApplicationDbContext db,
        IPasswordHasher hasher,
        BootstrapAdmin bootstrap,
        ILogger logger,
        CancellationToken ct)
    {
        // YAGONA shart: baza bo'sh bo'lsa. Bitta ham foydalanuvchi bo'lsa
        // demo ma'lumot yozilmaydi — prod bazasiga demo guruh tushib qolmasin.
        if (await db.Users.AnyAsync(ct).ConfigureAwait(false))
        {
            DbInitializerLog.SeedSkipped(logger);
            return;
        }

        // ══════════════════════════════════════════════════════════════
        // 🔴 TEKSHIRUV AYNAN SHU YERDA — "baza bo'sh" shartidan KEYIN.
        //
        // Tartib ATAYLAB shunday. Agar u yuqorida, `InitializeAsync`
        // boshida turganda, ISHLAB TURGAN har bir o'rnatish keyingi
        // qayta ishga tushishda YIQILARDI: ularda `Bootstrap__AdminPhone`
        // yo'q va kerak ham emas (administrator allaqachon bazada).
        // Ya'ni lockout'dan himoya qiladigan tekshiruvning O'ZI butun
        // platformani to'xtatib qo'yardi.
        //
        // Shart faqat "hozir birinchi administrator YARATILAYOTGAN
        // bo'lsa" ma'noga ega — va aynan o'shanda u qattiq.
        // ══════════════════════════════════════════════════════════════
        bootstrap.EnsureUsable();

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

        // 🔴 TELEFON — `SetPhone` ORQALI, qo'lda EMAS. Faqat shu metod
        //    `PhoneNormalized` ni to'ldiradi, kirish esa AYNAN o'sha
        //    ustun bo'yicha izlaydi. To'g'ridan-to'g'ri `Phone = "..."`
        //    yozilsa admin CRM'da normal ko'rinardi, lekin kirish uni
        //    hech qachon topa olmasdi — bu aynan migratsiya qoldirgan
        //    nosozlik turi.
        admin.SetPhone(bootstrap.AdminPhone);

        // Telegram ID berilgan bo'lsa — darhol bog'laymiz, aks holda
        // administrator botga bir marta raqamini ulashadi.
        if (bootstrap.AdminTelegramId is { } telegramId)
            admin.LinkTelegram(telegramId, username: null, now);

        var teacher = new User
        {
            FullName = "Demo Ustoz",
            Email = "teacher@zinnur.uz",
            PasswordHash = demoHash,
            Role = UserRole.Teacher,
        };

        teacher.SetPhone(DevTeacherPhone);

        var student = new User
        {
            FullName = "Demo O'quvchi",
            Email = "student@zinnur.uz",
            PasswordHash = demoHash,
            Role = UserRole.Student,
        };

        student.SetPhone(DevStudentPhone);

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

        DbInitializerLog.Seeded(logger, AdminEmail, bootstrap.AdminPhone ?? "-");
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

    /// <summary>
    /// ⚠️ MATN 2026-08-13 DA O'ZGARDI: "parolni almashtiring" maslahati
    /// endi ma'nosiz (parol bilan kirish yo'q) va u operatorni noto'g'ri
    /// ishga yo'naltirardi. Endi logda AYNAN kerakli fakt turadi —
    /// administrator qaysi RAQAM bilan kira oladi.
    /// </summary>
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Boshlang'ich ma'lumotlar yozildi. Admin: {Email}, telefon: {Phone}. "
                  + "Kirish uchun shu raqamni botga ulang (docs/DEPLOY_UBUNTU.md).")]
    internal static partial void Seeded(ILogger logger, string email, string phone);
}
