using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Users.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Users.Services;

/// <summary>
/// Foydalanuvchilarni boshqarish use-case'lari.
/// HTTP haqida HECH NARSA bilmaydi — faqat Application xatolarini ko'taradi.
/// </summary>
public sealed class UserService(
    IApplicationDbContext db,
    IPasswordHasher hasher) : IUserService
{
    // ================================================================= o'qish

    public async Task<PagedResult<UserDetailsDto>> ListAsync(
        UserListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await LoadActorAsync(actorId, ct);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = db.Users.AsNoTracking();

        if (query.Role is { } role)
            rows = rows.Where(u => u.Role == role);

        if (query.IsActive is { } isActive)
            rows = rows.Where(u => u.IsActive == isActive);

        rows = ApplySearch(rows, query.Search);

        // Ikkita so'rov (COUNT + sahifa) — ataylab: `Total` bo'lmasa frontend
        // paginator sahifalar sonini bila olmaydi.
        var total = await rows.CountAsync(ct);

        var items = await rows
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            // PasswordHash BAZADAN UMUMAN OLINMAYDI — faqat kerakli ustunlar.
            .Select(u => new Projection(
                u.Id, u.FullName, u.Email, u.Phone, u.TelegramId,
                u.Role, u.IsActive, u.CreatedAt, u.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<UserDetailsDto>(
            items.Select(Map).ToList(), page, pageSize, total);
    }

    public async Task<UserDetailsDto> GetAsync(long id, long actorId, CancellationToken ct = default)
    {
        await LoadActorAsync(actorId, ct);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException(nameof(User), id);

        return Map(user);
    }

    // ================================================================= yozish

    public async Task<CreateUserResponse> CreateAsync(
        CreateUserRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);

        // RUXSAT: nishon hali yo'q, shuning uchun faqat BERILAYOTGAN rol tekshiriladi.
        EnsureCanManage(actor, target: null, newRole: request.Role);

        var fullName = RequireFullName(request.FullName);
        var email = RequireEmail(request.Email);
        var phone = User.NormalizePhone(request.Phone);

        await EnsureEmailFreeAsync(email, exceptUserId: null, ct);
        await EnsurePhoneFreeAsync(phone, exceptUserId: null, ct);

        // Parol berilmasa — kuchli tasodifiy parol. Javobda BIR MARTA ko'rsatiladi.
        var generated = string.IsNullOrWhiteSpace(request.Password);
        var password = generated ? GenerateTemporaryPassword() : request.Password!;

        var user = new User
        {
            FullName = fullName,
            Email = email,
            PasswordHash = await hasher.HashAsync(password, ct),
            Role = request.Role,
            IsActive = request.IsActive,
        };

        user.SetPhone(request.Phone);

        db.Users.Add(user);
        await SaveWithUniqueGuardAsync(ct);

        return new CreateUserResponse(Map(user), generated ? password : null);
    }

    public async Task<UserDetailsDto> UpdateAsync(
        long id, UpdateUserRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        var user = await LoadTargetAsync(id, ct);

        // RUXSAT: ham NISHON roli (kimni tahrirlayapmiz), ham YANGI rol tekshiriladi.
        EnsureCanManage(actor, user, newRole: request.Role);

        var fullName = RequireFullName(request.FullName);
        var email = RequireEmail(request.Email);
        var phone = User.NormalizePhone(request.Phone);

        await EnsureEmailFreeAsync(email, exceptUserId: user.Id, ct);
        await EnsurePhoneFreeAsync(phone, exceptUserId: user.Id, ct);

        user.FullName = fullName;
        user.Email = email;
        user.SetPhone(request.Phone);

        // ChangeRole ichida InvalidateTokens() bor — rol o'zgarsa eski tokendagi
        // rol claim'i darhol yaroqsiz bo'ladi.
        if (request.Role is { } newRole)
            user.ChangeRole(newRole);

        await SaveWithUniqueGuardAsync(ct);
        return Map(user);
    }

    public async Task<UserDetailsDto> SetActiveAsync(
        long id, bool isActive, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        var user = await LoadTargetAsync(id, ct);

        // RUXSAT: o'chirish foydalanuvchini tizimdan qulflab qo'yishi mumkin.
        EnsureCanManage(actor, user, selfLockout: !isActive);

        if (user.IsActive == isActive) return Map(user);

        user.IsActive = isActive;

        // Profil o'chirilsa mavjud sessiyalar DARHOL o'lsin — aks holda kirish
        // tokeni yana 15 daqiqa ishlab turardi.
        if (!isActive)
            user.InvalidateTokens();

        await db.SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task<ResetPasswordResponse> ResetPasswordAsync(
        long id, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        var user = await LoadTargetAsync(id, ct);

        EnsureCanManage(actor, user);

        var password = GenerateTemporaryPassword();

        // SetPassword ichida InvalidateTokens() bor — eski sessiyalar o'ladi.
        user.SetPassword(await hasher.HashAsync(password, ct));
        await db.SaveChangesAsync(ct);

        // Parol OCHIQ KO'RINISHDA hech qayerda saqlanmaydi — faqat shu javobda.
        return new ResetPasswordResponse(user.Id, password);
    }

    // ================================================================= CSV import

    public async Task<UserImportResponse> ImportCsvAsync(
        Stream csv, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(csv);

        var actor = await LoadActorAsync(actorId, ct);

        if (csv.CanSeek && csv.Length > MaxImportBytes)
            throw Invalid("file", "Fayl juda katta. Chegara: "
                + (MaxImportBytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB.");

        var (rows, issues) = await ParseCsvAsync(csv, actor, ct);

        // Umumiy ma'lumot qatorlari soni: qabul qilinganlar + parse'da rad etilganlar.
        var totalRows = rows.Count + issues.Count;
        var created = 0;

        // PAKETLAB yoziladi: har paket = 1 ta dublikat SELECT'i + 1 ta INSERT
        // to'plami (bitta tranzaksiya). Eski tizim har qator uchun alohida
        // so'rov yuborardi — 1000 qator = 3000 marta baza bilan aloqa.
        for (var offset = 0; offset < rows.Count; offset += ImportBatchSize)
        {
            var batch = rows.GetRange(offset, Math.Min(ImportBatchSize, rows.Count - offset));
            created += await ImportBatchAsync(batch, issues, ct);
        }

        return new UserImportResponse(
            totalRows, created, issues.Count, issues.OrderBy(i => i.Line).ToList());
    }

    private async Task<int> ImportBatchAsync(
        List<ImportRow> batch, List<UserImportIssue> issues, CancellationToken ct)
    {
        // 1) Bazadagi band email/telefonlarni BITTA indeksli so'rovda aniqlaymiz.
        var emails = batch.ConvertAll(r => r.Email);
        var phones = batch.Where(r => r.Phone is not null).Select(r => r.Phone!).ToList();

        var taken = await db.Users
            .AsNoTracking()
            .Where(u => emails.Contains(u.Email)
                     || (u.PhoneNormalized != null && phones.Contains(u.PhoneNormalized)))
            .Select(u => new { u.Email, u.PhoneNormalized })
            .ToListAsync(ct);

        var takenEmails = taken.Select(t => t.Email).ToHashSet(StringComparer.Ordinal);
        var takenPhones = taken
            .Where(t => t.PhoneNormalized is not null)
            .Select(t => t.PhoneNormalized!)
            .ToHashSet(StringComparer.Ordinal);

        var accepted = new List<ImportRow>(batch.Count);

        foreach (var row in batch)
        {
            if (takenEmails.Contains(row.Email))
                issues.Add(new UserImportIssue(row.Line, "Bu email allaqachon ro'yxatda."));
            else if (row.Phone is not null && takenPhones.Contains(row.Phone))
                issues.Add(new UserImportIssue(row.Line, "Bu telefon raqam allaqachon ro'yxatda."));
            else
                accepted.Add(row);
        }

        if (accepted.Count == 0) return 0;

        // 2) Parollarni CHEKLANGAN PARALLELLIK bilan hash'laymiz.
        //    BCrypt ~100-250 ms sof CPU; ketma-ket qilinsa 200 qator ~30 soniya.
        //    Parallellik cheklanmasa import butun thread pool'ni egallab,
        //    boshqa so'rovlar javobsiz qolardi.
        var hashes = new string[accepted.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, accepted.Count),
            new ParallelOptions { MaxDegreeOfParallelism = HashParallelism, CancellationToken = ct },
            async (i, token) =>
            {
                hashes[i] = await hasher.HashAsync(GenerateTemporaryPassword(), token);
            });

        var entities = new List<User>(accepted.Count);

        for (var i = 0; i < accepted.Count; i++)
        {
            var row = accepted[i];

            var user = new User
            {
                FullName = row.FullName,
                Email = row.Email,
                PasswordHash = hashes[i],
                Role = row.Role,
            };

            user.SetPhone(row.RawPhone);
            entities.Add(user);
        }

        db.Users.AddRange(entities);

        // 3) Paket = bitta tranzaksiya (EF `SaveChangesAsync` shunday ishlaydi).
        try
        {
            await db.SaveChangesAsync(ct);
            return entities.Count;
        }
        catch (DbUpdateException)
        {
            // Poyga holati (bir vaqtda ikkinchi import) yoki kutilmagan constraint.
            // BUTUN paketni JIMGINA yo'qotmaymiz: qatorlarni bittalab qayta
            // urinamiz va AYNAN qaysi qator yiqilganini xabar qilamiz.
            //
            // `Remove` — Added holatidagi entity'ni kuzatuvdan CHIQARADI (o'chirmaydi).
            // Aks holda buzilgan yozuv keyingi SaveChanges'da yana urinilardi va
            // butun import zanjir bo'lib yiqilardi.
            foreach (var entity in entities)
                db.Users.Remove(entity);

            return await RetryOneByOneAsync(accepted, entities, issues, ct);
        }
    }

    /// <summary>Paket yiqilganda: har qatorni alohida yozib, aybdorini aniqlaydi.</summary>
    private async Task<int> RetryOneByOneAsync(
        List<ImportRow> accepted,
        List<User> entities,
        List<UserImportIssue> issues,
        CancellationToken ct)
    {
        var created = 0;

        for (var i = 0; i < entities.Count; i++)
        {
            db.Users.Add(entities[i]);

            try
            {
                await db.SaveChangesAsync(ct);
                created++;
            }
            catch (DbUpdateException)
            {
                db.Users.Remove(entities[i]);
                issues.Add(new UserImportIssue(
                    accepted[i].Line, "Bazaga yozib bo'lmadi (takroriy email yoki telefon)."));
            }
        }

        return created;
    }

    /// <summary>
    /// CSV ni o'qiydi va HAR QATORNI alohida tekshiradi — bitta xato qator
    /// qolganlarini to'xtatmaydi (eski tizimda bitta xato butun importni uzardi).
    /// </summary>
    private static async Task<(List<ImportRow> Rows, List<UserImportIssue> Issues)> ParseCsvAsync(
        Stream csv, User actor, CancellationToken ct)
    {
        var rows = new List<ImportRow>();
        var issues = new List<UserImportIssue>();

        // Fayl ICHIDAGI takrorlarni ham ushlaymiz (baza indeksigacha yetib bormaydi).
        var seenEmails = new HashSet<string>(StringComparer.Ordinal);
        var seenPhones = new HashSet<string>(StringComparer.Ordinal);

        using var reader = new StreamReader(csv, leaveOpen: true);

        var header = await reader.ReadLineAsync(ct)
            ?? throw Invalid("file", "Fayl bo'sh.");

        var columns = ReadHeader(header);
        var line = 1;

        while (await reader.ReadLineAsync(ct) is { } raw)
        {
            line++;

            if (string.IsNullOrWhiteSpace(raw)) continue;

            if (rows.Count + issues.Count >= MaxImportRows)
                throw Invalid("file", "Fayldagi qatorlar soni "
                    + MaxImportRows.ToString(CultureInfo.InvariantCulture) + " dan oshmasligi kerak.");

            var fields = SplitCsvLine(raw);

            var fullName = Field(fields, columns.FullName);
            var email = Field(fields, columns.Email);
            var rawPhone = Field(fields, columns.Phone);
            var rawRole = Field(fields, columns.Role);

            if (string.IsNullOrWhiteSpace(fullName))
            {
                issues.Add(new UserImportIssue(line, "F.I.Sh. bo'sh."));
                continue;
            }

            if (!TryParseRole(rawRole, out var role))
            {
                issues.Add(new UserImportIssue(line, "Rol noto'g'ri: '" + rawRole + "'."));
                continue;
            }

            // RUXSAT: HAR QATOR uchun ham tekshiriladi — o'quv bo'limi xodimi
            // CSV orqali admin yarata olmasin. (Eski tizimda import yo'li
            // ruxsat tekshiruvidan butunlay chetda edi.)
            try
            {
                EnsureCanManage(actor, target: null, newRole: role);
            }
            catch (ForbiddenException ex)
            {
                issues.Add(new UserImportIssue(line, ex.Message));
                continue;
            }

            var normalizedEmail = NormalizeEmail(email);

            if (!IsValidEmail(normalizedEmail))
            {
                issues.Add(new UserImportIssue(line, "Email noto'g'ri: '" + email + "'."));
                continue;
            }

            var phone = User.NormalizePhone(rawPhone);

            if (!seenEmails.Add(normalizedEmail))
            {
                issues.Add(new UserImportIssue(line, "Fayl ichida email takrorlangan."));
                continue;
            }

            if (phone is not null && !seenPhones.Add(phone))
            {
                issues.Add(new UserImportIssue(line, "Fayl ichida telefon takrorlangan."));
                continue;
            }

            rows.Add(new ImportRow(line, fullName.Trim(), normalizedEmail, phone, rawPhone, role));
        }

        return (rows, issues);
    }

    // ================================================================= RUXSAT QOIDASI

    /// <summary>
    /// ================================================================
    /// FOYDALANUVCHILARNI BOSHQARISHNING YAGONA RUXSAT QOIDASI
    /// ================================================================
    /// Har bir o'zgartiruvchi metod (yaratish, tahrirlash, o'chirish/yoqish,
    /// parol tiklash, CSV import) AYNAN shu metoddan o'tadi. Qoida BITTA joyda
    /// yozilgani uchun yangi endpoint qo'shilganda uni unutib bo'lmaydi (DRY).
    ///
    /// NIMA UCHUN (eski tizim zaifligi X-4): eski panelda `academic` roli
    /// `admin` akkauntini tahrirlay olardi — parolini almashtirib, rolini
    /// pasaytirib yoki profilni o'chirib qo'yishi mumkin edi. Ya'ni past
    /// huquqli xodim butun platformani egallab olardi.
    ///
    /// Qoidalar:
    ///  1) HIMOYALANGAN ROL (Admin, Academic) egasiga FAQAT Admin tega oladi.
    ///  2) Academic faqat {Student, Teacher, Assistant} rolini bera oladi.
    ///  3) Hech kim O'Z profilini o'chira olmaydi va O'Z rolini o'zgartira
    ///     olmaydi — o'zini tizimdan qulflab qo'yishning oldi olinadi.
    /// </summary>
    /// <param name="actor">Amalni bajaruvchi — BAZADAN o'qilgan, token'dagi claim'dan emas.</param>
    /// <param name="target">Nishon foydalanuvchi. Yaratish/importda <c>null</c>.</param>
    /// <param name="newRole">Beriladigan yoki o'zgartiriladigan rol; o'zgarmasa <c>null</c>.</param>
    /// <param name="selfLockout">Amal foydalanuvchini tizimdan chiqarib qo'yishi mumkinmi.</param>
    private static void EnsureCanManage(
        User actor,
        User? target,
        UserRole? newRole = null,
        bool selfLockout = false)
    {
        // 1) Himoyalangan rol egasini faqat Admin boshqaradi
        if (target is not null && IsProtected(target.Role) && actor.Role != UserRole.Admin)
            throw new ForbiddenException(
                "Bu profilni faqat administrator boshqara oladi. "
                + "O'quv bo'limi xodimi admin va o'quv bo'limi profillariga tega olmaydi.");

        // 2) Himoyalangan rolni faqat Admin bera oladi
        if (newRole is { } role && IsProtected(role) && actor.Role != UserRole.Admin)
            throw new ForbiddenException(
                "Faqat administrator 'Admin' yoki 'Academic' rolini bera oladi. "
                + "Sizga ruxsat etilgan rollar: Student, Teacher, Assistant.");

        if (target is null || target.Id != actor.Id) return;

        // 3) O'zini qulflab qo'yishdan himoya
        if (selfLockout)
            throw new ForbiddenException("O'z profilingizni o'chira olmaysiz.");

        if (newRole is { } ownRole && ownRole != actor.Role)
            throw new ForbiddenException("O'z rolingizni o'zgartira olmaysiz.");
    }

    /// <summary>Faqat Admin tega oladigan rollar.</summary>
    private static bool IsProtected(UserRole role) =>
        role is UserRole.Admin or UserRole.Academic;

    // ================================================================= ichki yordamchi

    private async Task<User> LoadActorAsync(long actorId, CancellationToken ct)
    {
        // Rol TOKEN'dan emas, BAZADAN olinadi: kirish tokeni 15 daqiqa yashaydi,
        // shuning uchun endi o'chirilgan yoki roli pasaytirilgan xodim eski
        // token bilan amal bajara olmasligi kerak.
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return actor;
    }

    private async Task<User> LoadTargetAsync(long id, CancellationToken ct) =>
        await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new NotFoundException(nameof(User), id);

    /// <summary>
    /// Qidiruv: F.I.Sh. / email / telefon bo'yicha qism-satr.
    ///
    /// <c>LIKE '%...%'</c> B-tree indeksdan FOYDALANA OLMAYDI, shuning uchun
    /// migratsiyada <c>pg_trgm</c> GIN ifoda-indekslari qo'yilgan:
    /// <c>gin (lower("FullName") gin_trgm_ops)</c> va h.k. Shu sababli bu yerda
    /// AYNAN <c>lower(...) LIKE ...</c> shakli ishlatiladi — <c>Contains</c>
    /// bo'lsa EF uni <c>strpos()</c> ga aylantirardi va indeks ishlamasdi.
    ///
    /// Telefon alohida: qidiruvdan faqat RAQAMLAR olinadi va ular
    /// <c>PhoneNormalized</c> (<c>+998...</c>) ichidan izlanadi — shu tufayli
    /// "+998 90 123", "90-123" va "90123" bir xil natija beradi.
    /// </summary>
    private static IQueryable<User> ApplySearch(IQueryable<User> rows, string? search)
    {
        var trimmed = search?.Trim();

        if (string.IsNullOrEmpty(trimmed)) return rows;

        // Trigrammadan kamida 3 belgi kerak. Qisqaroq so'rovda Postgres
        // GIN indeksdan foydalana olmay seq scan'ga tushardi — bu esa aynan
        // biz qochayotgan muammo (100 ming yozuvda sekundlar).
        if (trimmed.Length < MinSearchLength)
            throw Invalid("search", "Qidiruv uchun kamida "
                + MinSearchLength.ToString(CultureInfo.InvariantCulture) + " belgi kiriting.");

        var term = "%" + Escape(trimmed.ToLowerInvariant()) + "%";
        var digits = new string([.. trimmed.Where(char.IsAsciiDigit)]);

        // DIQQAT: quyidagi `u.FullName.ToLower()` .NET satri ustida ISHLAMAYDI —
        // u ifoda daraxti ichida va EF uni Postgres'ning `lower()` funksiyasiga
        // aylantiradi (indeks ifodasi bilan AYNAN mos tushishi uchun shart).
        // `ToLowerInvariant()` ni EF tarjima qila olmaydi, shuning uchun
        // globalizatsiya analizatori shu blokda ataylab o'chirilgan.
#pragma warning disable CA1304, CA1311
        if (digits.Length < MinSearchLength)
        {
            return rows.Where(u =>
                EF.Functions.Like(u.FullName.ToLower(), term) ||
                EF.Functions.Like(u.Email, term));
        }

        var phoneTerm = "%" + digits + "%";

        return rows.Where(u =>
            EF.Functions.Like(u.FullName.ToLower(), term) ||
            EF.Functions.Like(u.Email, term) ||
            (u.PhoneNormalized != null && EF.Functions.Like(u.PhoneNormalized, phoneTerm)));
#pragma warning restore CA1304, CA1311
    }

    /// <summary>LIKE metabelgilarini zararsizlantiradi (aks holda '%' butun jadvalni tortadi).</summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>Email bandligini BITTA indeksli so'rov bilan tekshiradi.</summary>
    private async Task EnsureEmailFreeAsync(string email, long? exceptUserId, CancellationToken ct)
    {
        var taken = await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == email && (exceptUserId == null || u.Id != exceptUserId), ct);

        if (taken)
            throw new ConflictException("Bu email allaqachon ro'yxatda.");
    }

    /// <summary>
    /// Telefon bandligini BITTA indeksli so'rov bilan tekshiradi.
    ///
    /// Eski tizimda bu joy <c>SELECT * FROM users WHERE phone IS NOT NULL</c>
    /// qilib HAMMA yozuvni xotiraga yuklardi va Python siklida normalizatsiya
    /// qilib taqqoslardi (O(N), har kirish va har tahrirda). Endi
    /// <c>IX_Users_PhoneNormalized</c> bo'yicha bitta indeksli qidiruv.
    /// </summary>
    private async Task EnsurePhoneFreeAsync(string? phoneNormalized, long? exceptUserId, CancellationToken ct)
    {
        if (phoneNormalized is null) return;

        var taken = await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.PhoneNormalized == phoneNormalized
                        && (exceptUserId == null || u.Id != exceptUserId), ct);

        if (taken)
            throw new ConflictException("Bu telefon raqam allaqachon ro'yxatda.");
    }

    /// <summary>
    /// Unikal indeks buzilishini tushunarli 409 ga aylantiradi.
    /// Tekshiruv bilan yozuv orasida boshqa so'rov ulgurib qolishi mumkin —
    /// indeks oxirgi (va ishonchli) himoya.
    /// </summary>
    private async Task SaveWithUniqueGuardAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException(
                "Bu email yoki telefon raqam allaqachon band. Qaytadan urinib ko'ring.");
        }
    }

    private static string RequireFullName(string? fullName)
    {
        var value = fullName?.Trim();

        if (string.IsNullOrEmpty(value))
            throw Invalid(nameof(User.FullName), "F.I.Sh. kiritilishi shart.");

        if (value.Length > MaxFullNameLength)
            throw Invalid(nameof(User.FullName), "F.I.Sh. juda uzun.");

        return value;
    }

    private static string RequireEmail(string? email)
    {
        var value = NormalizeEmail(email);

        return IsValidEmail(value)
            ? value
            : throw Invalid(nameof(User.Email), "Email noto'g'ri.");
    }

    private static string NormalizeEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Yengil tekshiruv: bitta '@', bo'shliqsiz, domenida nuqta bor.</summary>
    private static bool IsValidEmail(string email)
    {
        if (email.Length is 0 or > MaxEmailLength) return false;
        if (email.Contains(' ')) return false;

        var at = email.IndexOf('@');

        return at > 0
            && at < email.Length - 1
            && email.IndexOf('@', at + 1) < 0
            && email.LastIndexOf('.') > at + 1
            && email[^1] != '.';
    }

    /// <summary>
    /// Kriptografik jihatdan kuchli vaqtinchalik parol.
    /// Chalkashadigan belgilar (0/O, 1/l/I) ATAYLAB olib tashlangan — parol
    /// ko'pincha telefonda og'zaki aytiladi.
    /// </summary>
    private static string GenerateTemporaryPassword() =>
        RandomNumberGenerator.GetString(PasswordAlphabet, TemporaryPasswordLength);

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private static UserDetailsDto Map(User u) => new(
        u.Id, u.FullName, u.Email, u.Phone, u.TelegramId,
        u.Role.ToString(), u.IsActive, u.CreatedAt, u.UpdatedAt);

    private static UserDetailsDto Map(Projection p) => new(
        p.Id, p.FullName, p.Email, p.Phone, p.TelegramId,
        p.Role.ToString(), p.IsActive, p.CreatedAt, p.UpdatedAt);

    // ---------------------------------------------------------------- CSV yordamchi

    private static ImportColumns ReadHeader(string header)
    {
        var names = SplitCsvLine(header);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i].Trim().Trim('﻿');      // BOM

            if (name.Length > 0)
                index[name] = i;
        }

        return new ImportColumns(
            Require(index, "full_name"),
            Require(index, "phone"),
            Require(index, "email"),
            Require(index, "role"));

        static int Require(Dictionary<string, int> index, string column) =>
            index.TryGetValue(column, out var i)
                ? i
                : throw Invalid("file",
                    "CSV sarlavhasida '" + column + "' ustuni yo'q. "
                    + "Kutilayotgan ustunlar: full_name,phone,email,role");
    }

    /// <summary>
    /// Bitta CSV qatorini maydonlarga ajratadi. Qo'shtirnoq ichidagi ajratgich
    /// qo'llab-quvvatlanadi (<c>"Aliyev, Vali",...</c>), <c>""</c> — qo'shtirnoqning
    /// o'zi. Maydon ICHIDA qator ko'chirish qo'llab-quvvatlanmaydi.
    /// </summary>
    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>(4);
        var buffer = new StringBuilder(line.Length);
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (quoted)
            {
                if (ch != '"')
                {
                    buffer.Append(ch);
                }
                else if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    buffer.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch is ',' or ';')
            {
                fields.Add(buffer.ToString());
                buffer.Clear();
            }
            else
            {
                buffer.Append(ch);
            }
        }

        fields.Add(buffer.ToString());
        return [.. fields];
    }

    private static string Field(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index].Trim() : string.Empty;

    /// <summary>Rolni nom ("student", "Teacher") yoki raqam ("0") bo'yicha o'qiydi.</summary>
    private static bool TryParseRole(string raw, out UserRole role)
    {
        role = UserRole.Student;

        return !string.IsNullOrWhiteSpace(raw)
            && Enum.TryParse(raw.Trim(), ignoreCase: true, out role)
            && Enum.IsDefined(role);
    }

    // ---------------------------------------------------------------- doimiylar va ichki turlar

    private const int MaxPageSize = 100;
    private const int MinSearchLength = 3;
    private const int MaxFullNameLength = 200;
    private const int MaxEmailLength = 256;
    private const int TemporaryPasswordLength = 14;

    /// <summary>Import chegaralari: har qator uchun BCrypt hisobi kerak, cheksiz fayl serverni bo'g'adi.</summary>
    private const int MaxImportBytes = 2 * 1024 * 1024;

    private const int MaxImportRows = 1000;
    private const int ImportBatchSize = 200;

    private static readonly int HashParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8);

    private const string PasswordAlphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";

    /// <summary>Ro'yxat so'rovi uchun ustunlar to'plami — <c>PasswordHash</c> olinmaydi.</summary>
    private sealed record Projection(
        long Id, string FullName, string Email, string? Phone, long? TelegramId,
        UserRole Role, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

    private sealed record ImportColumns(int FullName, int Phone, int Email, int Role);

    private sealed record ImportRow(
        int Line, string FullName, string Email, string? Phone, string? RawPhone, UserRole Role);
}
