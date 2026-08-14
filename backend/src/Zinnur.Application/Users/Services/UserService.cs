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
    IPasswordHasher hasher,
    IAuthStateCache authState) : IUserService
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

        rows = ApplyGroupFilter(rows, query.GroupId);
        rows = ApplyTelegramFilter(rows, query.TelegramLinked);
        rows = ApplyPhoneFilter(rows, query.PhoneMissing);
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
                u.Id, u.FullName, u.Email, u.Phone, u.TelegramId, u.TelegramUsername,
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
        var phone = RequirePhoneForStaff(request.Phone, request.Role);

        await EnsureEmailFreeAsync(email, exceptUserId: null, ct);
        await EnsurePhoneFreeAsync(phone, exceptUserId: null, ct);

        var user = new User
        {
            FullName = fullName,
            Email = email,

            // 🔴 O'LIK USTUNNI TO'LDIRISH — KIRISH MA'LUMOTI EMAS.
            //    Sabab `PlaceholderPasswordHashAsync` izohida.
            PasswordHash = await PlaceholderPasswordHashAsync(ct),
            Role = request.Role,
            IsActive = request.IsActive,
        };

        user.SetPhone(request.Phone);

        db.Users.Add(user);
        await SaveWithUniqueGuardAsync(ct);

        return new CreateUserResponse(Map(user));
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

        // ★ TEKSHIRUV YANGI ROL BO'YICHA (rol berilmasa — hozirgisi
        //   bo'yicha). O'quvchini ustozga aylantirayotgan so'rov ham
        //   telefon talab qiladi: aks holda "ko'tarilgan" xodim ayni
        //   amaldan keyin tizimga umuman kira olmay qolardi va sababi
        //   hech qayerda ko'rinmasdi.
        var phone = RequirePhoneForStaff(request.Phone, request.Role ?? user.Role);

        await EnsureEmailFreeAsync(email, exceptUserId: user.Id, ct);
        await EnsurePhoneFreeAsync(phone, exceptUserId: user.Id, ct);

        user.FullName = fullName;
        user.Email = email;
        user.SetPhone(request.Phone);

        // ChangeRole ichida InvalidateTokens() bor — rol o'zgarsa eski tokendagi
        // rol claim'i darhol yaroqsiz bo'ladi.
        var roleChanged = request.Role is { } newRole && newRole != user.Role;
        if (request.Role is { } role)
            user.ChangeRole(role);

        await SaveWithUniqueGuardAsync(ct);

        // Rol o'zgargan bo'lsa `ChangeRole` sessiyalarni bekor qildi — kesh ham
        // tozalansin, aks holda eski roldagi token 60 sekund qabul qilinardi.
        if (roleChanged)
            await authState.InvalidateAsync(user.Id, ct);

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

        // ★ Sessiya holati keshi tozalanadi. Faollashtirishda ham tozalanadi:
        // aks holda keshda "faol emas" yozuvi qolib, tiklangan foydalanuvchi
        // 60 sekundgacha kira olmasdi.
        await authState.InvalidateAsync(user.Id, ct);

        return Map(user);
    }

    // ══════════════════════════════════════════════════════════════════
    // ⚠️ `ResetPasswordAsync` OLIB TASHLANDI (2026-08-13).
    //
    // Metod vaqtinchalik parol yasab, uni javobda qaytarardi. Parol bilan
    // kirish yo'q bo'lgach u FAOL ZARARLI bo'lib qolardi: xodim "parolni
    // tiklab berdim" deb foydalanuvchiga hech qayerda ishlamaydigan satrni
    // uzatardi, foydalanuvchi esa uni kirish ekranida qidirib vaqt
    // yo'qotardi.
    //
    // ★ UNING IKKINCHI, YASHIRIN VAZIFASI — "barcha sessiyalarni bekor
    //   qilish" — YO'QOLMADI. Ayni natijani beradigan ikkita amal qoldi:
    //     • `POST /users/{id}/deactivate` — profilni yopadi va
    //       `InvalidateTokens()` chaqiradi;
    //     • `POST /users/{id}/telegram/unlink` — bog'lanishni uzadi,
    //       sessiyalarni bekor qiladi VA audit iziga yozadi.
    //
    //   Ikkinchisi o'g'irlangan qurilma holati uchun AYNAN to'g'ri amal:
    //   u nafaqat sessiyani, balki qayta kirish imkoniyatini ham yopadi.
    // ══════════════════════════════════════════════════════════════════

    // ================================================================= Telegram

    public async Task<TelegramUnlinkResponse> UnlinkTelegramAsync(
        long id, TelegramUnlinkRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        var user = await LoadTargetAsync(id, ct);

        // RUXSAT: YAGONA qoida (X-4). Ya'ni o'quv bo'limi xodimi admin yoki
        // boshqa o'quv bo'limi xodimining Telegram'ini uzib, uning profilini
        // o'ziga bog'lab olishga urinib ko'ra olmaydi.
        EnsureCanManage(actor, user);

        // 🔴 Domain metodi: `TelegramId`/`TelegramUsername`/`TelegramLinkedAt`
        //    tozalanadi VA `TokenVersion` oshiriladi. Bog'lanmagan profilda
        //    `DomainException` -> 409 (middleware xaritalaydi), ya'ni "hech
        //    nima o'zgarmadi, lekin 200 qaytdi" holati mumkin emas.
        var (oldTelegramId, oldUsername) = user.UnlinkTelegram(DateTimeOffset.UtcNow);

        // AUDIT AYNI KUZATUVCHIDA: profil o'zgarishi va iz BITTA
        // `SaveChanges` — ya'ni bitta tranzaksiya — bilan yoziladi. Amal
        // yiqilsa iz ham qolmaydi (bo'lmagan o'zgarish haqida yozuv yo'q),
        // muvaffaqiyatli bo'lsa esa izsiz uzish MUMKIN EMAS.
        db.TelegramUnlinkAudits.Add(new TelegramUnlinkAudit
        {
            UserId = user.Id,
            ActorId = actor.Id,
            OldTelegramId = oldTelegramId,
            OldTelegramUsername = oldUsername,
            Reason = NormalizeReason(request.Reason),
        });

        await db.SaveChangesAsync(ct);

        // ★ KESHNI TOZALASH SHART. `TokenVersion` bazada oshdi, lekin sessiya
        //   holati Redis'da 60 sekund keshlanadi — tozalanmasa o'quvchining
        //   eski kirish tokeni shu vaqt ichida hamon qabul qilinardi va
        //   "platformaga kira olmaydi" talabi CHALA bajarilgan bo'lardi.
        //   (Ayni naqsh `SetActiveAsync` va `ResetPasswordAsync` da.)
        await authState.InvalidateAsync(user.Id, ct);

        return new TelegramUnlinkResponse(null, null);
    }

    /// <summary>Auditga yoziladigan sababni tozalaydi va chegaraga qirqadi.</summary>
    private static string? NormalizeReason(string? reason)
    {
        var value = reason?.Trim();

        if (string.IsNullOrEmpty(value)) return null;

        return value.Length <= TelegramUnlinkAudit.MaxReasonLength
            ? value
            : value[..TelegramUnlinkAudit.MaxReasonLength];
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

        // 2) O'lik `PasswordHash` ustunini CHEKLANGAN PARALLELLIK bilan
        //    to'ldiramiz (bu KIRISH MA'LUMOTI emas — sabab
        //    `PlaceholderPasswordHashAsync` izohida).
        //    BCrypt ~100-250 ms sof CPU; ketma-ket qilinsa 200 qator ~30 soniya.
        //    Parallellik cheklanmasa import butun thread pool'ni egallab,
        //    boshqa so'rovlar javobsiz qolardi.
        var hashes = new string[accepted.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, accepted.Count),
            new ParallelOptions { MaxDegreeOfParallelism = HashParallelism, CancellationToken = ct },
            async (i, token) =>
            {
                hashes[i] = await PlaceholderPasswordHashAsync(token);
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

            // 🔴 CSV — XODIM UCHUN TELEFON QOIDASINING IKKINCHI ESHIGI.
            //
            // Qoida `RequirePhoneForStaff` da, lekin import u yerdan
            // O'TMAYDI (u `User` entity'sini to'g'ridan-to'g'ri yasaydi —
            // paketlab yozish uchun). Tekshiruv bu yerda takrorlanmasa,
            // CSV himoyani chetlab o'tadigan yo'l bo'lib qolardi va
            // 200 ta kirolmaydigan xodim bitta fayl bilan yaratilardi.
            //
            // ★ QATOR RAD ETILADI, BUTUN FAYL EMAS — import falsafasi
            //   shunday: bitta xato qator qolganlarini to'xtatmaydi.
            if (phone is null && role != UserRole.Student)
            {
                issues.Add(new UserImportIssue(
                    line,
                    "Xodim uchun telefon raqami majburiy (kirish faqat telefon orqali)."));
                continue;
            }

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
    /// GURUH bo'yicha filtr: shu guruhda <c>Active</c> a'zo bo'lganlar.
    ///
    /// ★ Yozilish shakli MUHIM: avval a'zoliklar guruh va holat bo'yicha
    /// FILTRLANADI, so'ng foydalanuvchi Id'si o'sha to'plamda izlanadi
    /// (<c>IN (SELECT ...)</c> = yarim birlashma). Shu shaklda Postgres
    /// <c>IX_GroupMembers_GroupId_Status</c> indeksini AYNAN prefiksidan
    /// boshlab ishlatadi.
    ///
    /// ★ Nima uchun faqat <c>Active</c>: sabab <c>UserListQuery.GroupId</c>
    /// izohida (chiqarilgan o'quvchi ro'yxatda ko'rinsa xodim uni hali
    /// o'qiyapti deb o'ylardi).
    /// </summary>
    private IQueryable<User> ApplyGroupFilter(IQueryable<User> rows, long? groupId)
    {
        if (groupId is not { } id) return rows;

        var members = db.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == id && m.Status == MemberStatus.Active)
            .Select(m => m.StudentId);

        return rows.Where(u => members.Contains(u.Id));
    }

    /// <summary>
    /// TELEGRAM bo'yicha filtr. <c>true</c> — bog'langanlar.
    ///
    /// <c>TelegramId != null</c> sharti FILTRLI UNIKAL indeks
    /// (<c>IX_Users_TelegramId</c>, <c>WHERE "TelegramId" IS NOT NULL</c>)
    /// bilan aynan mos tushadi. <c>false</c> holati esa indekssiz: bog'lanmagan
    /// foydalanuvchilar ko'pchilikni tashkil qiladi va ular uchun indeks
    /// baribir foyda bermasdi (Postgres seq scan'ni tanlardi).
    /// </summary>
    private static IQueryable<User> ApplyTelegramFilter(IQueryable<User> rows, bool? linked) =>
        linked switch
        {
            true => rows.Where(u => u.TelegramId != null),
            false => rows.Where(u => u.TelegramId == null),
            null => rows,
        };

    /// <summary>
    /// 🔴 KIRISHGA TAYYORLIK FILTRI — <c>PhoneNormalized</c> BO'YICHA,
    /// <c>Phone</c> bo'yicha EMAS.
    ///
    /// ★ FARQ HAL QILUVCHI: eski tizimdan ko'chirishda dublikat raqamli
    /// foydalanuvchilarda <c>Phone</c> to'ldirilgan, <c>PhoneNormalized</c>
    /// esa <c>NULL</c> qolgan. Bot ham, kirish oqimi ham AYNAN
    /// normalizatsiyalangan ustun bo'yicha izlaydi — ya'ni bu odamlar
    /// CRM'da raqamli, tizim uchun esa raqamsiz.
    ///
    /// Filtr indeksli: <c>IX_Users_PhoneNormalized</c> FILTRLI unikal
    /// indeks (<c>WHERE "PhoneNormalized" IS NOT NULL</c>) — ya'ni
    /// <c>IS NULL</c> shoxi indeksdan foydalana olmaydi va to'liq
    /// skanerlash bo'ladi. Bu QABUL QILINGAN: so'rovni faqat o'quv
    /// bo'limi, kunda bir necha marta chaqiradi.
    /// </summary>
    private static IQueryable<User> ApplyPhoneFilter(IQueryable<User> rows, bool? missing) =>
        missing switch
        {
            true => rows.Where(u => u.PhoneNormalized == null),
            false => rows.Where(u => u.PhoneNormalized != null),
            null => rows,
        };

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
    /// ════════════════════════════════════════════════════════════════
    /// 🔴 O'LIK USTUNNI TO'LDIRUVCHI QIYMAT — KIRISH MA'LUMOTI EMAS
    /// ════════════════════════════════════════════════════════════════
    ///
    /// <c>User.PasswordHash</c> ustuni <c>required</c> va <c>NOT NULL</c>.
    /// Uni bazadan olib tashlash MIGRATSIYA talab qilardi; bu esa ayni
    /// paytda ATAYLAB qilinmadi (sabab <c>User.PasswordHash</c> izohida
    /// va hisobotda).
    ///
    /// Shuning uchun har yangi foydalanuvchiga HECH KIM BILMAYDIGAN
    /// tasodifiy qiymatning hash'i yoziladi.
    ///
    /// ★ NIMA UCHUN QAT'IY BIR XIL SATR EMAS ("disabled" kabi): u holda
    ///   butun bazada bitta hash takrorlanardi. Parol tekshiruvi
    ///   kelajakda biror yo'l bilan qaytib kelsa (yoki eski nusxadan
    ///   tiklansa), BITTA ma'lum qiymat butun tizimni ochib berardi.
    ///   Tasodifiy qiymat esa hech qachon hech kimga ko'rsatilmaydi va
    ///   xotirada ham qolmaydi.
    ///
    /// ★ NIMA UCHUN BCrypt (qimmat, ~250 ms) SAQLANDI: ustun formati
    ///   o'zgarmasin. Bir xil shakldagi ma'lumot kelajakdagi migratsiyani
    ///   (ustunni tashlash) sodda va bir xil qiladi.
    /// </summary>
    private Task<string> PlaceholderPasswordHashAsync(CancellationToken ct) =>
        hasher.HashAsync(
            RandomNumberGenerator.GetString(PasswordAlphabet, PlaceholderPasswordLength), ct);

    /// <summary>
    /// 🔴 XODIM UCHUN TELEFON MAJBURIY (2026-08-13).
    ///
    /// Sabab: kirishning yagona yo'li — telefon + o'sha raqamga
    /// bog'langan Telegram hisobiga keladigan kod. Telefonsiz xodim
    /// yaratish — kirolmaydigan xodim yaratish, va bu faqat u birinchi
    /// marta kirmoqchi bo'lganda ma'lum bo'lardi.
    ///
    /// ★ TEKSHIRUV <c>NormalizePhone</c> NATIJASI BO'YICHA, "bo'sh
    ///   emasmi" bo'yicha EMAS: <c>"-"</c> yoki <c>"yo'q"</c> kabi qiymat
    ///   bo'sh emas, lekin <c>SetPhone</c> uni raqamsiz deb
    ///   <c>PhoneNormalized = null</c> qilib qo'yardi — ya'ni tekshiruvdan
    ///   o'tgan, lekin baribir kira olmaydigan xodim. Aynan shu holat
    ///   eski ko'chirishdan keyin butun bir guruhda mavjud
    ///   (<c>UserListQuery.PhoneMissing</c> izohi).
    ///
    /// ★ O'QUVCHI ISTISNO: sabab <c>CreateUserRequest.Phone</c> izohida.
    /// </summary>
    /// <returns>Normalizatsiyalangan raqam (o'quvchi uchun <c>null</c> bo'lishi mumkin).</returns>
    private static string? RequirePhoneForStaff(string? rawPhone, UserRole role)
    {
        var phone = User.NormalizePhone(rawPhone);

        if (phone is not null || role == UserRole.Student)
            return phone;

        throw Invalid(
            nameof(User.Phone),
            "Xodim uchun telefon raqami majburiy: tizimga kirish faqat telefon orqali "
            + "bo'ladi va kirish kodi shu raqamga bog'langan Telegram hisobiga yuboriladi.");
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private static UserDetailsDto Map(User u) => new(
        u.Id, u.FullName, u.Email, u.Phone, u.TelegramId, u.TelegramUsername,
        u.Role.ToString(), u.IsActive, u.CreatedAt, u.UpdatedAt);

    private static UserDetailsDto Map(Projection p) => new(
        p.Id, p.FullName, p.Email, p.Phone, p.TelegramId, p.TelegramUsername,
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
    /// <summary>
    /// O'lik <c>PasswordHash</c> ustunini to'ldiradigan tasodifiy qiymat
    /// uzunligi (<see cref="PlaceholderPasswordHashAsync"/>).
    ///
    /// ⚠️ Ilgari `TemporaryPasswordLength` deb atalardi va qiymat
    /// foydalanuvchiga KO'RSATILARDI. 2026-08-13 dan u hech kimga
    /// ko'rinmaydi — nom shu sababdan o'zgartirildi, aks holda kod
    /// o'qigan odam hamon "vaqtinchalik parol" chiqadi deb o'ylardi.
    /// </summary>
    private const int PlaceholderPasswordLength = 14;

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
        string? TelegramUsername, UserRole Role, bool IsActive,
        DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

    private sealed record ImportColumns(int FullName, int Phone, int Email, int Role);

    private sealed record ImportRow(
        int Line, string FullName, string Email, string? Phone, string? RawPhone, UserRole Role);
}
