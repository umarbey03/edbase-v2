using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// O'QUVCHI PROFILI AGREGATI — <c>GET /api/v1/users/{id}/profile</c>
/// ========================================================================
///
/// NIMA UCHUN AYNAN BU TESTLAR: bu endpoint bitta javobda o'quvchining
/// shaxsiy ma'lumoti, PULI, o'quv natijalari va xodimlarning ICHKI
/// izohlarini birlashtiradi. Ya'ni ruxsatdagi bitta xato darhol eng nozik
/// ma'lumotni oshkor qiladi:
///
///   • ustoz o'quvchining qarzini ko'rib qolsa — talab BUZILADI
///     ("ustoz o'quvchining qarzini bilishi kerak emas");
///   • o'quvchi o'zi haqidagi ichki izohni ("otasi bilan gaplashildi")
///     ko'rsa — xodimlar bunday yozuvni umuman yozmay qo'yadi;
///   • begona guruh ustozi profilni ochsa — butun markazning ma'lumoti
///     har bir ustozga ochiq bo'lardi;
///   • ustoz o'quvchining telefonini ko'rsa — talab R27 buziladi
///     (*"student kontakt ma'lumotlari teacherga ko'rinmasligi kerak"*), va
///     amalda markazning mijoz bazasi har bir ustozda nusxalanardi.
///
/// Shu sababli tekshiruvlar JONLI JAVOB ustida: kod o'qib "shunday
/// yozilgan" deb ishonish yetarli emas.
/// </summary>
public sealed class UserProfileEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ================================================================= 1) MAZMUN

    /// <summary>
    /// O'quv bo'limi HAMMA blokni ko'radi va raqamlar moliya moduli bilan
    /// mos keladi (qisman to'langan oy — qolgan qismi bo'yicha qarz).
    /// </summary>
    [Fact]
    public async Task Profile_ForAcademic_ReturnsEveryBlock()
    {
        var world = await ProfileWorldBuilder.CreateWithFinanceAsync(factory, "prof-full");
        await ProfileWorldBuilder.AddTwoEndedSessionsAsync(factory, world.GroupId, world.Student.Id);
        await ProfileWorldBuilder.AddSubmissionWithFileAsync(factory, world.GroupId, world.Student.Id);

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var profile = await ProfileWorldBuilder.GetProfileAsync(admin, world.Student.Id);

        // --- shaxsiy
        profile.User.Id.Should().Be(world.Student.Id);
        profile.User.Role.Should().Be(nameof(UserRole.Student));
        profile.Telegram.Linked.Should().BeFalse();

        // --- guruhlar
        profile.Groups.Should().HaveCount(1);
        profile.Groups[0].GroupId.Should().Be(world.GroupId);
        profile.Groups[0].Status.Should().Be(nameof(MemberStatus.Active));
        profile.Groups[0].TeacherName.Should().NotBeNullOrEmpty("ustoz ismi bitta so'rovda olinadi");
        profile.Groups[0].LeftAt.Should().BeNull("faol a'zolikda chiqish vaqti bo'lmaydi");

        // --- moliya
        profile.Finance.Should().NotBeNull();
        profile.Finance!.TotalPaid.Should().Be(200_000m);
        profile.Finance.TotalDue.Should().Be(ProfileWorldBuilder.MonthlyPrice - 200_000m);
        profile.Finance.Balance.Should().Be(0m);

        var period = profile.Finance.Periods.Should().ContainSingle().Subject;
        period.Month.Should().Be(ProfileWorldBuilder.Period);
        period.Status.Should().Be(nameof(PaymentStatus.Partial));
        period.Outstanding.Should().Be(ProfileWorldBuilder.MonthlyPrice - 200_000m);

        // ★ "Qaysi dars uchun" talabining o'rnini bosadigan son: o'sha oyda
        //   o'tkazilgan darslar soni (per-lesson billing modelda yo'q).
        period.SessionCount.Should().Be(2, "yanvarda ikkita dars YAKUNLANGAN");

        var transaction = profile.Finance.Transactions.Should().ContainSingle().Subject;
        transaction.Kind.Should().Be(nameof(PaymentTransactionKind.Payment));
        transaction.Amount.Should().Be(200_000m);
        profile.Finance.HasMoreTransactions.Should().BeFalse();

        // --- o'quv natijalari
        profile.Study.Assignments.Should().ContainSingle();
        profile.Study.Assignments[0].Score.Should().Be(8m);
        profile.Study.Assignments[0].MaxScore.Should().Be(10m);
        profile.Study.Assignments[0].IsLate.Should().BeTrue();
        profile.Study.Assignments[0].FileCount.Should().Be(1);

        profile.Study.Attendance.Total.Should().Be(2);
        profile.Study.Attendance.Present.Should().Be(1);
        profile.Study.Attendance.Percent.Should().Be(50m);

        // --- izohlar (hozircha bo'sh, lekin `null` EMAS)
        profile.Notes.Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>
    /// A'zolik holatlari javobda ko'rinadi: chiqarilgan va pauzadagi
    /// a'zolik ham qaytadi (talab: "qaysilaridan chiqarib yuborilgan").
    ///
    /// ⚠️ TARTIB 2026-08-22 DA O'ZGARDI. Ilgari o'quvchi ikkinchi guruhga
    /// HAM qo'shilib, keyin o'sha yerdan chiqarilardi — 2026-08-17 dan bu
    /// 409 beradi ("o'quvchi bir vaqtda faqatgina bitta o'qituvchi
    /// guruhida bo'lishi mumkin"). Endi ssenariy HAQIQIY hayotdagi
    /// ketma-ketlikni takrorlaydi: birinchi guruhdan CHIQADI, ikkinchisiga
    /// O'TADI, u yerda MUZLATILADI.
    ///
    /// ★ Tekshiruvning mazmuni o'zgarmadi — profilda baribir bitta
    ///   <c>Stopped</c> va bitta <c>Paused</c> a'zolik ko'rinishi kerak.
    ///   Faqat qaysi guruh qaysi holatda ekani almashdi.
    /// </summary>
    [Fact]
    public async Task Profile_ShowsStoppedAndPausedMemberships()
    {
        var world = await WorldBuilder.CreateAsync(factory, "prof-holat");
        var second = await WorldBuilder.CreateAsync(factory, "prof-ikki");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        // 1) Birinchi guruhdan chiqaramiz (sabab MAJBURIY).
        var remove = await WorldBuilder.RemoveMemberAsync(
            admin, world.GroupId, world.Student.Id);
        remove.IsSuccessStatusCode.Should().BeTrue(await WorldBuilder.Body(remove));

        // 2) Ikkinchisiga qo'shamiz — endi boshqa FAOL a'zolik yo'q.
        var add = await admin.PostAsJsonAsync(
            $"/api/v1/groups/{second.GroupId}/members", new { studentId = world.Student.Id });
        add.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(add));

        // 3) Va u yerda muzlatamiz (muddat bilan).
        var pause = await WorldBuilder.PauseMemberAsync(
            admin, second.GroupId, world.Student.Id, new DateOnly(2030, 1, 1));
        pause.IsSuccessStatusCode.Should().BeTrue(await WorldBuilder.Body(pause));

        var profile = await ProfileWorldBuilder.GetProfileAsync(admin, world.Student.Id);

        profile.Groups.Should().HaveCount(2);

        var stopped = profile.Groups.Find(g => g.GroupId == world.GroupId)!;
        stopped.Status.Should().Be(nameof(MemberStatus.Stopped));
        stopped.LeftAt.Should().NotBeNull("chiqarilgan a'zolikda taxminiy chiqish vaqti bo'ladi");

        var paused = profile.Groups.Find(g => g.GroupId == second.GroupId)!;
        paused.Status.Should().Be(nameof(MemberStatus.Paused));
        paused.PausedUntil.Should().Be(new DateOnly(2030, 1, 1),
            "pauza muddati SOYA ustundan o'qiladi");
    }

    // ================================================================= 2) RUXSAT MATRITSASI

    /// <summary>
    /// 🔴 ENG MUHIM TEKSHIRUV: USTOZ JAVOBIDA MOLIYA BLOKI <c>null</c>.
    ///
    /// Tekshiruv IKKI qatlamli — tiplangan javobda ham, XOM JSON'da ham:
    /// maydon "bor, lekin bo'sh" bo'lib qolsa (masalan kelajakda kimdir
    /// bo'sh obyekt qaytarsa) tiplangan tekshiruv o'tib ketishi mumkin,
    /// lekin qarz summasi baribir simdan o'tgan bo'lardi.
    /// </summary>
    [Fact]
    public async Task Profile_ForOwnTeacher_HidesFinanceCompletely()
    {
        var world = await ProfileWorldBuilder.CreateWithFinanceAsync(factory, "prof-ustoz");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var (status, json) = await ProfileWorldBuilder.GetProfileRawAsync(teacher, world.Student.Id);

        status.Should().Be(HttpStatusCode.OK, json);

        json.Should().Contain("\"finance\":null",
            "moliya bloki javobdan UMUMAN chiqmasligi kerak");

        json.Should().NotContain("540000",
            "oylik summa ustoz javobiga tushmasligi kerak");

        json.Should().NotContain("totalDue",
            "qarz maydoni ustoz javobida bo'lmasligi kerak");

        var profile = await ProfileWorldBuilder.GetProfileAsync(teacher, world.Student.Id);

        profile.Finance.Should().BeNull();

        // Qolgan bloklar esa ustoz uchun KERAK va ochiq.
        profile.Groups.Should().NotBeEmpty();
        profile.Notes.Should().NotBeNull("ustoz izohlarni ko'rishi kerak");
    }

    /// <summary>
    /// 🔴 R27: USTOZ JAVOBIDA KONTAKT YO'Q — email, telefon, Telegram id
    /// va Telegram nomi.
    ///
    /// Moliya testi bilan AYNI naqsh (xom JSON + tiplangan javob) va AYNI
    /// sabab: bu ma'lumot oddiy JSON'da keladi, ya'ni frontendda yashirish
    /// hech narsani bermaydi — brauzer konsoli yetarli bo'lardi.
    ///
    /// ★ NIMA UCHUN "ISM QOLADI" HAM TEKSHIRILADI: kesish HADDAN TASHQARI
    /// bo'lib ketsa (masalan butun `user` bloki `null` qilinsa) ustozning
    /// jurnali ishlamay qolardi va buni faqat brauzerda sezish mumkin bo'lardi.
    /// </summary>
    [Fact]
    public async Task Profile_ForOwnTeacher_HidesStudentContact()
    {
        var world = await WorldBuilder.CreateAsync(factory, "prof-kontakt");

        // Telegram ham BOG'LANADI: aks holda maydonlar shundoq ham `null`
        // bo'lib, test kesishni emas, bo'sh ma'lumotni tekshirardi.
        var telegramId = ProfileWorldBuilder.NextTelegramId();
        await ProfileWorldBuilder.LinkTelegramAsync(
            factory, world.Student.Id, telegramId, "maxfiy_nom");

        var (email, phone) = await ProfileWorldBuilder.ContactOfAsync(factory, world.Student.Id);
        phone.Should().NotBeNullOrEmpty("dunyo quruvchi o'quvchiga ham raqam beradi");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var (status, json) = await ProfileWorldBuilder.GetProfileRawAsync(teacher, world.Student.Id);

        status.Should().Be(HttpStatusCode.OK, json);

        json.Should().NotContain(email, "o'quvchi emaili ustoz javobiga tushmasligi kerak");
        json.Should().NotContain(phone!, "o'quvchi telefoni ustoz javobiga tushmasligi kerak");
        json.Should().NotContain("maxfiy_nom", "Telegram nomi ham kontakt — u orqali yozib bo'ladi");
        json.Should().NotContain(
            telegramId.ToString(CultureInfo.InvariantCulture),
            "Telegram id ham kontakt");

        var profile = await ProfileWorldBuilder.GetProfileAsync(teacher, world.Student.Id);

        profile.User.Email.Should().BeNull();
        profile.User.Phone.Should().BeNull();
        profile.User.TelegramId.Should().BeNull();
        profile.User.TelegramUsername.Should().BeNull();
        profile.Telegram.TelegramId.Should().BeNull();
        profile.Telegram.Username.Should().BeNull();

        // Ustozning ishi uchun kerak bo'lgani QOLADI.
        profile.User.Id.Should().Be(world.Student.Id);
        profile.User.FullName.Should().NotBeNullOrEmpty();
        profile.Telegram.Linked.Should().BeTrue(
            "\"kira oladimi\" — HOLAT, kontakt emas: u orqali bog'lanib bo'lmaydi");
        profile.Groups.Should().NotBeEmpty();
    }

    /// <summary>
    /// 🔴 R27 NING IKKINCHI YARMI: KURATORDA KONTAKT QOLADI.
    ///
    /// Bu test ATAYLAB "hech narsa buzilmaganini" emas, QAROR ni qulflaydi:
    /// kurator uchun qo'ng'iroq — asosiy amal (dars qoldirgan o'quvchini u
    /// qidiradi). Kimdir kelajakda R27 ni "hamma xodimga yopamiz" deb
    /// kengaytirsa, kuratorning ish oqimi JIMGINA sinardi — shu test uni
    /// to'xtatadi.
    /// </summary>
    [Fact]
    public async Task Profile_ForOwnCurator_IsAllowedWithoutFinanceButWithContact()
    {
        var world = await ProfileWorldBuilder.CreateWithFinanceAsync(factory, "prof-kurator");

        var (email, phone) = await ProfileWorldBuilder.ContactOfAsync(factory, world.Student.Id);

        using var curator = await WorldBuilder.ClientAsync(factory, world.Curator);

        var profile = await ProfileWorldBuilder.GetProfileAsync(curator, world.Student.Id);

        profile.User.Id.Should().Be(world.Student.Id);
        profile.Finance.Should().BeNull();

        profile.User.Email.Should().Be(email);
        profile.User.Phone.Should().Be(phone, "kuratorning ASOSIY amali — qo'ng'iroq");
    }

    /// <summary>🔴 BEGONA guruh ustozi — 403 (butun markaz ma'lumoti ochiq qolmasin).</summary>
    [Fact]
    public async Task Profile_ForForeignTeacher_IsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "prof-oz");
        var other = await WorldBuilder.CreateAsync(factory, "prof-begona");

        using var foreignTeacher = await WorldBuilder.ClientAsync(factory, other.Teacher);

        var (status, json) = await ProfileWorldBuilder.GetProfileRawAsync(
            foreignTeacher, world.Student.Id);

        status.Should().Be(HttpStatusCode.Forbidden, json);
    }

    /// <summary>
    /// O'quvchi O'Z profilini ko'radi, lekin: izohlar YO'Q va to'lov
    /// jurnali YO'Q (oylar esa ko'rinadi — u o'z qarzini bilishi kerak).
    /// </summary>
    [Fact]
    public async Task Profile_ForSelf_HidesNotesAndTransactions()
    {
        var world = await ProfileWorldBuilder.CreateWithFinanceAsync(factory, "prof-ozi");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        // Ustoz izoh yozib qo'yadi — o'quvchi javobida ko'rinmasligi kerak.
        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var note = await teacher.PostAsJsonAsync(
            $"/api/v1/users/{world.Student.Id}/notes",
            new { body = "Ichki eslatma: darsga kech qoladi." });

        note.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(note));

        // Izoh admin javobida BOR.
        (await ProfileWorldBuilder.GetProfileAsync(admin, world.Student.Id))
            .Notes.Should().ContainSingle();

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var (status, json) = await ProfileWorldBuilder.GetProfileRawAsync(student, world.Student.Id);

        status.Should().Be(HttpStatusCode.OK, json);
        json.Should().NotContain("kech qoladi", "ichki izoh o'quvchiga ko'rinmasligi kerak");

        var profile = await ProfileWorldBuilder.GetProfileAsync(student, world.Student.Id);

        profile.Notes.Should().BeNull();
        profile.Finance.Should().NotBeNull("o'quvchi o'z qarzini ko'radi");
        profile.Finance!.Transactions.Should().BeNull("jurnal alohida endpointda");
        profile.Finance.Periods.Should().NotBeEmpty();
    }

    /// <summary>🔴 O'quvchi BOSHQA o'quvchining profilini ko'ra olmaydi.</summary>
    [Fact]
    public async Task Profile_ForAnotherStudent_IsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "prof-boshqa");
        var classmate = await WorldBuilder.AddStudentAsync(factory, world.GroupId, "prof-sinf");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var (status, json) = await ProfileWorldBuilder.GetProfileRawAsync(student, classmate.Id);

        status.Should().Be(HttpStatusCode.Forbidden, json,
            "bitta guruhda o'qish boshqasining profilini ochish huquqini bermaydi");
    }

    /// <summary>Mavjud bo'lmagan profil — 404 (o'quv bo'limi uchun).</summary>
    [Fact]
    public async Task Profile_ForMissingUser_IsNotFound()
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var (status, _) = await ProfileWorldBuilder.GetProfileRawAsync(admin, 99_999_999);

        status.Should().Be(HttpStatusCode.NotFound);
    }

    // ================================================================= 3) OMBOR KALITI

    /// <summary>
    /// 🔴 <c>objectKey</c> JAVOBGA CHIQMAYDI (6-bo'lim, 16-tuzoq).
    ///
    /// Ombor kaliti ichki ma'lumot: u bilan imzolangan havola yasashga
    /// urinish mumkin va u fayl tuzilmasini oshkor qiladi. Profilda faqat
    /// fayllar SONI beriladi.
    /// </summary>
    [Fact]
    public async Task Profile_NeverLeaksSubmissionObjectKey()
    {
        var world = await WorldBuilder.CreateAsync(factory, "prof-fayl");

        var objectKey = await ProfileWorldBuilder.AddSubmissionWithFileAsync(
            factory, world.GroupId, world.Student.Id);

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var (status, json) = await ProfileWorldBuilder.GetProfileRawAsync(admin, world.Student.Id);

        status.Should().Be(HttpStatusCode.OK, json);

        json.Should().NotContain("objectKey", "ombor kaliti maydoni javobda bo'lmasligi kerak");
        json.Should().NotContain(objectKey, "kalitning O'ZI ham javobga tushmasligi kerak");
        json.Should().Contain("\"fileCount\":1", "faqat fayllar soni beriladi");
    }
}
