using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Media;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.IntegrationTests.Bootstrap;

/// <summary>
/// `Seed__Demo=true` bilan ko'tariladigan fixture — namunaviy ma'lumot
/// AYNAN ilova ishga tushganda yoziladi (`DbInitializer` -> `DemoDataSeeder`).
///
/// ⚠️ Ombor ATAYLAB bo'sh qoldiriladi (odatiy fixture qoidasi): fayl
/// qatorlari baribir yaratilishi kerak — bu ham tekshiriladigan xulq.
/// </summary>
public sealed class DemoSeedApiFactory : ZinnurApiFactory
{
    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings() =>
    [
        new(DemoDataSeeder.EnabledKey, "true"),
    ];
}

/// <summary>
/// ========================================================================
/// NAMUNAVIY MA'LUMOT — HAR BIR XUSUSIYAT UCHUN MA'LUMOT BORMI
/// ========================================================================
///
/// ★ NIMA UCHUN BU TESTLAR BOR: seeder "kompilyatsiya bo'ldi" degani
/// hech nima anglatmaydi. U yozadigan ma'lumot MA'LUM XUSUSIYATLARNI
/// tekshirish uchun kerak, va aynan o'sha bog'liqlik jimgina yo'qoladi:
/// masalan qarzdorning qarzi chegaradan pastga tushib qolsa, to'lov
/// darvozasi HECH QACHON ishga tushmaydi va buni hech kim payqamaydi
/// (ekranda hamma narsa "normal" ko'rinadi).
///
/// Shuning uchun bu yerda qatorlar SONI emas, HOLATLAR QAMROVI
/// tekshiriladi.
/// </summary>
public sealed class DemoDataSeederTests(DemoSeedApiFactory factory)
    : IClassFixture<DemoSeedApiFactory>
{
    /// <summary>
    /// 🔴 ENG MUHIM TEKSHIRUV: har bir namunaviy hisobga KIRIB BO'LADIMI.
    ///
    /// Kirish faqat telefon + Telegram kodi bilan. `PhoneNormalized`
    /// yoki `TelegramId` yo'q profil CRM'da mutlaqo normal ko'rinadi,
    /// lekin unga hech qachon kirib bo'lmaydi — ya'ni tekshiruvchi
    /// "o'quvchi ko'zi bilan" ekranni umuman ko'ra olmaydi.
    /// </summary>
    [Fact]
    public async Task EveryDemoUser_CanReceiveLoginCode()
    {
        var broken = await factory.WithDbAsync(db => db.Users
            .Where(u => u.PhoneNormalized == null || u.TelegramId == null)
            .Select(u => u.Email)
            .ToListAsync());

        broken.Should().BeEmpty(
            "telefonsiz yoki Telegram'siz profil kirish kodini OLA OLMAYDI — "
            + "u namunaviy ma'lumotda ko'rinadi, lekin ishlatib bo'lmaydi");

        var users = await factory.CountUsersAsync();
        users.Should().BeGreaterThan(15, "ssenariyda xodimlar + 12 o'quvchi bor");
    }

    /// <summary>
    /// Davomatning BARCHA holati, jumladan BELGILANMAGAN (qatorsiz).
    ///
    /// ★ Beshinchi holat — qatorning YO'QLIGI. Jurnalda u "yo'q" dan
    /// boshqacha ko'rinishi kerak; hamma o'quvchiga qator yozilsa bu
    /// farq umuman sinovdan o'tmasdi.
    /// </summary>
    [Fact]
    public async Task Attendance_CoversEveryStatus_AndLeavesSomeoneUnmarked()
    {
        var statuses = await factory.WithDbAsync(db => db.Attendances
            .Select(a => a.Status)
            .Distinct()
            .ToListAsync());

        statuses.Should().Contain(
            [AttendanceStatus.Present, AttendanceStatus.Late,
             AttendanceStatus.Partial, AttendanceStatus.Absent]);

        var unmarked = await factory.WithDbAsync(db =>
            (from session in db.LiveSessions
             where session.Status == SessionStatus.Ended
             from member in db.GroupMembers.Where(m => m.GroupId == session.GroupId)
             where !db.Attendances.Any(a =>
                 a.SessionId == session.Id && a.StudentId == member.StudentId)
             select session.Id).CountAsync());

        unmarked.Should().BeGreaterThan(0,
            "kamida bitta o'quvchi ATAYLAB belgilanmagan holda qoldirilgan");

        var manual = await factory.WithDbAsync(db =>
            db.Attendances.CountAsync(a => a.IsManual && a.Reason != null));

        manual.Should().BeGreaterThan(0, "qo'lda sabab bilan belgilangan yo'qlik ham kerak");
    }

    /// <summary>
    /// 🔴 TO'LOV DARVOZASI: qarzi CHEGARADAN OSHGAN o'quvchi bo'lishi shart.
    ///
    /// Bu — ko'rinmas xususiyat: qarzdor bo'lmasa bloklash kodi HECH
    /// QACHON ishga tushmaydi. Chegara standart holatda 540 000 so'm.
    /// </summary>
    [Fact]
    public async Task Payments_ContainDebtorAboveBlockThreshold()
    {
        const decimal defaultThreshold = 540_000m;

        var debts = await factory.WithDbAsync(db => db.Payments
            .Where(p => p.Status == PaymentStatus.Due || p.Status == PaymentStatus.Partial)
            .GroupBy(p => p.StudentId)
            .Select(g => g.Sum(p => p.Amount - p.PaidAmount))
            .ToListAsync());

        debts.Should().Contain(debt => debt > defaultThreshold,
            "video/yozuv darvozasi faqat chegaradan OSHGAN qarzda ishga tushadi");

        debts.Should().Contain(debt => debt > 0 && debt <= defaultThreshold,
            "chegaradan PAST qarz ham kerak — aks holda 'qarzi bor, lekin "
            + "bloklanmaydi' holati sinovdan o'tmasdi");

        var statuses = await factory.WithDbAsync(db => db.Payments
            .Select(p => p.Status).Distinct().ToListAsync());

        statuses.Should().Contain(
            [PaymentStatus.Due, PaymentStatus.Partial, PaymentStatus.Paid, PaymentStatus.Waived]);
    }

    /// <summary>
    /// Ko'p qismli video pleer uchun ma'lumot: bitta darsda bir nechta
    /// <c>LessonAsset</c>, har biri o'z <c>Position</c> va sarlavhasi bilan.
    /// </summary>
    [Fact]
    public async Task Lessons_HaveMultiPartVideo()
    {
        var parts = await factory.WithDbAsync(db => db.LessonAssets
            .Where(a => a.Kind == LessonAssetKind.Video)
            .GroupBy(a => a.LessonId)
            .Select(g => new { LessonId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstAsync());

        parts.Count.Should().BeGreaterThanOrEqualTo(3,
            "ko'p qismli pleer faqat 2+ qismda oddiy pleerdan farq qiladi");

        var titled = await factory.WithDbAsync(db => db.LessonAssets
            .CountAsync(a => a.LessonId == parts.LessonId && a.Title != null));

        titled.Should().Be(parts.Count, "har bir qismning nomi bo'lishi kerak");

        var images = await factory.WithDbAsync(db =>
            db.LessonAssets.CountAsync(a => a.Kind == LessonAssetKind.Image));

        images.Should().BeGreaterThan(0, "imtihon darsi uchun rasm ham kerak");
    }

    /// <summary>
    /// Savollar navbati (R40): darsga bog'langan, O'QUVCHI yozgan va hali
    /// JAVOBSIZ xabar. Navbat aynan shu uch shartga tayanadi.
    /// </summary>
    [Fact]
    public async Task Questions_QueueHasUnansweredLessonQuestion()
    {
        var unanswered = await factory.WithDbAsync(db => db.DirectMessages
            .Where(m => m.ModuleLessonId != null
                     && m.SenderId == m.StudentId
                     && !db.DirectMessages.Any(r => r.StudentId == m.StudentId
                                                 && r.StaffId == m.StaffId
                                                 && r.SenderId == m.StaffId
                                                 && r.Id > m.Id))
            .CountAsync());

        unanswered.Should().BeGreaterThan(0);

        var answered = await factory.WithDbAsync(db => db.DirectMessages
            .CountAsync(m => m.ModuleLessonId != null && m.SenderId != m.StudentId));

        answered.Should().BeGreaterThan(0, "javob berilgan savol navbatda PASTDA turadi");
    }

    /// <summary>Guruh filtrlari uchun: kategoriya, tur va arxiv holati.</summary>
    [Fact]
    public async Task Groups_CoverFilters()
    {
        var groups = await factory.WithDbAsync(db => db.Groups
            .Select(g => new { g.Type, g.IsActive, g.CategoryId, Days = g.Weekdays.Count })
            .ToListAsync());

        groups.Should().Contain(g => !g.IsActive, "arxiv filtri uchun nofaol guruh kerak");
        groups.Should().Contain(g => g.Type == GroupType.Individual);
        groups.Should().Contain(g => g.Type == GroupType.Curator);
        groups.Should().OnlyContain(g => g.CategoryId != null, "kategoriya ustuni bo'sh qolmasin");
        groups.Should().Contain(g => g.Days >= 2, "haftalik jadval to'ldirilgan bo'lishi kerak");

        var inactiveCategory = await factory.WithDbAsync(db =>
            db.GroupCategories.CountAsync(c => !c.IsActive));

        inactiveCategory.Should().BeGreaterThan(0);
    }

    /// <summary>Javoblar: baholanmagan, baholangan va qayta ochilgan.</summary>
    [Fact]
    public async Task Submissions_CoverEveryState()
    {
        var rows = await factory.WithDbAsync(db => db.Submissions
            .Select(s => new { s.Status, Graded = s.Score != null, s.AllowResubmit, s.IsLate })
            .ToListAsync());

        rows.Should().Contain(s => s.Status == SubmissionStatus.Submitted && !s.Graded);
        rows.Should().Contain(s => s.Status == SubmissionStatus.Graded && s.Graded);
        rows.Should().Contain(s => s.AllowResubmit);
        rows.Should().Contain(s => s.IsLate);

        var files = await factory.WithDbAsync(db => db.SubmissionFiles.CountAsync());
        var feedback = await factory.WithDbAsync(db => db.SubmissionFeedbackFiles.CountAsync());

        files.Should().BeGreaterThan(0, "o'quvchi fayli");
        feedback.Should().BeGreaterThan(0, "ustoz/kurator javob fayli");
    }

    /// <summary>Yozuvlar: ko'rinadigan, YASHIRILGAN va sifat nazorati xulosasi.</summary>
    [Fact]
    public async Task Recordings_CoverVisibilityAndReview()
    {
        var recordings = await factory.WithDbAsync(db => db.SessionRecordings
            .Select(r => new { r.Status, r.IsVisibleToStudents })
            .ToListAsync());

        recordings.Should().Contain(r => r.Status == RecordingStatus.Completed && r.IsVisibleToStudents);
        recordings.Should().Contain(r => r.Status == RecordingStatus.Completed && !r.IsVisibleToStudents);
        recordings.Should().Contain(r => r.Status == RecordingStatus.Failed);

        var verdicts = await factory.WithDbAsync(db => db.SessionReviews
            .Select(r => r.Verdict).ToListAsync());

        verdicts.Should().Contain(SessionReviewVerdict.HasIssue);
        verdicts.Should().Contain(SessionReviewVerdict.Approved);

        var unread = await factory.WithDbAsync(db =>
            db.Notifications.CountAsync(n => n.ReadAt == null));

        unread.Should().BeGreaterThan(0, "qo'ng'iroq belgisi faqat o'qilmaganlarni sanaydi");
    }

    /// <summary>
    /// 🔴 IDEMPOTENTLIK: ikkinchi chaqiruv hech nima o'zgartirmaydi.
    ///
    /// Konteyner har qayta ishga tushganda seeder ishlaydi. Marker
    /// tekshiruvi buzilsa, har restart ma'lumotni IKKILANTIRARDI va buni
    /// faqat bir necha kundan keyin, ro'yxatlar tushunarsiz bo'lib
    /// qolganda payqashardi.
    /// </summary>
    [Fact]
    public async Task Seeding_Twice_ChangesNothing()
    {
        var before = await SnapshotAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var media = scope.ServiceProvider.GetService<IMediaStorage>();
        var submissions = scope.ServiceProvider.GetService<ISubmissionStorage>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<DemoDataSeederTests>();

        await DemoDataSeeder.SeedAsync(db, hasher, media, submissions, logger);

        var after = await SnapshotAsync();

        after.Should().BeEquivalentTo(before);
    }

    private Task<(long Users, long Groups, long Payments, long Sessions, long Messages)> SnapshotAsync() =>
        factory.WithDbAsync(async db => (
            await db.Users.LongCountAsync(),
            await db.Groups.LongCountAsync(),
            await db.Payments.LongCountAsync(),
            await db.LiveSessions.LongCountAsync(),
            await db.GroupChatMessages.LongCountAsync()));
}

/// <summary>
/// ========================================================================
/// 🔴 KALIT O'CHIQ BO'LSA — HECH NIMA YOZILMAYDI
/// ========================================================================
///
/// Bu — ishlab chiqarish bazasini himoya qiladigan ASOSIY shart, va u
/// ALOHIDA fixture talab qiladi: odatiy `ZinnurApiFactory` da
/// `Seed:Demo` umuman berilmaydi, ya'ni bu test AYNI paytda "standart
/// qiymat `false` mi?" degan savolga ham javob beradi.
///
/// ⚠️ Bu test qolgan 600+ integratsion testni ham qo'riqlaydi: kalit
/// tasodifan yoqilib qolsa, ularning har biri o'z bazasida 18 ta
/// begona foydalanuvchi va 5 ta guruh bilan uchrashardi.
/// </summary>
public sealed class DemoSeedDisabledTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    [Fact]
    public async Task WithoutSwitch_OnlyBootstrapRowsExist()
    {
        var users = await factory.WithDbAsync(db => db.Users
            .Select(u => u.Email)
            .ToListAsync());

        users.Should().NotContain(DemoDataSeeder.AcademicEmail,
            "namunaviy ma'lumot faqat OSHKOR kalit bilan yoziladi");

        users.Count.Should().BeLessThanOrEqualTo(DemoDataSeeder.MaxUsersForDemo);

        var groups = await factory.WithDbAsync(db => db.Groups.CountAsync());
        groups.Should().Be(1, "`DbInitializer` faqat bitta demo guruh yozadi");
    }
}
