using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Media;

namespace Zinnur.Infrastructure.Persistence;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// NAMUNAVIY (DEMO) MA'LUMOT — TO'LIQ SSENARIY
/// ════════════════════════════════════════════════════════════════════════
///
/// <see cref="DbInitializer"/> bo'sh bazaga MINIMAL to'plamni yozadi:
/// administrator, bitta ustoz, bitta o'quvchi, bitta guruh. Bu ishga
/// tushirish uchun yetarli, lekin BIRORTA ham ekranni tekshirish uchun
/// yetarli emas: davomat bo'sh, moliya bo'sh, chat bo'sh, filtrlar
/// tanlash uchun hech nima yo'q.
///
/// Bu sinf o'sha minimal dunyoni TO'LIQ o'quv markaziga aylantiradi
/// (batafsil — <see cref="DemoWorld"/>).
///
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 U ISHLAB CHIQARISH BAZASIGA HECH QACHON TUSHMASLIGI KERAK
/// ════════════════════════════════════════════════════════════════════════
///
/// Uch mustaqil qatlam, VA UCHALASI HAM bajarilishi shart:
///
///  1) OSHKOR KALIT — <c>Seed__Demo=true</c> muhit o'zgaruvchisi.
///     Standart qiymat — <c>false</c>. Ya'ni "hech narsa yozmaslik" —
///     harakatsizlikdagi holat; demo ma'lumot faqat kimdir uni ATAYLAB
///     so'raganda paydo bo'ladi.
///
///     ★ NIMA UCHUN "faqat Development" YETARLI EMAS: integratsion
///       testlar ham <c>Development</c> muhitida ishlaydi
///       (<c>ZinnurApiFactory</c>), ya'ni muhit bo'yicha shart 600 dan
///       ortiq testga soxta o'quvchilarni olib kirardi. Va teskarisi:
///       loyiha egasi namunani YANGI SERVERDA ko'rmoqchi, u yerda esa
///       muhit <c>Production</c>.
///
///  2) BAZA HALI BO'SH — foydalanuvchilar soni
///     <see cref="MaxUsersForDemo"/> dan oshmasligi kerak (ya'ni faqat
///     <c>DbInitializer</c> yozgan uchtasi).
///
///     ★ NIMA UCHUN: kalit tasodifan yoqilib qolishi mumkin — eski
///       <c>.env</c> nusxasi, noto'g'ri qatlam, deploy skriptidagi
///       qoldiq. Ishlayotgan markazda 40 ta xodim va 300 ta o'quvchi
///       bor, ya'ni bu shart o'sha tasodifni TO'XTATADI. Xato
///       LOGDA baland ovozda yoziladi — jimgina o'tib ketmaydi.
///
///  3) MARKER — <see cref="AcademicEmail"/> profili allaqachon bormi.
///     Bo'lsa, demo ALLAQACHON yozilgan: ikkinchi ishga tushirish
///     hech nima qilmaydi (idempotentlik).
///
/// ★ TRANZAKSIYA: butun ssenariy BITTA tranzaksiyada yoziladi. Sabab —
///   yarim yozilgan holat markerni qoldirib ketardi (o'quv bo'limi bor,
///   guruhlar yo'q) va keyingi ishga tushirish uni "tayyor" deb hisoblab
///   o'tkazib yuborardi. Endi yiqilish = to'liq qaytarish.
/// </summary>
public static class DemoDataSeeder
{
    /// <summary>Muhit o'zgaruvchisi: <c>Seed__Demo</c> (<c>true</c> bo'lsa yoqiladi).</summary>
    public const string EnabledKey = "Seed:Demo";

    /// <summary>
    /// Marker profil. Bu email bazada bo'lsa — namunaviy ma'lumot yozilgan.
    ///
    /// ★ NIMA UCHUN ALOHIDA "seeded" BELGISI EMAS: qo'shimcha jadval yoki
    /// sozlama kaliti bazani ifloslantirardi va uni ko'rgan odam "bu nima?"
    /// deb so'rardi. Mavjud, ko'rinadigan va MA'NOLI qator — o'quv bo'limi
    /// xodimi — ayni vazifani bajaradi.
    /// </summary>
    public const string AcademicEmail = "academic@zinnur.uz";

    /// <summary>
    /// Namuna yozilishi mumkin bo'lgan eng ko'p foydalanuvchi soni —
    /// <c>DbInitializer</c> ning uchtasi (admin + ustoz + o'quvchi).
    /// </summary>
    public const int MaxUsersForDemo = 3;

    /// <summary>
    /// ⚠️ PAROL BILAN KIRISH YO'Q (2026-08-13 dan). Bu qiymat faqat
    /// <c>PasswordHash</c> ustuni <c>required</c> bo'lgani uchun kerak.
    /// Uni bilgan odam hech qayerga kira olmaydi.
    /// </summary>
    private const string FillerPassword = "Demo!2345";

    /// <summary>Kalit yoqilganmi.</summary>
    public static bool IsEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var raw = configuration[EnabledKey];

        if (string.IsNullOrWhiteSpace(raw)) return false;

        // "1" ham qabul qilinadi: Docker Compose va CI faylida odatiy yozuv.
        return bool.TryParse(raw, out var value)
            ? value
            : string.Equals(raw.Trim(), "1", StringComparison.Ordinal);
    }

    /// <summary>Namunaviy ma'lumotni yozadi (shartlar bajarilsa).</summary>
    /// <param name="db">Baza konteksti.</param>
    /// <param name="hasher">Parol xeshlovchi (ustunni to'ldirish uchun).</param>
    /// <param name="media">Media ombori — sozlanmagan bo'lsa <c>null</c> bo'lishi mumkin.</param>
    /// <param name="submissions">Javob fayllari ombori — ixtiyoriy.</param>
    /// <param name="logger">Log.</param>
    /// <param name="ct">Bekor qilish belgisi.</param>
    public static async Task SeedAsync(
        ApplicationDbContext db,
        IPasswordHasher hasher,
        IMediaStorage? media,
        ISubmissionStorage? submissions,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(hasher);

        // ---- 3-qatlam: marker ----
        if (await db.Users.AnyAsync(u => u.Email == AcademicEmail, ct).ConfigureAwait(false))
        {
            DemoSeedLog.AlreadySeeded(logger);
            return;
        }

        // ---- 2-qatlam: baza hali bo'shmi ----
        var userCount = await db.Users.CountAsync(ct).ConfigureAwait(false);

        if (userCount > MaxUsersForDemo)
        {
            DemoSeedLog.Refused(logger, userCount, MaxUsersForDemo);
            return;
        }

        DemoSeedLog.Starting(logger);

        var hash = await hasher.HashAsync(FillerPassword, ct).ConfigureAwait(false);

        DemoMediaSink sink = null!;
        DemoWorld world = null!;

        // ════════════════════════════════════════════════════════════════
        // 🔴 TRANZAKSIYA — `ExecutionStrategy` ORQALI, TO'G'RIDAN-TO'G'RI EMAS
        //
        // Npgsql `EnableRetryOnFailure` bilan sozlangan, ya'ni EF qo'lda
        // ochilgan tranzaksiyani RAD ETADI: qayta urinish tranzaksiyaning
        // yarmini takrorlab, ma'lumotni ikki marta yozib qo'yishi mumkin.
        // Shuning uchun butun blok strategiya ichida bajariladi — u
        // "qayta urinish birligi" ni BUTUNLIGICHA qaytadan boshlaydi.
        //
        // ★ SHU SABABLI dunyo ham, hisoblagichlar ham LAMBDA ICHIDA
        //   yasaladi va o'zgarish kuzatkichi tozalanadi: qayta urinishda
        //   eski (allaqachon bekor qilingan) obyektlar qolib ketmasin.
        //
        // Tranzaksiyaning O'ZI nima uchun kerak — sinf izohida
        // ("yarim yozilgan marker").
        // ════════════════════════════════════════════════════════════════
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(
            async token =>
            {
                db.ChangeTracker.Clear();

                sink = new DemoMediaSink(media, submissions);
                world = new DemoWorld(db, sink, hash, DateTimeOffset.UtcNow);

                await using var transaction = await db.Database
                    .BeginTransactionAsync(token)
                    .ConfigureAwait(false);

                await world.BuildAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
            },
            ct)
            .ConfigureAwait(false);

        DemoSeedLog.Credentials(logger, FormatAccounts(world.Accounts));
        DemoSeedLog.Rows(logger, await CountRowsAsync(db, ct).ConfigureAwait(false));
        DemoSeedLog.Media(logger, sink.Uploaded, sink.Synthetic, sink.LastError ?? "-");
    }

    /// <summary>
    /// Kirish jadvali — LOGDA. Tekshiruvchi shu jadvalsiz hech qayerga
    /// kira olmaydi: raqamlar hech qanday ekranda ko'rinmaydi.
    /// </summary>
    private static string FormatAccounts(IReadOnlyList<DemoAccount> accounts)
    {
        var text = new StringBuilder();
        text.AppendLine();
        text.AppendLine("┌──────────────────┬────────────────────────┬─────────────────┐");
        text.AppendLine("│ Rol              │ Ism                    │ Telefon         │");
        text.AppendLine("├──────────────────┼────────────────────────┼─────────────────┤");

        foreach (var account in accounts)
        {
            text.Append(CultureInfo.InvariantCulture, $"│ {Pad(account.Role, 16)} ");
            text.Append(CultureInfo.InvariantCulture, $"│ {Pad(account.FullName, 22)} ");
            text.AppendLine(CultureInfo.InvariantCulture, $"│ {Pad(account.Phone, 15)} │");
        }

        text.AppendLine("└──────────────────┴────────────────────────┴─────────────────┘");

        return text.ToString();
    }

    private static string Pad(string value, int width) =>
        value.Length >= width ? value[..width] : value.PadRight(width);

    /// <summary>
    /// Haqiqiy qator sonlari — BAZADAN o'qiladi.
    ///
    /// ★ NIMA UCHUN hisoblagich emas: kodda sanalgan son "nima yozmoqchi
    /// bo'ldik" ni ko'rsatadi, bazadagi son esa "nima YOZILDI" ni. Farqi —
    /// aynan qidirilayotgan xato.
    /// </summary>
    private static async Task<string> CountRowsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var rows = new (string Name, int Count)[]
        {
            ("Users", await db.Users.CountAsync(ct).ConfigureAwait(false)),
            ("GroupCategories", await db.GroupCategories.CountAsync(ct).ConfigureAwait(false)),
            ("Courses", await db.Courses.CountAsync(ct).ConfigureAwait(false)),
            ("Modules", await db.Modules.CountAsync(ct).ConfigureAwait(false)),
            ("ModuleLessons", await db.ModuleLessons.CountAsync(ct).ConfigureAwait(false)),
            ("LessonAssets", await db.LessonAssets.CountAsync(ct).ConfigureAwait(false)),
            ("Groups", await db.Groups.CountAsync(ct).ConfigureAwait(false)),
            ("GroupMembers", await db.GroupMembers.CountAsync(ct).ConfigureAwait(false)),
            ("LiveSessions", await db.LiveSessions.CountAsync(ct).ConfigureAwait(false)),
            ("Attendances", await db.Attendances.CountAsync(ct).ConfigureAwait(false)),
            ("LessonGrades", await db.LessonGrades.CountAsync(ct).ConfigureAwait(false)),
            ("SessionRecordings", await db.SessionRecordings.CountAsync(ct).ConfigureAwait(false)),
            ("SessionReviews", await db.SessionReviews.CountAsync(ct).ConfigureAwait(false)),
            ("Assignments", await db.Assignments.CountAsync(ct).ConfigureAwait(false)),
            ("Submissions", await db.Submissions.CountAsync(ct).ConfigureAwait(false)),
            ("Tests", await db.Tests.CountAsync(ct).ConfigureAwait(false)),
            ("TestAttempts", await db.TestAttempts.CountAsync(ct).ConfigureAwait(false)),
            ("Payments", await db.Payments.CountAsync(ct).ConfigureAwait(false)),
            ("GroupChatMessages", await db.GroupChatMessages.CountAsync(ct).ConfigureAwait(false)),
            ("DirectMessages", await db.DirectMessages.CountAsync(ct).ConfigureAwait(false)),
            ("Notifications", await db.Notifications.CountAsync(ct).ConfigureAwait(false)),
        };

        var text = new StringBuilder();

        foreach (var (name, count) in rows)
        {
            text.Append(CultureInfo.InvariantCulture, $"{name}={count} ");
        }

        return text.ToString().TrimEnd();
    }
}

/// <summary>Manbadan generatsiya qilinadigan log metodlari (CA1848).</summary>
internal static partial class DemoSeedLog
{
    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Namunaviy ma'lumot allaqachon yozilgan — o'tkazib yuborildi.")]
    internal static partial void AlreadySeeded(ILogger logger);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Error,
        Message = "🔴 `Seed__Demo=true` YOQILGAN, LEKIN BAZA BO'SH EMAS: {UserCount} ta "
                  + "foydalanuvchi bor (ruxsat etilgani {Allowed}). Namunaviy ma'lumot "
                  + "YOZILMADI — bu ishlab chiqarish bazasi bo'lishi mumkin. Kalitni "
                  + "o'chiring yoki toza bazada ishga tushiring.")]
    internal static partial void Refused(ILogger logger, int userCount, int allowed);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Warning,
        Message = "Namunaviy (demo) ma'lumot yozilmoqda — `Seed__Demo` yoqilgan.")]
    internal static partial void Starting(ILogger logger);

    [LoggerMessage(
        EventId = 1103,
        Level = LogLevel.Warning,
        Message = "Namunaviy hisoblar (kirish: telefon + Telegram kodi). "
                  + "Kod soxta Telegram ID'ga bormaydi — uni `MessageOutbox` "
                  + "jadvalidan o'qing.{Table}")]
    internal static partial void Credentials(ILogger logger, string table);

    [LoggerMessage(
        EventId = 1104,
        Level = LogLevel.Information,
        Message = "Namunaviy ma'lumot qatorlari: {Rows}")]
    internal static partial void Rows(ILogger logger, string rows);

    [LoggerMessage(
        EventId = 1105,
        Level = LogLevel.Information,
        Message = "Namunaviy fayllar: omborga yozildi={Uploaded}, faqat qator={Synthetic} "
                  + "(video va dars yozuvi ATAYLAB faqat qator — ochilganda 404). "
                  + "Ombor xatosi: {Error}")]
    internal static partial void Media(ILogger logger, int uploaded, int synthetic, string error);
}
