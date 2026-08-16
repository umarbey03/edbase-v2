using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Finance;
using Zinnur.Domain.Staffing;

namespace Zinnur.Infrastructure.Persistence;

/// <summary>Log'da chiqadigan kirish ma'lumoti (bitta qator).</summary>
/// <param name="Role">Rol nomi (o'zbekcha).</param>
/// <param name="FullName">To'liq ism.</param>
/// <param name="Phone">Kirish uchun telefon raqami.</param>
/// <param name="TelegramId">Bog'langan (soxta) Telegram ID.</param>
internal sealed record DemoAccount(string Role, string FullName, string Phone, long TelegramId);

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// NAMUNAVIY MA'LUMOT — BITTA YAXLIT SSENARIY
/// ════════════════════════════════════════════════════════════════════════
///
/// Bu sinf tarqoq qatorlar yozmaydi. U BITTA o'quv markazini quradi:
/// bitta kurs, uning ichida modul va darslar, o'sha kursda o'qiydigan
/// to'rt guruh, o'sha guruhlarning jadvali, darslari, davomati, baholari,
/// vazifalari, to'lovlari va yozishmalari.
///
/// ★ NIMA UCHUN AYNAN SHUNDAY: tekshiruvchi ekranma-ekran yuradi va har
/// ekranda AYNI o'quvchilarni ko'radi. Tasodifiy generatsiya qilingan
/// ma'lumotda "Alisher" bir ekranda 95% davomatga ega, ikkinchisida umuman
/// yo'q bo'lardi — bu holatda ekranning O'ZI to'g'ri ishlayaptimi yoki
/// yo'qmi ayta olmaysiz.
///
/// ★ HAR BIR HOLAT ATAYLAB QO'YILGAN: davomatda beshta holat (jumladan
/// BELGILANMAGAN), vazifada to'rtta holat, to'lovda qarzdor VA chegara
/// ostidagi qarzdor. Bo'sh holat ham holat — filtr uni ko'rsatishi kerak.
///
/// ⚠️ SINF DOMEN QOIDALARINI CHETLAB O'TMAYDI: har joyda fabrika metodi
/// (<c>DirectMessage.Create</c>, <c>Submission.Grade</c>, <c>User.SetPhone</c>)
/// ishlatiladi. Aks holda seeder domen tekshirmaydigan holatlarni yozib,
/// UI'da hech qachon uchramaydigan ma'lumot yaratardi.
/// </summary>
internal sealed class DemoWorld(
    ApplicationDbContext db,
    DemoMediaSink media,
    string passwordHash,
    DateTimeOffset now)
{
    // ════════════════════════════════════════════════════════════════════
    // 🔴 SOXTA TELEGRAM ID DIAPAZONI — NIMA UCHUN AYNAN 7·10¹²
    //
    // Kirish uchun `TelegramId` SHART: kod aynan o'sha hisobga yuboriladi
    // (`PhoneLoginService`), bog'lanmagan profil esa JIMGINA rad etiladi.
    // Ya'ni ID'siz namunaviy foydalanuvchiga umuman kirib bo'lmaydi.
    //
    // Diapazon esa HAQIQIY Telegram ID'lari bilan KESISHMASLIGI kerak.
    // Bugungi haqiqiy foydalanuvchi ID'lari ~10¹⁰ atrofida; Telegram
    // protokoli 2⁵² gacha ruxsat beradi. 7·10¹² — ikkalasining orasida:
    // hali taqsimlanmagan, lekin formatga to'g'ri keladi.
    //
    // Agar oddiy kichik son (masalan 111111111) ishlatilsa, u ALLAQACHON
    // BIROVNIKI bo'lardi va namunaviy hisobga kirish kodi BEGONA ODAMGA
    // ketardi.
    //
    // ★ KODNI QAYERDAN OLASIZ: soxta ID'ga xabar yetib bormaydi. Kod
    //   `MessageOutbox` jadvalida qoladi (buyruq — hisobotda).
    //
    // 🔴 QIYMATNING O'ZI ENDI `DemoDataSeeder` DA (2026-08-14). Sabab:
    //    diapazon ikkinchi vazifani ham bajaradi — u "bu qator namunaviy"
    //    degan YAGONA ishonchli belgi va `DevQuickLoginService` aynan
    //    shunga tayanib haqiqiy markaz hisoblarini rad etadi. Ikki nusxa
    //    bo'lsa, biri o'zgarganda ikkinchisi jimgina "hamma qator
    //    namunaviy emas" deb qolardi.
    //
    // ⚠️ QUYIDA ISHLATILADIGAN ENG KATTA SILJISH — `+112`
    //    (12 o'quvchi, `TelegramBase + 101 + i`). U
    //    `DemoDataSeeder.DemoTelegramIdMaxExclusive` (=+1000) dan OSHMASLIGI
    //    SHART: oshsa, o'sha o'quvchi diapazondan chiqib ketadi va unga
    //    test kirishi jimgina ishlamay qo'yadi.
    // ════════════════════════════════════════════════════════════════════
    private const long TelegramBase = DemoDataSeeder.DemoTelegramIdMin;

    /// <summary>Namunaviy raqamlar prefiksi — hammasi bitta diapazonda.</summary>
    private const string PhonePrefix = "+99890111";

    private readonly List<DemoAccount> _accounts = [];

    private User _admin = null!;
    private User _academic = null!;
    private User _teacher1 = null!;
    private User _teacher2 = null!;
    private User _curator1 = null!;
    private User _curator2 = null!;
    private readonly List<User> _students = [];

    private GroupCategory _catChildren = null!;
    private GroupCategory _catAdults = null!;
    private GroupCategory _catIndividual = null!;

    private Course _course = null!;
    private readonly List<ModuleLesson> _lessons = [];

    private Group _main = null!;
    private Group _evening = null!;
    private Group _individual = null!;
    private Group _archived = null!;
    private Group _curatorGroup = null!;

    private LiveSession _past1 = null!;
    private LiveSession _past2 = null!;
    private LiveSession _past3 = null!;
    private LiveSession _curatorPast = null!;

    private Assignment _groupAssignment = null!;
    private Assignment _courseAssignment = null!;

    /// <summary>Kirish ma'lumotlari jadvali (log uchun).</summary>
    public IReadOnlyList<DemoAccount> Accounts => _accounts;

    /// <summary>Butun ssenariyni yozadi.</summary>
    public async Task BuildAsync(CancellationToken ct)
    {
        await SeedPeopleAsync(ct).ConfigureAwait(false);
        await SeedCatalogAsync(ct).ConfigureAwait(false);
        await SeedGroupsAsync(ct).ConfigureAwait(false);
        await SeedSessionsAsync(ct).ConfigureAwait(false);
        await SeedRecordingsAsync(ct).ConfigureAwait(false);
        await SeedAssignmentsAsync(ct).ConfigureAwait(false);
        await SeedTestsAsync(ct).ConfigureAwait(false);
        await SeedFinanceAsync(ct).ConfigureAwait(false);
        await SeedConversationsAsync(ct).ConfigureAwait(false);
        await SeedExtrasAsync(ct).ConfigureAwait(false);
    }

    // ══════════════════════════════════════════════════════════ 1. ODAMLAR

    private async Task SeedPeopleAsync(CancellationToken ct)
    {
        // ★ ADMIN — TEGILMAYDI, faqat Telegram bog'lanadi.
        //
        // 🔴 Uning telefoni `Bootstrap__AdminPhone` dan keladi va u
        // PROD'DA LOYIHA EGASINING HAQIQIY RAQAMI bo'lishi mumkin.
        // Namunaviy raqamga almashtirish — egasini o'z tizimidan
        // qulflab qo'yish demak.
        _admin = await db.Users
            .FirstAsync(u => u.Email == DbInitializer.AdminEmail, ct)
            .ConfigureAwait(false);

        if (_admin.TelegramId is null)
            _admin.LinkTelegram(TelegramBase + 1, "zinnur_demo_admin", now);

        _accounts.Add(new DemoAccount(
            "Administrator", _admin.FullName, _admin.Phone ?? "-", _admin.TelegramId!.Value));

        _academic = await UpsertAsync(
            DemoDataSeeder.AcademicEmail, "Dilnoza Ergasheva", UserRole.Academic,
            Phone(1), TelegramBase + 11, "O'quv bo'limi", ct).ConfigureAwait(false);

        // ⚠️ `teacher@zinnur.uz` va `student@zinnur.uz` — `DbInitializer` ning
        //    minimal seed'i. Ular QAYTA ISHLATILADI, yangisi yaratilmaydi:
        //    aks holda ro'yxatda ma'nosiz "Demo Ustoz" qatori osilib qolardi
        //    va tekshiruvchi qaysi biri haqiqiy ssenariy ekanini bilmasdi.
        _teacher1 = await UpsertAsync(
            "teacher@zinnur.uz", "Bekzod Rahimov", UserRole.Teacher,
            Phone(11), TelegramBase + 21, "Ustoz", ct).ConfigureAwait(false);

        _teacher2 = await UpsertAsync(
            "teacher2@zinnur.uz", "Nodira Qosimova", UserRole.Teacher,
            Phone(12), TelegramBase + 22, "Ustoz", ct).ConfigureAwait(false);

        _curator1 = await UpsertAsync(
            "curator1@zinnur.uz", "Javohir To'xtayev", UserRole.Assistant,
            Phone(21), TelegramBase + 31, "Kurator", ct).ConfigureAwait(false);

        _curator2 = await UpsertAsync(
            "curator2@zinnur.uz", "Malika Yusupova", UserRole.Assistant,
            Phone(22), TelegramBase + 32, "Kurator", ct).ConfigureAwait(false);

        var names = StudentNames;

        for (var i = 0; i < names.Length; i++)
        {
            // Birinchi o'quvchi — `DbInitializer` yaratgan `student@zinnur.uz`.
            var email = i == 0
                ? "student@zinnur.uz"
                : string.Create(CultureInfo.InvariantCulture, $"student{i + 1:D2}@zinnur.uz");

            var student = await UpsertAsync(
                email, names[i], UserRole.Student,
                Phone(101 + i), TelegramBase + 101 + i, "O'quvchi", ct).ConfigureAwait(false);

            _students.Add(student);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static readonly string[] StudentNames =
    [
        "Ozodbek Yo'ldoshev",
        "Zilola Abdullayeva",
        "Sardor Aliyev",
        "Nilufar Karimova",
        "Jasurbek Ochilov",
        "Madina Sattorova",
        "Doniyor Ergashev",
        "Shahzoda Nazarova",
        "Islombek Qodirov",
        "Gulnoza Tursunova",
        "Alisher Xolmatov",
        "Sevinch Ismoilova",
    ];

    /// <summary>Raqam: <c>+99890111 XXXX</c> — diapazon bitta, ko'zga tashlanadi.</summary>
    private static string Phone(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{PhonePrefix}{index:D4}");

    /// <summary>
    /// Foydalanuvchini email bo'yicha topadi yoki yaratadi.
    ///
    /// ★ IDEMPOTENTLIKNING IKKINCHI QATLAMI: yuqorida marker tekshiruvi bor,
    /// lekin u chetlab o'tilsa ham bu yerda dublikat CHIQMAYDI — email
    /// unikal va yozuv YANGILANADI.
    /// </summary>
    private async Task<User> UpsertAsync(
        string email,
        string fullName,
        UserRole role,
        string phone,
        long telegramId,
        string roleLabel,
        CancellationToken ct)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email == email, ct)
            .ConfigureAwait(false);

        if (user is null)
        {
            user = new User
            {
                FullName = fullName,
                Email = email,
                PasswordHash = passwordHash,
                Role = role,
            };

            db.Users.Add(user);
        }
        else
        {
            user.FullName = fullName;
            user.Role = role;
        }

        // 🔴 FAQAT `SetPhone` — `PhoneNormalized` ni boshqa hech nima
        //    to'ldirmaydi, kirish esa AYNAN o'sha ustun bo'yicha izlaydi.
        user.SetPhone(phone);

        if (user.TelegramId is null)
            user.LinkTelegram(telegramId, null, now);

        _accounts.Add(new DemoAccount(roleLabel, fullName, phone, user.TelegramId!.Value));

        return user;
    }

    // ═════════════════════════════════════════════════════════ 2. KATALOG

    private async Task SeedCatalogAsync(CancellationToken ct)
    {
        _catChildren = await UpsertCategoryAsync("Bolalar guruhi", 1, active: true, ct)
            .ConfigureAwait(false);
        _catAdults = await UpsertCategoryAsync("Kattalar guruhi", 2, active: true, ct)
            .ConfigureAwait(false);
        _catIndividual = await UpsertCategoryAsync("Individual mashg'ulot", 3, active: true, ct)
            .ConfigureAwait(false);

        // ⚠️ FAOL EMAS kategoriya ATAYLAB: kategoriya tanlash ro'yxati
        //    arxivlanganini KO'RSATMASLIGI kerak, filtr esa ko'rsatishi.
        //    Bitta ham nofaol qator bo'lmasa bu farq sinovdan o'tmasdi.
        await UpsertCategoryAsync("Arxiv (2025)", 4, active: false, ct).ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Kurs — `DbInitializer` yaratgan "ATF" qayta ishlatiladi.
        _course = await db.Courses
            .Include(c => c.Modules)
            .ThenInclude(m => m.Lessons)
            .FirstOrDefaultAsync(c => c.Name == "ATF", ct)
            .ConfigureAwait(false)
            ?? AddCourse();

        // ⚠️ "Tajvid" ATAYLAB ishlatilmaydi: u Qur'on tilovati QOIDALARI degan
        // ma'noni anglatadi (diniy fan), platforma esa arab TILINI o'rgatadi
        // (loyiha egasi, 2026-08-15: "biz diniy ta'lim bermaymiz"). "Harakat",
        // "fatha/kasra/damma", "madd" kabi atamalar QOLADI — ular arab
        // GRAMMATIKASI/FONETIKASI terminlari, diniy fanga xos emas.
        _course.Description =
            "Boshlang'ich kurs: arab alifbosi, harakatlar va talaffuz qoidalari.";
        _course.IsActive = true;

        var module1 = EnsureModule(_course, "1-modul — Alifbo", 1);
        var module2 = EnsureModule(_course, "2-modul — Harakatlar va talaffuz", 2);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Darslar. Birinchisi — minimal seed'dagi "Alif" (nomi to'ldiriladi).
        var l1 = EnsureLesson(module1, "Alif va Ba harflari", 1, 45, LessonKind.Normal, existingName: "Alif");
        var l2 = EnsureLesson(module1, "Jim, Ha va Xo harflari", 2, 45, LessonKind.Normal);
        var l3 = EnsureLesson(module1, "1-modul nazorati", 3, 60, LessonKind.Exam);
        var l4 = EnsureLesson(module2, "Fatha, Kasra va Damma", 1, 45, LessonKind.Normal);
        var l5 = EnsureLesson(module2, "Sukun va Shadda", 2, 45, LessonKind.Normal);
        var l6 = EnsureLesson(module2, "Madd qoidalari", 3, 50, LessonKind.Normal);

        l1.Description = "Alifbo boshlanishi: ikki harfning yozilishi va talaffuzi.";
        l4.Description = "Uch harakat: belgisi, o'qilishi va mashqlari.";

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _lessons.AddRange([l1, l2, l3, l4, l5, l6]);

        await SeedLessonAssetsAsync(l1, l2, l3, l4, l6, ct).ConfigureAwait(false);
    }

    private Course AddCourse()
    {
        var course = new Course { Name = "ATF", Position = 1 };
        db.Courses.Add(course);
        return course;
    }

    private async Task<GroupCategory> UpsertCategoryAsync(
        string name, int position, bool active, CancellationToken ct)
    {
        var category = await db.GroupCategories
            .FirstOrDefaultAsync(c => c.Name == name, ct)
            .ConfigureAwait(false);

        if (category is null)
        {
            category = new GroupCategory { Name = name };
            db.GroupCategories.Add(category);
        }

        category.Position = position;
        category.IsActive = active;
        category.Validate();

        return category;
    }

    private static CourseModule EnsureModule(Course course, string name, int position)
    {
        var module = course.Modules.FirstOrDefault(m => m.Position == position);

        if (module is null)
        {
            module = new CourseModule { Name = name, Position = position, CourseId = course.Id };
            course.Modules.Add(module);
        }

        module.Name = name;
        return module;
    }

    private static ModuleLesson EnsureLesson(
        CourseModule module,
        string name,
        int position,
        int durationMin,
        LessonKind kind,
        string? existingName = null)
    {
        var lesson = module.Lessons.FirstOrDefault(l => l.Position == position)
                     ?? (existingName is null
                         ? null
                         : module.Lessons.FirstOrDefault(l => l.Name == existingName));

        if (lesson is null)
        {
            lesson = new ModuleLesson { Name = name, Position = position };
            module.Lessons.Add(lesson);
        }

        lesson.Name = name;
        lesson.Position = position;
        lesson.DurationMin = durationMin;
        lesson.Kind = kind;

        return lesson;
    }

    /// <summary>
    /// Dars mediasi.
    ///
    /// 🔴 ENG MUHIM QATOR SHU YERDA: <paramref name="multiPart"/> darsiga
    /// UCHTA video qismi yoziladi (<c>Position</c> 1..3, har birida sarlavha).
    /// Ko'p qismli pleer AYNAN shu holatda ishlaydi — bitta qismli darsda
    /// u oddiy pleerdan farq qilmaydi va xatosi ko'rinmaydi.
    /// </summary>
    private async Task SeedLessonAssetsAsync(
        ModuleLesson single,
        ModuleLesson multiPart,
        ModuleLesson exam,
        ModuleLesson second,
        ModuleLesson third,
        CancellationToken ct)
    {
        if (await db.LessonAssets.AnyAsync(ct).ConfigureAwait(false))
            return;

        AddVideo(single, 1, null, 2_640, 180_000_000);

        AddVideo(multiPart, 1, "1-qism — harflar shakli", 1_020, 74_000_000);
        AddVideo(multiPart, 2, "2-qism — talaffuz mashqi", 1_380, 96_000_000);
        AddVideo(multiPart, 3, "3-qism — xulosa va uy vazifasi", 720, 52_000_000);

        AddVideo(second, 1, "1-qism — nazariya", 1_500, 105_000_000);
        AddVideo(second, 2, "2-qism — mashqlar", 1_140, 82_000_000);

        AddVideo(third, 1, null, 1_800, 128_000_000);

        // ⚠️ Imtihon darsiga FAQAT rasm biriktiriladi (`AllowedAssetKind`).
        //    Bu qoida buzilsa `LessonAssetService` yiqilardi — seeder ham
        //    o'sha invariantga bo'ysunadi.
        var sheet1 = await media.ImageAsync("lesson-assets", 1_240, 1_754, 1, ct)
            .ConfigureAwait(false);
        var sheet2 = await media.ImageAsync("lesson-assets", 1_240, 1_754, 4, ct)
            .ConfigureAwait(false);

        AddAsset(exam, LessonAssetKind.Image, 1, "Nazorat varaqasi — 1-bet", sheet1, null, 1_240, 1_754);
        AddAsset(exam, LessonAssetKind.Image, 2, "Nazorat varaqasi — 2-bet", sheet2, null, 1_240, 1_754);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private void AddVideo(ModuleLesson lesson, int position, string? title, int durationSec, long size)
    {
        var file = media.VideoMetadata(size);
        AddAsset(lesson, LessonAssetKind.Video, position, title, file, durationSec, 1_280, 720);
    }

    private void AddAsset(
        ModuleLesson lesson,
        LessonAssetKind kind,
        int position,
        string? title,
        DemoFile file,
        int? durationSec,
        int width,
        int height)
    {
        var asset = new LessonAsset
        {
            LessonId = lesson.Id,
            Kind = kind,
            Position = position,
            Title = title,
            ObjectKey = file.ObjectKey,
            ContentType = file.ContentType,
            SizeBytes = file.SizeBytes,
            DurationSec = durationSec,
            Width = width,
            Height = height,
            CreatedById = _teacher1.Id,
        };

        asset.Validate();
        db.LessonAssets.Add(asset);
    }

    // ══════════════════════════════════════════════════════════ 3. GURUHLAR

    private async Task SeedGroupsAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // Kurator guruhi BIRINCHI: oddiy guruh unga havola qiladi.
        _curatorGroup = await UpsertGroupAsync("Kurator guruhi — Javohir", ct).ConfigureAwait(false);
        _curatorGroup.Type = GroupType.Curator;
        _curatorGroup.CourseId = _course.Id;
        _curatorGroup.AssistantId = _curator1.Id;
        _curatorGroup.CategoryId = _catChildren.Id;
        _curatorGroup.StartDate = today.AddMonths(-2);
        _curatorGroup.Weekdays = [DayOfWeek.Friday];
        _curatorGroup.StartTime = new TimeOnly(17, 0);
        _curatorGroup.DurationMinutes = 60;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Asosiy guruh — `DbInitializer` yaratgan "ATF-1 (demo)" ustiga.
        _main = await db.Groups
            .FirstOrDefaultAsync(g => g.Name == "ATF-1 (demo)", ct)
            .ConfigureAwait(false)
            ?? await UpsertGroupAsync("ATF-1 (ertalabki)", ct).ConfigureAwait(false);

        _main.Name = "ATF-1 (ertalabki)";
        _main.CourseId = _course.Id;
        _main.TeacherId = _teacher1.Id;
        _main.AssistantId = _curator1.Id;
        _main.CategoryId = _catChildren.Id;
        _main.CuratorGroupId = _curatorGroup.Id;
        _main.Type = GroupType.Group;
        _main.IsActive = true;
        _main.StartDate = today.AddMonths(-2);
        _main.CourseMonths = 8;
        _main.Weekdays = [DayOfWeek.Monday, DayOfWeek.Wednesday];
        _main.StartTime = new TimeOnly(9, 0);
        _main.DurationMinutes = 80;

        // Avtomatik yozuv + o'quvchiga ko'rinishi — yozuvlar ekrani uchun.
        _main.RecordEnabled = true;
        _main.RecordingsVisibleToStudents = true;
        _main.VideoStartLessonId = _lessons[0].Id;
        _main.AssignmentGraderRole = GroupStaffRole.Both;
        _main.QuestionResponderRole = GroupStaffRole.Assistant;

        _evening = await UpsertGroupAsync("ATF-2 (kechki)", ct).ConfigureAwait(false);
        _evening.CourseId = _course.Id;
        _evening.TeacherId = _teacher2.Id;
        _evening.AssistantId = _curator2.Id;
        _evening.CategoryId = _catAdults.Id;
        _evening.StartDate = today.AddMonths(-1);
        _evening.Weekdays = [DayOfWeek.Tuesday, DayOfWeek.Thursday];
        _evening.StartTime = new TimeOnly(19, 0);
        _evening.DurationMinutes = 90;

        // ⚠️ INDIVIDUAL tur: haftalik kunlar soni qoidasi BOSHQACHA
        //    (oddiy guruhda aniq 2 kun, individualda ixtiyoriy).
        //    Filtrda "tur" ustuni bo'sh qolmasin uchun kerak.
        _individual = await UpsertGroupAsync("Individual — Sevinch Ismoilova", ct)
            .ConfigureAwait(false);
        _individual.Type = GroupType.Individual;
        _individual.CourseId = _course.Id;
        _individual.TeacherId = _teacher2.Id;
        _individual.CategoryId = _catIndividual.Id;
        _individual.StartDate = today.AddMonths(-1);
        _individual.Weekdays = [DayOfWeek.Saturday];
        _individual.StartTime = new TimeOnly(11, 0);
        _individual.DurationMinutes = 60;
        _individual.CourseMonths = 4;

        // ⚠️ ARXIV guruh — `IsActive = false`. Guruhlar ro'yxati odatda
        //    faqat faollarni ko'rsatadi; arxiv filtri tekshirilishi uchun
        //    kamida bitta nofaol guruh BO'LISHI SHART.
        _archived = await UpsertGroupAsync("ATF-0 (2025 bitiruvchilari)", ct).ConfigureAwait(false);
        _archived.CourseId = _course.Id;
        _archived.TeacherId = _teacher1.Id;
        _archived.AssistantId = _curator2.Id;
        _archived.CategoryId = _catAdults.Id;
        _archived.IsActive = false;
        _archived.StartDate = today.AddMonths(-11);
        _archived.CourseMonths = 8;
        _archived.Weekdays = [DayOfWeek.Monday, DayOfWeek.Friday];
        _archived.StartTime = new TimeOnly(15, 0);

        foreach (var group in new[] { _curatorGroup, _main, _evening, _individual, _archived })
            group.ValidateScheduleRule();

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // ---- A'ZOLIK ----
        // Holatlar aralash: faol, pauzada, to'xtatilgan — har biri filtrda
        // boshqacha ko'rinadi.
        //
        // ★ Mavjud juftliklar OLDINDAN o'qiladi: `(GroupId, StudentId)`
        //   unikal, ya'ni takroriy qo'shish butun seeding'ni yiqitardi.
        _members = (await db.GroupMembers
                .Select(m => new { m.GroupId, m.StudentId })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(m => (m.GroupId, m.StudentId))
            .ToHashSet();

        for (var i = 0; i < 7; i++)
            AddMember(_main, _students[i], MemberStatus.Active, now.AddDays(-60));

        AddMember(_main, _students[7], MemberStatus.Paused, now.AddDays(-55));

        for (var i = 8; i < 11; i++)
            AddMember(_evening, _students[i], MemberStatus.Active, now.AddDays(-30));

        AddMember(_individual, _students[11], MemberStatus.Active, now.AddDays(-25));

        AddMember(_archived, _students[0], MemberStatus.Stopped, now.AddDays(-330));
        AddMember(_archived, _students[1], MemberStatus.Moved, now.AddDays(-330));

        for (var i = 0; i < 4; i++)
            AddMember(_curatorGroup, _students[i], MemberStatus.Active, now.AddDays(-58));

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<Group> UpsertGroupAsync(string name, CancellationToken ct)
    {
        var group = await db.Groups
            .FirstOrDefaultAsync(g => g.Name == name, ct)
            .ConfigureAwait(false);

        if (group is not null) return group;

        group = new Group { Name = name, StartDate = DateOnly.FromDateTime(now.UtcDateTime) };
        db.Groups.Add(group);

        return group;
    }

    private HashSet<(long GroupId, long StudentId)> _members = [];

    private void AddMember(Group group, User student, MemberStatus status, DateTimeOffset joinedAt)
    {
        if (!_members.Add((group.Id, student.Id))) return;

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = group.Id,
            StudentId = student.Id,
            Status = status,
            JoinedAt = joinedAt,
        });
    }

    // ══════════════════════════════════════════════════ 4. DARSLAR VA DAVOMAT

    private async Task SeedSessionsAsync(CancellationToken ct)
    {
        _past1 = Ended(_main, _teacher1, "Alif va Ba harflari — jonli dars", -7);
        _past2 = Ended(_main, _teacher1, "Jim, Ha va Xo — jonli dars", -5);
        _past3 = Ended(_main, _teacher1, "Fatha, Kasra va Damma — jonli dars", -2);
        _curatorPast = Ended(_curatorGroup, _curator1, "Kurator darsi — savol-javob", -3);
        _curatorPast.Type = SessionType.Assistant;

        // KELGUSI dars — kalendar va "yaqinlashayotgan dars" kartasi uchun.
        db.LiveSessions.Add(new LiveSession
        {
            GroupId = _main.Id,
            HostId = _teacher1.Id,
            Title = "Sukun va Shadda — jonli dars",
            Type = SessionType.Teacher,
            Status = SessionStatus.Scheduled,
            ScheduledStart = now.AddDays(3),
            ScheduledEnd = now.AddDays(3).AddMinutes(80),
            RoomName = LiveSession.GenerateRoomName(),
        });

        db.LiveSessions.Add(new LiveSession
        {
            GroupId = _evening.Id,
            HostId = _teacher2.Id,
            Title = "Kechki guruh — 6-dars",
            Type = SessionType.Teacher,
            Status = SessionStatus.Scheduled,
            ScheduledStart = now.AddDays(1),
            ScheduledEnd = now.AddDays(1).AddMinutes(90),
            RoomName = LiveSession.GenerateRoomName(),
        });

        // ★ "HOZIR BOSHLANADIGAN" DARS — `DbInitializer` yaratgan dars
        //   (2 daqiqadan keyin) qayta ishlatiladi. Domen qoidasi bo'yicha
        //   darsni `ScheduledStart − 5 daqiqa` dan boshlash mumkin, ya'ni
        //   tekshiruvchi seeddan KEYIN DARHOL "Darsni boshlash" tugmasini
        //   bosa oladi. Yangi dars yaratsak bu imkoniyat yo'qolardi.
        var soon = await db.LiveSessions
            .Where(s => s.GroupId == _main.Id && s.Status == SessionStatus.Scheduled)
            .OrderBy(s => s.ScheduledStart)
            .FirstOrDefaultAsync(s => s.ScheduledStart <= now.AddMinutes(30), ct)
            .ConfigureAwait(false);

        if (soon is null)
        {
            soon = new LiveSession
            {
                GroupId = _main.Id,
                HostId = _teacher1.Id,
                Title = string.Empty,
                Type = SessionType.Teacher,
                Status = SessionStatus.Scheduled,
                ScheduledStart = now.AddMinutes(2),
                ScheduledEnd = now.AddMinutes(82),
                RoomName = LiveSession.GenerateRoomName(),
            };

            db.LiveSessions.Add(soon);
        }

        soon.Title = "Madd qoidalari — jonli dars (hozir boshlanadi)";
        soon.HostId = _teacher1.Id;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        SeedAttendance();
        SeedLessonGrades();
        SeedSessionChat();

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private LiveSession Ended(Group group, User host, string title, int daysAgo)
    {
        var start = now.AddDays(daysAgo);

        var session = new LiveSession
        {
            GroupId = group.Id,
            HostId = host.Id,
            Title = title,
            Type = group.PlannedSessionType,
            Status = SessionStatus.Ended,
            ScheduledStart = start,
            ScheduledEnd = start.AddMinutes(group.DurationMinutes),
            ActualStart = start.AddMinutes(1),
            ActualEnd = start.AddMinutes(group.DurationMinutes + 3),
            RoomName = LiveSession.GenerateRoomName(),
        };

        db.LiveSessions.Add(session);
        return session;
    }

    /// <summary>
    /// Davomat — BESHTA holat.
    ///
    /// 🔴 BESHINCHISI — QATORNING O'ZI YO'QLIGI. Jurnalda "belgilanmagan"
    /// katak "yo'q" dan BOSHQACHA ko'rinishi kerak; qator bo'lmasa ustoz
    /// hali baholamagan degani. Hamma o'quvchiga qator yozib qo'ysak bu
    /// holat hech qachon ekranga chiqmasdi.
    /// </summary>
    private void SeedAttendance()
    {
        var s = _students;

        Attend(_past1, s[0], AttendanceStatus.Present, 4_720);
        Attend(_past1, s[1], AttendanceStatus.Present, 4_810);
        Attend(_past1, s[2], AttendanceStatus.Late, 3_600, lateMinutes: 18);
        Attend(_past1, s[3], AttendanceStatus.Partial, 1_260);
        Attend(_past1, s[5], AttendanceStatus.Late, 4_100, lateMinutes: 9);
        Attend(_past1, s[6], AttendanceStatus.Present, 4_760);

        // Qo'lda belgilangan yo'qlik — sababi bilan (audit yozuvi ham bor).
        var manual = Attend(_past1, s[4], AttendanceStatus.Absent, 0);
        manual.IsManual = true;
        manual.Reason = "Kasal — ota-onasi oldindan ogohlantirgan.";

        db.AttendanceAudits.Add(new AttendanceAudit
        {
            SessionId = _past1.Id,
            StudentId = s[4].Id,
            ActorId = _curator1.Id,
            OldStatus = AttendanceStatus.Absent,
            NewStatus = AttendanceStatus.Absent,
            OldIsManual = false,
            NewReason = manual.Reason,
            CreatedAt = now.AddDays(-7).AddHours(2),
            Attendance = manual,
        });

        // s[7] — ATAYLAB qatorsiz (pauzadagi o'quvchi).

        for (var i = 0; i < 6; i++)
            Attend(_past2, s[i], AttendanceStatus.Present, 4_800);

        Attend(_past2, s[6], AttendanceStatus.Absent, 0);

        Attend(_past3, s[0], AttendanceStatus.Present, 4_790);
        Attend(_past3, s[1], AttendanceStatus.Late, 4_200, lateMinutes: 12);
        Attend(_past3, s[2], AttendanceStatus.Present, 4_830);
        Attend(_past3, s[3], AttendanceStatus.Present, 4_650);
        Attend(_past3, s[4], AttendanceStatus.Present, 4_700);
        Attend(_past3, s[5], AttendanceStatus.Partial, 900);

        Attend(_curatorPast, s[0], AttendanceStatus.Present, 3_500);
        Attend(_curatorPast, s[1], AttendanceStatus.Present, 3_480);
        Attend(_curatorPast, s[2], AttendanceStatus.Absent, 0);
        Attend(_curatorPast, s[3], AttendanceStatus.Late, 2_900, lateMinutes: 11);
    }

    private Attendance Attend(
        LiveSession session, User student, AttendanceStatus status, int seconds, int lateMinutes = 0)
    {
        var joined = session.ActualStart!.Value.AddMinutes(lateMinutes);

        var attendance = new Attendance
        {
            SessionId = session.Id,
            StudentId = student.Id,
            Status = status,
            DurationSeconds = seconds,
            FirstJoinAt = status == AttendanceStatus.Absent ? null : joined,
            LastJoinAt = status == AttendanceStatus.Absent ? null : joined,
            LeftAt = status == AttendanceStatus.Absent ? null : joined.AddSeconds(seconds),
            CreatedAt = session.ActualStart.Value,
        };

        db.Attendances.Add(attendance);
        return attendance;
    }

    /// <summary>
    /// Dars baholari (R24) — reytingning TO'RTINCHI mezoni.
    ///
    /// ★ Ataylab HAMMA o'quvchiga emas: baholanmagan katak ham ekranda
    /// ko'rinadi va o'rtacha hisobiga KIRMASLIGI kerak.
    /// </summary>
    private void SeedLessonGrades()
    {
        Grade(_past1, _students[0], 5m, null, null);
        Grade(_past1, _students[1], 4m, null, "Yozuvi toza, talaffuz ustida ishlash kerak.");
        Grade(_past1, _students[2], 3m, null, "Kech qoldi, mashqni tugatmadi.");
        Grade(_past1, _students[3], 5m, null, null);
        Grade(_past1, _students[5], 4m, null, null);

        Grade(_past2, _students[0], 4.5m, null, null);
        Grade(_past2, _students[1], 5m, null, null);
        Grade(_past2, _students[2], 4m, null, null);
        Grade(_past2, _students[3], 3.5m, null, null);
        Grade(_past2, _students[4], 4m, null, null);

        // ★ BOSHQA MAKSIMAL BALL: 10 lik tizim. Foiz hisobi ikkala shkalada
        //   ham to'g'ri ishlashini faqat aralash ma'lumot ko'rsatadi.
        Grade(_past3, _students[0], 9m, 10m, "Mustahkam natija.");
        Grade(_past3, _students[1], 7.5m, 10m, null);
        Grade(_past3, _students[2], 8m, 10m, "Talaffuzi sezilarli yaxshilandi.");

        // Tahrirlangan baho tarixi — audit ekrani bo'sh qolmasin.
        db.LessonGradeAudits.Add(new LessonGradeAudit
        {
            SessionId = _past1.Id,
            StudentId = _students[2].Id,
            ActorId = _teacher1.Id,
            OldScore = 2m,
            NewScore = 3m,
            OldComment = null,
            NewComment = "Kech qoldi, mashqni tugatmadi.",
            CreatedAt = now.AddDays(-6),
        });
    }

    private void Grade(LiveSession session, User student, decimal score, decimal? max, string? comment)
    {
        var grade = new LessonGrade
        {
            SessionId = session.Id,
            StudentId = student.Id,
            GradedById = session.HostId ?? _teacher1.Id,
            GradedAt = session.ActualEnd ?? now,
            CreatedAt = session.ActualEnd ?? now,
        };

        grade.Apply(score, max, comment, grade.GradedById, grade.GradedAt);
        db.LessonGrades.Add(grade);
    }

    /// <summary>Jonli dars ichidagi chat — yozuv sahifasida ham ko'rinadi.</summary>
    private void SeedSessionChat()
    {
        var start = _past1.ActualStart!.Value;

        Say(_past1, _teacher1, "Assalomu alaykum! Bugun Alif va Ba harflarini yozamiz.", start.AddMinutes(1));
        Say(_past1, _students[0], "Va alaykum assalom, ustoz!", start.AddMinutes(2));
        Say(_past1, _students[2], "Ustoz, kamerani biroz yaqinlashtira olasizmi?", start.AddMinutes(14));
        Say(_past1, _teacher1, "Albatta, hozir kattalashtiraman.", start.AddMinutes(15));
        Say(_past1, _students[1], "Rahmat, endi aniq ko'rinyapti.", start.AddMinutes(16));
    }

    private void Say(LiveSession session, User sender, string body, DateTimeOffset at) =>
        db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            SenderId = sender.Id,
            SenderName = sender.FullName,
            Body = ChatMessage.NormalizeBody(body),
            SentAt = at,
            CreatedAt = at,
        });

    // ═════════════════════════════════════════════════════════ 5. YOZUVLAR

    private async Task SeedRecordingsAsync(CancellationToken ct)
    {
        // 1) Tayyor va o'quvchilarga OCHIQ yozuv.
        //    ⚠️ 2026-08-15 dan `IsVisibleToStudents` STANDARTI `false`
        //    (sabab `SessionRecording.IsVisibleToStudents` izohida), ya'ni
        //    "ochiq" holatni demo ma'lumotida ko'rsatish uchun endi
        //    `ShowToStudents` ANIQ chaqirilishi SHART — aks holda bu qator
        //    ham `hidden` bilan bir xil (yashirin) bo'lib qolardi va demo
        //    ikki rolni ham (ochiq/yopiq) tekshirish imkoniyatini yo'qotardi.
        var ok = Recording(_past1, 4_680, 512_000_000);
        ok.MarkCompleted(null, ok.SizeBytes, ok.DurationSeconds, _past1.ActualEnd!.Value, now);
        ok.ShowToStudents(_academic.Id, now.AddDays(-5));

        // 2) Tayyor, lekin O'QUVCHIDAN YASHIRILGAN.
        //    ⚠️ Bu holat UI'da faqat xodimga ko'rinadi — ikkala rolni ham
        //    tekshirish uchun kerak.
        var hidden = Recording(_past2, 4_710, 498_000_000);
        hidden.MarkCompleted(null, hidden.SizeBytes, hidden.DurationSeconds, _past2.ActualEnd!.Value, now);
        hidden.HideFromStudents(_academic.Id, now.AddDays(-4));

        // 3) Tayyor + SIFAT NAZORATI xulosasi (R29) — bu ham OCHIQ holatni
        //    namoyish qiladi (1-band bilan AYNI sabab bo'yicha ANIQ ochiladi).
        var reviewed = Recording(_past3, 4_640, 476_000_000);
        reviewed.MarkCompleted(null, reviewed.SizeBytes, reviewed.DurationSeconds, _past3.ActualEnd!.Value, now);
        reviewed.ShowToStudents(_academic.Id, now.AddDays(-2));

        // 4) YIQILGAN yozuv — xato holati ham ko'rinishi kerak.
        var failed = Recording(_curatorPast, null, null);
        failed.MarkFailed("Egress xizmatiga ulanib bo'lmadi (namunaviy yozuv).", now.AddDays(-3));

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        db.SessionReviews.Add(SessionReview.Create(
            _past3.Id,
            _academic.Id,
            SessionReviewVerdict.HasIssue,
            plus: "Mavzuni tushuntirish tizimli, misollar aniq.",
            minus: "Darsning 12-daqiqasida ovoz yo'qolgan.",
            conclusion: "Ustoz bilan gaplashildi, keyingi darsda mikrofon oldindan tekshiriladi.",
            now: now.AddDays(-1)));

        db.SessionReviews.Add(SessionReview.Create(
            _past1.Id,
            _academic.Id,
            SessionReviewVerdict.Approved,
            plus: "Dars reja bo'yicha o'tdi, savol-javob faol.",
            minus: null,
            conclusion: "Ushbu tajribani boshqa guruhlarda ham qo'llash tavsiya etiladi.",
            now: now.AddDays(-6)));

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private SessionRecording Recording(LiveSession session, int? durationSec, long? size)
    {
        var file = media.RecordingMetadata(size ?? 0);

        var recording = new SessionRecording
        {
            SessionId = session.Id,
            RequestedBy = session.HostId,
            ObjectKey = file.ObjectKey,
            EgressId = string.Create(CultureInfo.InvariantCulture, $"demo-egress-{session.Id}"),
            DurationSeconds = durationSec,
            SizeBytes = size,
            StartedAt = session.ActualStart,
            Attempts = 1,
            LastAttemptAt = session.ActualStart,
            CreatedAt = session.ActualStart ?? now,
        };

        db.SessionRecordings.Add(recording);
        return recording;
    }

    // ══════════════════════════════════════════════════════════ 6. VAZIFALAR

    private async Task SeedAssignmentsAsync(CancellationToken ct)
    {
        _groupAssignment = new Assignment
        {
            GroupId = _main.Id,
            Title = "1-dars uy vazifasi — Alif harfini yozish",
            Description =
                "Daftarga Alif harfini 10 marta yozing va suratini yuklang. "
                + "Yozuv toza va qatorga tekis joylashgan bo'lsin.",
            MaxScore = 5,
            DueAt = now.AddDays(-1),
            AllowedFormats = AnswerFormats.Text | AnswerFormats.Image,
            GraderRole = GroupStaffRole.Assistant,
            CreatedById = _teacher1.Id,
            CreatedAt = now.AddDays(-6),
        };

        _groupAssignment.Validate();
        db.Assignments.Add(_groupAssignment);

        // ★ KURS VAZIFASI: guruhga emas, DARSGA biriktirilgan — ya'ni shu
        //   kursdagi HAMMA guruh ko'radi. `GraderRole` bu yerda taqiqlangan
        //   (sabab `Assignment.Validate` da) — shuning uchun berilmaydi.
        _courseAssignment = new Assignment
        {
            ModuleLessonId = _lessons[3].Id,
            Title = "Fatha va Kasra — yozma mashq",
            Description = "Berilgan 12 ta so'zni harakatlari bilan ko'chiring.",
            MaxScore = 10,
            AllowedFormats = AnswerFormats.Text | AnswerFormats.Image | AnswerFormats.Audio,
            CreatedById = _teacher1.Id,
            CreatedAt = now.AddDays(-4),
        };

        _courseAssignment.Validate();
        db.Assignments.Add(_courseAssignment);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var sheet = await media.ImageAsync("assignment-attachments", 1_240, 1_754, 2, ct)
            .ConfigureAwait(false);

        var attachment = new AssignmentAttachment
        {
            AssignmentId = _courseAssignment.Id,
            Kind = AttachmentKind.Image,
            Position = 1,
            ObjectKey = sheet.ObjectKey,
            ContentType = sheet.ContentType,
            SizeBytes = sheet.SizeBytes,
            CreatedById = _teacher1.Id,
        };

        attachment.Validate();
        db.AssignmentAttachments.Add(attachment);

        await SeedSubmissionsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Javoblar — TO'RT holat + umuman topshirmaganlar.
    ///
    /// 🔴 "Topshirmagan" ham holat: tekshiruv navbatida u KO'RINMASLIGI,
    /// guruh ro'yxatida esa qizil ko'rinishi kerak. Hamma javob yozilsa
    /// bu farq sinovdan o'tmasdi.
    /// </summary>
    private async Task SeedSubmissionsAsync(CancellationToken ct)
    {
        // 1) Topshirilgan, HALI BAHOLANMAGAN — tekshiruv navbatining o'zagi.
        var pending = Submission.Create(
            _groupAssignment.Id, _students[0].Id,
            "Ustoz, 10 marta yozdim. Rasmi biroz qorong'i chiqdi, kechirasiz.",
            isLate: false, now.AddDays(-2));

        db.Submissions.Add(pending);

        // 2) BAHOLANGAN + ustoz izohi (fayl bilan).
        var graded = Submission.Create(
            _groupAssignment.Id, _students[1].Id,
            "Vazifani bajardim.", isLate: false, now.AddDays(-3));

        graded.Grade(5m, _groupAssignment.MaxScore, "Ajoyib! Harflar orasidagi masofa ham to'g'ri.",
            _curator1.Id, now.AddDays(-2));

        db.Submissions.Add(graded);

        // 3) BAHOLANGAN, keyin QAYTA TOPSHIRISHGA OCHILGAN.
        var reopened = Submission.Create(
            _groupAssignment.Id, _students[2].Id,
            "Yozib ko'rdim.", isLate: false, now.AddDays(-3));

        reopened.Grade(3m, _groupAssignment.MaxScore, "Harflar qiyshiq. Qaytadan yozib yuboring.",
            _curator1.Id, now.AddDays(-2));

        reopened.ReopenForResubmit("Qatorga tekis yozing va yorug'roq suratga oling.", now.AddDays(-1));

        db.Submissions.Add(reopened);

        // 4) KECHIKKAN javob — FAYL bilan.
        var late = Submission.Create(
            _groupAssignment.Id, _students[3].Id,
            "Kechikkanim uchun uzr, internet o'chib qolgandi.",
            isLate: true, now.AddHours(-6));

        db.Submissions.Add(late);

        // Kurs vazifasiga javoblar — boshqa guruh o'quvchilaridan ham.
        var courseGraded = Submission.Create(
            _courseAssignment.Id, _students[0].Id, "Mashqni bajardim.",
            isLate: false, now.AddDays(-2));

        courseGraded.Grade(9m, _courseAssignment.MaxScore, "Yaxshi, faqat 7-so'zda kasra tushib qolgan.",
            _teacher1.Id, now.AddDays(-1));

        db.Submissions.Add(courseGraded);

        var courseOther = Submission.Create(
            _courseAssignment.Id, _students[8].Id, "Tayyor.", isLate: false, now.AddDays(-1));

        db.Submissions.Add(courseOther);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // ---- FAYLLAR ----
        var answer = await media.SubmissionImageAsync(_students[3].Id, ct).ConfigureAwait(false);

        db.SubmissionFiles.Add(new SubmissionFile
        {
            SubmissionId = late.Id,
            ObjectKey = answer.ObjectKey,
            Kind = AttachmentKind.Image,
            ContentType = answer.ContentType,
            SizeBytes = answer.SizeBytes,
        });

        var feedback = await media
            .DocumentAsync(
                "submission-feedback",
                "Zilola uchun izoh\n\n"
                + "1) Alif harfining boshlanish nuqtasi biroz baland.\n"
                + "2) Ba harfining nuqtasi markazda bo'lsin.\n"
                + "3) Keyingi darsga 5 qator mashq yozib keling.\n",
                ct)
            .ConfigureAwait(false);

        var feedbackFile = new SubmissionFeedbackFile
        {
            SubmissionId = graded.Id,
            Kind = AttachmentKind.Document,
            ObjectKey = feedback.ObjectKey,
            ContentType = feedback.ContentType,
            FileName = "izoh-zilola.txt",
            SizeBytes = feedback.SizeBytes,
            CreatedById = _curator1.Id,
        };

        feedbackFile.Validate();
        db.SubmissionFeedbackFiles.Add(feedbackFile);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ═════════════════════════════════════════════════════════════ 7. TESTLAR

    private async Task SeedTestsAsync(CancellationToken ct)
    {
        var lessonTest = new Test
        {
            Title = "Alifbo — nazorat testi",
            Description = "1-modul bo'yicha qisqa test.",
            Kind = TestKind.Lesson,
            ModuleLessonId = _lessons[1].Id,
            TimeLimitMinutes = 15,
            CreatedById = _teacher1.Id,
            CreatedAt = now.AddDays(-5),
        };

        AddQuestion(lessonTest, "Alif harfi qaysi tomondan yoziladi?", 1,
            ("O'ngdan chapga", true), ("Chapdan o'ngga", false), ("Yuqoridan pastga", false));

        AddQuestion(lessonTest, "Ba harfining nuqtasi qayerda turadi?", 2,
            ("Harf ostida", true), ("Harf ustida", false), ("Harf ichida", false));

        AddQuestion(lessonTest, "Quyidagilardan qaysilari alifbodagi harf?", 3,
            ("Jim", true), ("Ha", true), ("Fatha", false), ("Sukun", false));

        AddQuestion(lessonTest, "Xo harfida nechta nuqta bor?", 4,
            ("Bitta", true), ("Ikkita", false), ("Uchta", false));

        lessonTest.Validate();
        lessonTest.Publish();
        db.Tests.Add(lessonTest);

        var contest = new Test
        {
            Title = "Oylik musobaqa — talaffuz asoslari",
            Description = "Oy yakunidagi umumiy musobaqa. Har bir o'quvchi bir marta topshiradi.",
            Kind = TestKind.Competition,
            DueAt = now.AddDays(5),
            TimeLimitMinutes = 20,
            CreatedById = _academic.Id,
            CreatedAt = now.AddDays(-2),
        };

        AddQuestion(contest, "Madd qoidasi nimani anglatadi?", 1,
            ("Cho'zish", true), ("Qisqartirish", false), ("To'xtash", false));

        AddQuestion(contest, "Shadda belgisi nimani bildiradi?", 2,
            ("Harf ikki marta o'qiladi", true), ("Harf o'qilmaydi", false), ("Harf cho'ziladi", false));

        AddQuestion(contest, "Sukun qo'yilgan harf qanday o'qiladi?", 3,
            ("Harakatsiz", true), ("Cho'zib", false), ("Ikki marta", false));

        contest.Validate();
        contest.Publish();
        db.Tests.Add(contest);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // ---- URINISHLAR ----
        // ⚠️ IKKALA HOLAT HAM KERAK: tugallangan urinish natijani, DAVOM
        //    ETAYOTGANI esa taymer va "davom ettirish" tugmasini tekshiradi.
        Attempt(lessonTest, _students[0], submitted: true, score: 3m, max: 4m, minutesAgo: 2_880);
        Attempt(lessonTest, _students[1], submitted: false, score: null, max: null, minutesAgo: 6);
        Attempt(contest, _students[2], submitted: true, score: 2m, max: 3m, minutesAgo: 1_440);
        Attempt(contest, _students[3], submitted: false, score: null, max: null, minutesAgo: 3);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await SeedTestAnswersAsync(lessonTest, _students[0].Id, ct).ConfigureAwait(false);
        await SeedTestAnswersAsync(contest, _students[2].Id, ct).ConfigureAwait(false);
    }

    private static void AddQuestion(
        Test test, string body, int position, params (string Body, bool Correct)[] options)
    {
        var question = new TestQuestion
        {
            Body = body,
            Position = position,
            Points = 1,
        };

        for (var i = 0; i < options.Length; i++)
        {
            question.Options.Add(new TestOption
            {
                Body = options[i].Body,
                IsCorrect = options[i].Correct,
                Position = i + 1,
            });
        }

        test.Questions.Add(question);
    }

    private void Attempt(
        Test test, User student, bool submitted, decimal? score, decimal? max, int minutesAgo)
    {
        var started = now.AddMinutes(-minutesAgo);

        db.TestAttempts.Add(new TestAttempt
        {
            TestId = test.Id,
            StudentId = student.Id,
            Status = submitted ? AttemptStatus.Submitted : AttemptStatus.InProgress,
            Score = score,
            MaxScore = max,
            StartedAt = started,
            SubmittedAt = submitted ? started.AddMinutes(9) : null,
            CreatedAt = started,
        });
    }

    /// <summary>
    /// Tugallangan urinishga javob qatorlari.
    ///
    /// ★ Javoblar HAQIQIY variant ID'lariga bog'lanadi — natija sahifasi
    /// "qaysi savolda xato qilgan" ni ko'rsatadi va ID'lar mos kelmasa
    /// u bo'sh qolardi.
    /// </summary>
    private async Task SeedTestAnswersAsync(Test test, long studentId, CancellationToken ct)
    {
        var attempt = await db.TestAttempts
            .FirstAsync(a => a.TestId == test.Id && a.StudentId == studentId, ct)
            .ConfigureAwait(false);

        var questions = await db.TestQuestions
            .Where(q => q.TestId == test.Id)
            .Include(q => q.Options)
            .OrderBy(q => q.Position)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        for (var i = 0; i < questions.Count; i++)
        {
            var options = questions[i].Options.OrderBy(o => o.Position).ToList();

            // Oxirgi savolda ATAYLAB xato variant tanlanadi — natija 100%
            // bo'lmasin, aks holda "xato javob" ko'rinishi tekshirilmasdi.
            var chosen = i == questions.Count - 1
                ? options.FirstOrDefault(o => !o.IsCorrect) ?? options[0]
                : options.First(o => o.IsCorrect);

            db.TestAnswers.Add(new TestAnswer
            {
                AttemptId = attempt.Id,
                QuestionId = questions[i].Id,
                OptionId = chosen.Id,
                CreatedAt = attempt.SubmittedAt ?? now,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ══════════════════════════════════════════════════════════ 8. MOLIYA

    private async Task SeedFinanceAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        db.Tariffs.Add(NewTariff("ATF standart tarifi", 600_000m, courseId: _course.Id, groupId: null, today));
        db.Tariffs.Add(NewTariff("ATF-2 kechki tarifi", 700_000m, courseId: null, groupId: _evening.Id, today));

        var discount = new StudentDiscount
        {
            StudentId = _students[5].Id,
            GroupId = _main.Id,
            Kind = DiscountKind.Percent,
            Value = 20m,
            ValidFrom = today.AddMonths(-3),
            Reason = "Ko'p farzandli oila",
        };

        discount.Validate();
        db.StudentDiscounts.Add(discount);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var current = BillingPeriod.FromDate(today);
        var prev = current.AddMonths(-1);
        var older = current.AddMonths(-2);

        // 1) To'liq to'lagan.
        Pay(_students[0], older, 600_000m, 600_000m);
        Pay(_students[0], prev, 600_000m, 600_000m);
        Pay(_students[0], current, 600_000m, 600_000m);

        // 2) QISMAN to'lagan — qolgani 300 000 (chegaradan past).
        Pay(_students[1], older, 600_000m, 600_000m);
        Pay(_students[1], prev, 600_000m, 600_000m);
        Pay(_students[1], current, 600_000m, 300_000m);

        // ════════════════════════════════════════════════════════════
        // 3) 🔴 QARZDOR — BLOKLASH CHEGARASIDAN YUQORI
        //
        // Chegara standart holatda 540 000 so'm, qamrov — `Video`.
        // Uch oy to'lanmagan = 1 800 000 so'm qarz, ya'ni bu o'quvchi
        // video darsni VA yozuvni ocha olmaydi (403 + sabab matni).
        //
        // ★ NIMA UCHUN KERAK: to'lov darvozasi ko'rinmas xususiyat —
        //   qarzdor bo'lmasa u HECH QACHON ishga tushmaydi va uning
        //   ishlashini ham, buzilganini ham bilib bo'lmaydi.
        // ════════════════════════════════════════════════════════════
        Pay(_students[2], older, 600_000m, 0m);
        Pay(_students[2], prev, 600_000m, 0m);
        Pay(_students[2], current, 600_000m, 0m);

        // 4) Qarzi bor, LEKIN chegaradan past (400 000) — bloklanmaydi.
        Pay(_students[3], older, 600_000m, 600_000m);
        Pay(_students[3], prev, 600_000m, 600_000m);
        Pay(_students[3], current, 600_000m, 200_000m);

        // 5) KECHIRILGAN oy.
        Pay(_students[4], older, 600_000m, 600_000m);
        Pay(_students[4], prev, 600_000m, 600_000m);
        var waived = Pay(_students[4], current, 600_000m, 0m);
        waived.Waive(now, _academic.Id);

        // 6) CHEGIRMA qo'llangan oy: 600 000 − 20% = 480 000.
        Pay(_students[5], prev, 600_000m, 480_000m, discountAmount: 120_000m);
        Pay(_students[5], current, 600_000m, 0m, discountAmount: 120_000m);

        Pay(_students[6], current, 600_000m, 600_000m);
        Pay(_students[8], current, 700_000m, 700_000m);
        Pay(_students[9], current, 700_000m, 350_000m);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        db.PaymentAudits.Add(PaymentAudit.Money(
            "Payment",
            "waive",
            waived.Id,
            _students[4].Id,
            waived.Amount,
            0m,
            now,
            _academic.Id,
            "Oy kechirildi: o'quvchi oilaviy sabab bilan darslarga kelmadi."));

        // Balans — ortiqcha to'lov keyingi oyga o'tadi.
        var account = new StudentAccount { StudentId = _students[0].Id };
        account.Deposit(150_000m, now.AddDays(-3));
        db.StudentAccounts.Add(account);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static Tariff NewTariff(string name, decimal amount, long? courseId, long? groupId, DateOnly from)
    {
        var tariff = new Tariff
        {
            Name = name,
            Amount = amount,
            LessonsCount = 8,
            CourseId = courseId,
            GroupId = groupId,
            ActiveFrom = from.AddMonths(-6),
        };

        tariff.Validate();
        return tariff;
    }

    /// <summary>
    /// Oylik hisob-kitob qatori (+ to'langan bo'lsa tranzaksiya).
    ///
    /// ⚠️ <c>Amount = BaseAmount − DiscountAmount</c> — bazada CHECK
    /// cheklovi bor, ya'ni bu formula buzilsa seeding YIQILADI. Ataylab:
    /// moliya hisoboti aynan shu uch ustunga tayanadi.
    /// </summary>
    private Payment Pay(
        User student,
        BillingPeriod period,
        decimal baseAmount,
        decimal paid,
        decimal discountAmount = 0m)
    {
        var groupId = student == _students[8] || student == _students[9]
            ? _evening.Id
            : _main.Id;

        var amount = baseAmount - discountAmount;

        var payment = new Payment
        {
            StudentId = student.Id,
            GroupId = groupId,
            Period = period.ToString(),
            BaseAmount = baseAmount,
            DiscountAmount = discountAmount,
            Amount = amount,
            PaidAmount = paid,
            Status = paid >= amount ? PaymentStatus.Paid
                : paid > 0 ? PaymentStatus.Partial
                : PaymentStatus.Due,
            PaidAt = paid > 0 ? now.AddDays(-10) : null,
            Method = paid > 0 ? PaymentMethod.Cash : null,
            MarkedById = paid > 0 ? _academic.Id : null,
        };

        payment.Validate();
        db.Payments.Add(payment);

        if (paid > 0)
        {
            db.PaymentTransactions.Add(new PaymentTransaction
            {
                StudentId = student.Id,
                GroupId = groupId,
                Kind = PaymentTransactionKind.Payment,
                Amount = paid,
                ReceiptNo = string.Create(
                    CultureInfo.InvariantCulture,
                    $"DEMO-{student.Id:D3}-{period}"),
                Method = PaymentMethod.Cash,
                Note = period + " oyi uchun",
                ActorId = _academic.Id,
                CreatedAt = now.AddDays(-10),
            });
        }

        return payment;
    }

    // ═════════════════════════════════════════════════════════ 9. YOZISHMA

    private async Task SeedConversationsAsync(CancellationToken ct)
    {
        // ---- GURUH CHATI: USTOZ kanali ----
        var t0 = now.AddDays(-3);

        Post(_main, GroupChatChannel.Teacher, _teacher1, "Assalomu alaykum! Ertangi darsga 12-betdagi mashqni tayyorlab keling.", t0);
        Post(_main, GroupChatChannel.Teacher, _students[0], "Va alaykum assalom, xo'p bo'ladi ustoz.", t0.AddMinutes(12));
        Post(_main, GroupChatChannel.Teacher, _students[4], "Ustoz, mashq daftarga yozilsinmi yoki kitobga?", t0.AddMinutes(20));
        Post(_main, GroupChatChannel.Teacher, _teacher1, "Daftarga yozing, kitobni toza qoldiring.", t0.AddMinutes(25));
        Post(_main, GroupChatChannel.Teacher, _students[1], "Rahmat!", t0.AddMinutes(31));

        // ---- GURUH CHATI: KURATOR kanali ----
        var c0 = now.AddDays(-2);

        Post(_main, GroupChatChannel.Curator, _curator1, "Bugungi uy vazifasini kim topshirmagan bo'lsa, kechgacha yuborsin.", c0);
        Post(_main, GroupChatChannel.Curator, _students[2], "Kurator, men bugun yuboraman.", c0.AddMinutes(40));
        Post(_main, GroupChatChannel.Curator, _curator1, "Yaxshi, kutaman.", c0.AddMinutes(45));
        Post(_main, GroupChatChannel.Curator, _students[3], "Menikini qabul qildingizmi?", c0.AddHours(3));

        Post(_evening, GroupChatChannel.Teacher, _teacher2, "Kechki guruh, ertaga dars 19:00 da.", now.AddDays(-1));
        Post(_evening, GroupChatChannel.Teacher, _students[8], "Qabul qilindi.", now.AddDays(-1).AddMinutes(15));

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // ---- BIRIKTIRMALI XABAR (R16b) ----
        var photo = await media.ImageAsync("group-chat", 1_080, 720, 5, ct).ConfigureAwait(false);

        var withFile = GroupChatMessage.CreateWithAttachments(
            _main.Id, GroupChatChannel.Teacher, _teacher1.Id, _teacher1.FullName,
            _teacher1.Role, "Mana, taxta suratini yubordim.", attachmentCount: 1,
            t0.AddMinutes(35));

        db.GroupChatMessages.Add(withFile);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var chatFile = new GroupChatAttachment
        {
            MessageId = withFile.Id,
            Kind = AttachmentKind.Image,
            Position = 1,
            ObjectKey = photo.ObjectKey,
            ContentType = photo.ContentType,
            FileName = "taxta.png",
            SizeBytes = photo.SizeBytes,
        };

        chatFile.Validate();
        db.GroupChatAttachments.Add(chatFile);

        // ---- O'QILGANLIK ----
        // ⚠️ Ataylab FAQAT bir qismi o'qilgan: o'qilmaganlar soni noldan
        //    farqli bo'lsa, chat ro'yxatidagi belgini ko'rish mumkin.
        var lastTeacherId = await db.GroupChatMessages
            .Where(m => m.GroupId == _main.Id && m.Channel == GroupChatChannel.Teacher)
            .OrderByDescending(m => m.Id)
            .Select(m => m.Id)
            .FirstAsync(ct)
            .ConfigureAwait(false);

        db.GroupChatReads.Add(new GroupChatRead
        {
            GroupId = _main.Id,
            Channel = GroupChatChannel.Teacher,
            UserId = _teacher1.Id,
            LastReadMessageId = lastTeacherId,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        SeedDirectMessages();

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private void Post(Group group, GroupChatChannel channel, User sender, string body, DateTimeOffset at) =>
        db.GroupChatMessages.Add(GroupChatMessage.Create(
            group.Id, channel, sender.Id, sender.FullName, sender.Role, body, at));

    /// <summary>
    /// Shaxsiy yozishma (kurator ↔ o'quvchi) va DARSGA BOG'LANGAN savollar.
    ///
    /// 🔴 SAVOLLAR NAVBATI AYNAN SHU YERDAN TO'LADI: navbat
    /// <c>StaffId = xodim</c> VA <c>ModuleLessonId != null</c> VA
    /// <c>SenderId = StudentId</c> shartiga tayanadi (o'quvchi YOZGAN
    /// xabar). Xodimning javobi ham dars konteksti bilan saqlanadi, lekin
    /// u savol emas — shuning uchun javob berilgani navbatda pastga tushadi.
    /// </summary>
    private void SeedDirectMessages()
    {
        var s1 = _students[0];
        var s3 = _students[2];
        var s5 = _students[4];

        // Oddiy suhbat — darsga bog'lanmagan.
        Dm(s1, _curator1, s1, null, "Kurator, ertangi darsga kechikaman, avtobusim kech qatnaydi.",
            now.AddDays(-2));
        Dm(s1, _curator1, _curator1, null, "Tushunarli, ustozga aytib qo'yaman. Kirganingizda xabar bering.",
            now.AddDays(-2).AddMinutes(20));
        Dm(s1, _curator1, s1, null, "Rahmat!", now.AddHours(-5));

        // ★ JAVOBSIZ SAVOL — navbatning TEPASIDA turadi.
        Dm(s3, _curator1, s3, _lessons[3].Id,
            "Fatha bilan kasra farqini tushunmadim. Videoning 8-daqiqasidagi so'zni "
            + "qanday o'qiymiz?", now.AddHours(-3));

        // ★ JAVOB BERILGAN savol — navbatda pastda turadi.
        Dm(s5, _curator1, s5, _lessons[1].Id, "Xo harfining nuqtasi ustidami yoki ostida?",
            now.AddDays(-1));
        Dm(s5, _curator1, _curator1, _lessons[1].Id, "Ustida. Videoning 4-daqiqasida ko'rsatilgan.",
            now.AddDays(-1).AddMinutes(35));

        // Ustoz bilan yozishma — kurator paritetini tekshirish uchun.
        Dm(_students[1], _teacher1, _students[1], null, "Ustoz, dars yozuvini ko'ra olmayapman.",
            now.AddHours(-8));
        Dm(_students[1], _teacher1, _teacher1, null, "Tekshiraman, kechga qadar ochib qo'yaman.",
            now.AddHours(-7));
    }

    private void Dm(User student, User staff, User sender, long? lessonId, string body, DateTimeOffset at) =>
        db.DirectMessages.Add(DirectMessage.Create(
            student.Id, staff.Id, sender.Id, lessonId, body, at));

    // ═══════════════════════════════════════════════════════ 10. QOLGANLARI

    private async Task SeedExtrasAsync(CancellationToken ct)
    {
        // ---- BILDIRISHNOMALAR ----
        // ⚠️ O'QILMAGANLARI kerak: qo'ng'iroq belgisidagi son faqat
        //    `ReadAt IS NULL` qatorlardan hisoblanadi.
        db.Notifications.Add(Notification.Create(
            _students[0].Id, NotificationKind.SubmissionGraded,
            "Vazifangiz baholandi",
            "«Fatha va Kasra — yozma mashq» uchun 9/10 ball qo'yildi.",
            _courseAssignment.Id, now.AddHours(-4)));

        db.Notifications.Add(Notification.Create(
            _students[1].Id, NotificationKind.SubmissionGraded,
            "Vazifangiz baholandi",
            "«1-dars uy vazifasi» uchun 5/5 ball qo'yildi. Ustoz izohini o'qing.",
            _groupAssignment.Id, now.AddDays(-2)));

        db.Notifications.Add(Notification.Create(
            _students[2].Id, NotificationKind.SubmissionGraded,
            "Vazifa qayta topshirishga ochildi",
            "Kurator javobingizni qayta yuborishni so'radi.",
            _groupAssignment.Id, now.AddDays(-1)));

        var read = Notification.Create(
            _students[0].Id, NotificationKind.SubmissionGraded,
            "Vazifangiz baholandi",
            "«1-dars uy vazifasi» tekshirildi.",
            _groupAssignment.Id, now.AddDays(-3));

        read.MarkRead(now.AddDays(-3).AddHours(1));
        db.Notifications.Add(read);

        // ---- O'QUVCHI IZOHLARI (profil paneli) ----
        db.StudentNotes.Add(StudentNote.Create(
            _students[2].Id, _curator1.Id, _main.Id,
            "Uy vazifasini muntazam kechiktiryapti. Ota-onasi bilan gaplashildi, "
            + "kechqurun 19:00 dan keyin bo'sh bo'ladi.",
            now.AddDays(-4)));

        db.StudentNotes.Add(StudentNote.Create(
            _students[0].Id, _teacher1.Id, _main.Id,
            "Talaffuzi guruhda eng yaxshisi. Musobaqaga tayyorlash mumkin.",
            now.AddDays(-6)));

        // ---- VIDEO KO'RILGANLIGI (gating va progress) ----
        Watched(_students[0], _lessons[0], now.AddDays(-6));
        Watched(_students[0], _lessons[1], now.AddDays(-4));
        Watched(_students[1], _lessons[0], now.AddDays(-5));

        // ★ QO'LDA OCHIB BERILGAN dars: o'quvchi shartni bajarmagan, lekin
        //   kurator ruxsat bergan. Gating'ning "override" yo'li shu bilan
        //   ko'rinadi.
        var forced = new LessonProgress
        {
            StudentId = _students[2].Id,
            ModuleLessonId = _lessons[1].Id,
            CreatedAt = now.AddDays(-2),
        };

        forced.SetOverride(true, "Kasal bo'lgani uchun oldingi dars talab qilinmadi.",
            _curator1.Id, now.AddDays(-2));

        db.LessonProgress.Add(forced);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private void Watched(User student, ModuleLesson lesson, DateTimeOffset at)
    {
        var progress = new LessonProgress
        {
            StudentId = student.Id,
            ModuleLessonId = lesson.Id,
            CreatedAt = at,
        };

        progress.MarkVideoWatched(at);
        db.LessonProgress.Add(progress);
    }
}
