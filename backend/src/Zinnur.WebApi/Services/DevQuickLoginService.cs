using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Auth.Services;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.WebApi.Services;

/// <summary>Namunaviy hisob — tugma uchun yetarli minimal ma'lumot.</summary>
/// <param name="Role">Rolning MASHINA nomi (<c>Admin</c>, <c>Teacher</c>…) — POST tanasiga tushadi.</param>
/// <param name="RoleLabel">O'zbekcha nom — tugmada shu ko'rinadi.</param>
/// <param name="FullName">Kimning ko'zi bilan ko'riladi.</param>
/// <param name="Phone">
/// XOM raqam — faqat KO'RSATISH uchun. Tugma bosilganda serverga rol
/// yuboriladi, raqam emas: shunda interfeys hisobni "tanlab" olmaydi.
/// </param>
public sealed record DevQuickLoginAccount(string Role, string RoleLabel, string FullName, string? Phone);

/// <summary>
/// <c>GET /api/v1/auth/dev/quick-login</c> javobi.
/// </summary>
/// <param name="Warning">
/// 🔴 OGOHLANTIRISH JAVOBNING O'ZIDA. Sabab: bu endpointni ko'rgan
/// birinchi odam (yangi dasturchi, xavfsizlik auditi, `curl` bilan
/// qidirayotgan kishi) uni HUJJATDAN emas, JAVOBDAN ko'radi. Matn
/// javobda bo'lsa "bu nima?" degan savol umuman tug'ilmaydi.
/// </param>
/// <param name="Environment">Joriy muhit nomi — "nega prod'da yo'q?" savoliga javob.</param>
/// <param name="Accounts">Har rolga BITTADAN hisob.</param>
public sealed record DevQuickLoginList(
    string Warning,
    string Environment,
    IReadOnlyList<DevQuickLoginAccount> Accounts);

/// <summary>
/// <c>POST /api/v1/auth/dev/quick-login</c> tanasi.
/// </summary>
/// <param name="Role">
/// Rol nomi (<c>Admin</c>, <c>Academic</c>, <c>Teacher</c>, <c>Assistant</c>,
/// <c>Student</c>). Interfeys AYNAN shuni yuboradi.
/// </param>
/// <param name="Phone">
/// Muqobil: aniq raqam (qo'lda `curl` uchun qulay). Ikkalasi berilsa
/// raqam ustun turadi.
///
/// 🔴 FOYDALANUVCHI ID'si TANADA UMUMAN YO'Q va bo'lmaydi ham: har
/// ikkala maydon ham FAQAT namunaviy diapazon ichida qidiriladi, ya'ni
/// so'rov tanasi "kimga kirish" ni tanlay olmaydi — u faqat oldindan
/// tasdiqlangan qisqa ro'yxatdan tanlaydi.
/// </param>
public sealed record DevQuickLoginRequest(string? Role, string? Phone);

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 3-DARVOZA: FAQAT `DemoDataSeeder` YOZGAN HISOBLARGA KIRISH MUMKIN
/// ════════════════════════════════════════════════════════════════════════
///
/// 1- va 2-darvoza (oshkor kalit + muhit) <see cref="DevQuickLoginGate"/>
/// da. Bu sinf UCHINCHISINI bajaradi va u — ZARAR DOIRASINI cheklovchi:
/// hatto birinchi ikkalasi ham qandaydir tarzda noto'g'ri sozlangan
/// bo'lsa ham, HAQIQIY o'quv markazining administratori BARIBIR
/// yetib bo'lmaydigan bo'lib qoladi, chunki namunaviy hisoblar faqat
/// demo seed ishlagan bazada mavjud.
///
/// ★ HAR SO'ROVDA, HAR QATOR uchun tekshiriladi (ro'yxat ham, kirish
///   ham AYNI <see cref="DemoCandidates"/> so'rovidan o'tadi). "Avval
///   ro'yxat oldim, keyin o'shandan tanladim" degan ishonch YO'Q:
///   POST ro'yxatga umuman tayanmaydi, u qaytadan filtrlaydi.
///
/// ──────────────────────────────────────────────────────────────────────
/// 🔴 TOKEN YANGI YO'LDAN BERILMAYDI
///
/// Sessiya <see cref="IAuthService.LoginWithPhoneAsync"/> orqali
/// ochiladi — ya'ni AYNI <c>AuthService.Build()</c>, AYNI <c>ver</c>
/// (TokenVersion) va AYNI bekor qilish mexanizmi. Ikkinchi token
/// yaratuvchi yozish `IAuthService` izohidagi QAT'IY QOIDANI buzardi:
/// birida tuzatilgan zaiflik ikkinchisida ochiq qolardi.
///
/// ★ SHU SABABLI "chiqish" (`/auth/logout`), sessiya bekor qilish va
///   `refresh` bu yo'l bilan olingan token uchun ham ODATDAGIDEK
///   ishlaydi — hech qanday alohida holat yo'q.
/// </summary>
public sealed class DevQuickLoginService(
    IApplicationDbContext db,
    IAuthService auth,
    ILogger<DevQuickLoginService> logger)
{
    /// <summary>Javobda va OpenAPI tavsifida chiqadigan ogohlantirish.</summary>
    public const string WarningText =
        "⚠️ FAQAT SINOV UCHUN. Bu endpoint parolsiz/kodsiz sessiya ochadi va "
        + "faqat namunaviy (demo) hisoblarga ishlaydi. Ishlab chiqarish "
        + "muhitida (`Production`) u UMUMAN mavjud emas.";

    /// <summary>
    /// Rollar TARTIBI va o'zbekcha nomlari — interfeys tugmalari AYNAN
    /// shu tartibda chiqadi (loyiha egasi sanagan tartib).
    ///
    /// ★ NIMA UCHUN enum tartibi EMAS: `UserRole` da `Student = 0` va
    ///   `Admin = 4` — ya'ni enum bo'yicha saralash tugmalarni teskari
    ///   qo'yardi. Enum qiymatlari esa BAZAGA yozilgan, ularni
    ///   ko'rinish uchun o'zgartirib bo'lmaydi.
    /// </summary>
    private static readonly (UserRole Role, string Label)[] RoleOrder =
    [
        (UserRole.Admin, "Administrator"),
        (UserRole.Academic, "O'quv bo'limi"),
        (UserRole.Teacher, "Ustoz"),
        (UserRole.Assistant, "Kurator"),
        (UserRole.Student, "O'quvchi"),
    ];

    /// <summary>
    /// Har rolga BITTADAN namunaviy hisob.
    ///
    /// ★ NIMA UCHUN BITTADAN: namunada 2 ta ustoz, 2 ta kurator va 12 ta
    ///   o'quvchi bor. Hammasini ko'rsatish kirish sahifasini ro'yxatga
    ///   aylantirardi; tekshiruvchiga esa "shu rol ko'zi bilan ochish"
    ///   kerak, aniq odam emas. Boshqa odam kerak bo'lsa — <c>phone</c>
    ///   bilan.
    /// </summary>
    public async Task<IReadOnlyList<DevQuickLoginAccount>> ListAsync(CancellationToken ct = default)
    {
        var rows = await DemoCandidates().ToListAsync(ct).ConfigureAwait(false);

        var accounts = new List<DevQuickLoginAccount>(RoleOrder.Length);

        foreach (var (role, label) in RoleOrder)
        {
            // ★ ENG KICHIK `Id` — BASHORATLI tanlov. Seeder rollarni ketma-ket
            //   yozadi, ya'ni bu har doim ssenariyning "asosiy" odami
            //   (1-ustoz, 1-kurator, 1-o'quvchi) bo'ladi va u AYNAN
            //   guruhlar/darslar/to'lovlar bog'langan profil. Tasodifiy
            //   tanlovda tekshiruvchi ba'zan bo'sh ekranga tushardi.
            var first = rows
                .Where(r => r.Role == role)
                .OrderBy(r => r.Id)
                .FirstOrDefault();

            if (first is not null)
                accounts.Add(new DevQuickLoginAccount(role.ToString(), label, first.FullName, first.Phone));
        }

        return accounts;
    }

    /// <summary>
    /// Sessiya ochadi — FAQAT namunaviy hisob uchun.
    /// </summary>
    /// <exception cref="ValidationException">Tanada na rol, na raqam berilgan.</exception>
    /// <exception cref="ForbiddenException">
    /// So'ralgan hisob namunaviy emas (yoki umuman yo'q).
    ///
    /// 🔴 IKKALA HOLAT UCHUN AYNI XABAR: "bunday odam yo'q" va "bor,
    /// lekin haqiqiy" ni ajratib ko'rsatish endpointni haqiqiy
    /// foydalanuvchilarni sanaydigan vositaga aylantirardi. Bu yo'l
    /// dev'da ochiq bo'lgani uchun bunday oshkorlik bepul emas.
    /// </exception>
    public async Task<AuthResponse> LoginAsync(
        DevQuickLoginRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedPhone = User.NormalizePhone(request.Phone);
        var roleGiven = !string.IsNullOrWhiteSpace(request.Role);

        // 400 — SO'ROV shakli buzuq (ikkala maydon ham bo'sh). Bu
        // "ruxsat yo'q" emas, klientning xatosi, shuning uchun 403 EMAS.
        if (normalizedPhone is null && !roleGiven)
        {
            throw new ValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["role"] = ["`role` yoki `phone` maydonlaridan biri to'ldirilishi kerak."],
            });
        }

        // 🔴 FILTR SO'ROVNING O'ZIDA — kod oqimida emas.
        //
        // `DemoCandidates()` allaqachon namunaviy diapazon bilan
        // cheklangan, ya'ni "topildi, endi namunaviymi deb tekshiraman"
        // degan ikkinchi qadam UMUMAN yo'q. Ikki qadamli tekshiruvda
        // kelajakda kimdir shartni `if` bilan chetlab o'tishi mumkin
        // bo'lardi; bu yerda esa chetlab o'tadigan `if` yo'q.
        User? target = null;

        if (normalizedPhone is not null)
        {
            target = await DemoCandidates()
                .Where(u => u.PhoneNormalized == normalizedPhone)
                .OrderBy(u => u.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }
        else if (ParseRole(request.Role) is { } role)
        {
            // Noma'lum rol nomi ("Boshliq") bu shoxga umuman kirmaydi va
            // pastda AYNI 403 ni oladi — ya'ni "bunday rol yo'q" ham
            // alohida xabar bermaydi.
            target = await DemoCandidates()
                .Where(u => u.Role == role)
                .OrderBy(u => u.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        if (target is null)
        {
            throw new ForbiddenException(
                "Bu yo'l FAQAT namunaviy (demo) hisoblar uchun ishlaydi. "
                + "So'ralgan hisob namunaviy ma'lumotga kirmaydi.");
        }

        // ★ AUDIT IZI: bu — autentifikatsiyani chetlab o'tish, ya'ni
        //   HAR ishlatilishi logda ko'rinishi kerak. `Warning` darajasi
        //   ataylab: dev logida ham ajralib tursin.
        ApiLog.DevQuickLoginUsed(logger, target.Role.ToString(), target.Id);

        // 🔴 TOKEN — MAVJUD YO'LDAN. `LoginWithPhoneAsync` `IsActive` ni
        //    QAYTA tekshiradi va `Build()` orqali odatiy juftlikni beradi.
        return await auth.LoginWithPhoneAsync(target.Id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Namunaviy hisoblar so'rovi — BU SINFDAGI YAGONA MANBA.
    ///
    /// ★ `IsActive` ham shu yerda: o'chirilgan namunaviy profil ro'yxatda
    ///   ham ko'rinmasin, unga kirib ham bo'lmasin. (`AuthService` buni
    ///   yana bir bor tekshiradi — takrorlash ataylab.)
    /// </summary>
    private IQueryable<User> DemoCandidates() =>
        db.Users
            .AsNoTracking()
            .Where(u => u.IsActive
                     && u.TelegramId >= DemoDataSeeder.DemoTelegramIdMin
                     && u.TelegramId < DemoDataSeeder.DemoTelegramIdMaxExclusive);

    /// <summary>
    /// Rol nomini o'qiydi. Noma'lum qiymat — <c>null</c> (keyin 403),
    /// istisno EMAS: "Boshliq" deb yozgan klient uchun 500 bermaymiz.
    /// </summary>
    private static UserRole? ParseRole(string? raw) =>
        !string.IsNullOrWhiteSpace(raw)
        && Enum.TryParse<UserRole>(raw.Trim(), ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : null;
}
