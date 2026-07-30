using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.LiveSessions.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.LiveSessions.Services;

/// <inheritdoc cref="IAttendanceService"/>
public sealed class AttendanceService(
    IApplicationDbContext db,
    TimeProvider clock) : IAttendanceService
{
    /// <summary>
    /// Sabab uzunligi chegarasi — `AttendanceConfiguration.ReasonMaxLength`
    /// bilan AYNAN bir xil.
    ///
    /// NIMA UCHUN BU YERDA HAM: baza chegarasi buzilsa `DbUpdateException`
    /// bo'lardi, ya'ni foydalanuvchi "Serverda kutilmagan xato" (500)
    /// ko'rardi. Bu yerdagi tekshiruv esa aniq 400 va maydon nomini
    /// beradi. Application qatlami Infrastructure'ni KO'RMAYDI, shuning
    /// uchun konstantaga havola qilib bo'lmaydi — qiymat ataylab
    /// takrorlangan va ikkalasida ham izoh qo'yilgan.
    /// </summary>
    private const int ReasonMaxLength = 300;

    /// <inheritdoc />
    public async Task<SessionAttendanceDto> GetSessionAttendanceAsync(
        long sessionId, long actorId, CancellationToken ct = default)
    {
        var (session, _) = await LoadAndAuthorizeAsync(sessionId, actorId, ct);

        var rows = await BuildRowsAsync(session, ct);

        return new SessionAttendanceDto(
            session.Id,
            session.GroupId,
            session.Group?.Name ?? string.Empty,
            session.Title,
            session.Type.ToString(),
            session.Status.ToString(),
            session.ScheduledStart,
            session.ScheduledEnd,

            // Hozircha "ko'ra oladigan tuzata ham oladi" — biz bu yerga
            // faqat ruxsat tekshiruvidan O'TIB kelamiz.
            CanEdit: true,
            rows);
    }

    /// <inheritdoc />
    public async Task<AttendanceRowDto> UpdateAsync(
        long sessionId,
        long studentId,
        UpdateAttendanceRequest request,
        long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = ValidateStatus(request.Status);
        var reason = ValidateReason(request.Reason);

        var (session, _) = await LoadAndAuthorizeAsync(sessionId, actorId, ct, tracking: true);

        // BEKOR QILINGAN dars: davomat belgilash ma'nosiz va zararli.
        // Bunday dars hisobotlarga umuman kirmaydi (`AttendanceSummaryService`
        // faqat `Ended` darslarni sanaydi), ya'ni yozuv ko'rinmas bo'lib
        // qolardi — ustoz esa "belgiladim" deb o'ylardi.
        if (session.Status == SessionStatus.Cancelled)
            throw new ConflictException("Bekor qilingan dars uchun davomat belgilanmaydi.");

        var student = await LoadStudentOfSessionAsync(session, studentId, ct);

        var attendance = await db.Attendances
            .AsTracking()
            .FirstOrDefaultAsync(a => a.SessionId == sessionId && a.StudentId == studentId, ct);

        // ★ ESKI QIYMATLAR AVVAL olinadi: `ApplyManual` ularni yozib
        //   yuborgandan keyin auditga yozadigan narsa qolmasdi.
        var existed = attendance is not null;
        var oldStatus = existed ? attendance!.Status : (AttendanceStatus?)null;
        var oldIsManual = existed && attendance!.IsManual;
        var oldReason = existed ? attendance!.Reason : null;

        if (attendance is null)
        {
            // O'quvchi xonaga UMUMAN kirmagan — qator hali yo'q. Ustoz uni
            // baribir "kelgan" deb belgilay olishi kerak (internet uzilgan,
            // telefonda tinglagan va h.k.).
            attendance = new Attendance { SessionId = sessionId, StudentId = studentId };
            db.Attendances.Add(attendance);
        }

        var now = clock.GetUtcNow();

        // Vaqt o'lchovlariga TEGILMAYDI — sababi `Attendance.ApplyManual` da.
        attendance.ApplyManual(status, reason, now);

        db.AttendanceAudits.Add(new AttendanceAudit
        {
            // `AttendanceId` yangi qatorda hali 0; EF uni SaveChanges paytida
            // FK bog'lanishi orqali O'ZI to'ldiradi (`Attendance` navigatsiyasi
            // yo'q, shuning uchun bog'lanish oshkora berilishi kerak).
            Attendance = attendance,
            SessionId = sessionId,
            StudentId = studentId,
            ActorId = actorId,
            OldStatus = oldStatus,
            NewStatus = status,
            OldIsManual = oldIsManual,
            OldReason = oldReason,
            NewReason = reason,
            CreatedAt = now,
        });

        // ★ BITTA TRANZAKSIYA: tuzatish va uning izi birga saqlanadi.
        // Ikki `SaveChanges` bo'lsa, ikkinchisi yiqilganda o'zgargan, lekin
        // IZSIZ qator qolardi — audit esa aynan shunday holat bo'lmasin
        // deb qo'shilgan.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // `UX_Attendances_SessionId_StudentId` — ikki xodim bir katakni
            // bir vaqtda birinchi marta belgilaganda. 500 emas, 409: bu
            // bizning bug emas va "qayta urinib ko'ring" to'g'ri maslahat.
            throw new ConflictException(
                "Bu davomat qatori ayni paytda boshqa xodim tomonidan o'zgartirildi. "
                + "Sahifani yangilab, qaytadan urinib ko'ring.", ex);
        }

        return new AttendanceRowDto(
            studentId,
            student,
            attendance.Status,
            attendance.IsManual,
            attendance.Reason,
            attendance.FirstJoinAt,
            attendance.LeftAt,
            attendance.DurationSeconds,
            actorId,
            await ActorNameAsync(actorId, ct),
            now);
    }

    // ================================================================= o'qish

    /// <summary>
    /// Varaq qatorlari.
    ///
    /// ★ N+1 YO'Q — jami UCHTA so'rov, dars ichidagi o'quvchilar sonidan
    /// QAT'I NAZAR:
    ///   1) shu darsning davomat yozuvlari (o'quvchi ismi bilan);
    ///   2) guruhning FAOL a'zolari (hali yozuvi yo'qlar uchun bo'sh qator);
    ///   3) shu darsning audit izlari (kim oxirgi tuzatgan).
    ///
    /// Audit izlari TO'LIQ o'qiladi va oxirgisi XOTIRADA tanlanadi: bitta
    /// dars bo'yicha ular o'nlab qator (qo'lda tuzatish — kamdan-kam
    /// hodisa). SQL'da "har o'quvchi uchun oxirgisi" oyna funksiyasi yoki
    /// lateral join talab qilardi — bu yerda foydasiz murakkablik.
    /// </summary>
    private async Task<IReadOnlyList<AttendanceRowDto>> BuildRowsAsync(
        LiveSession session, CancellationToken ct)
    {
        var records = await db.Attendances.AsNoTracking()
            .Where(a => a.SessionId == session.Id)
            .Select(a => new RecordRow(
                a.StudentId,
                a.Student!.FullName,
                a.Status,
                a.IsManual,
                a.Reason,
                a.FirstJoinAt,
                a.LeftAt,
                a.DurationSeconds))
            .ToListAsync(ct);

        // ★ A'ZOLIK FILTRI `LiveSessionService.LoadAndAuthorizeAsync` BILAN
        //   BIR XIL (faol a'zolik): varaqda AYNAN darsga kira oladigan
        //   o'quvchilar turadi. Boshqacha bo'lsa ustoz ro'yxatda ko'rgan
        //   o'quvchiga baho qo'yardi, o'quvchi esa darsga umuman kira
        //   olmasdi.
        var members = await db.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == session.GroupId && m.Status == MemberStatus.Active)
            .Select(m => new MemberRow(m.StudentId, m.Student!.FullName))
            .ToListAsync(ct);

        var stamps = await db.AttendanceAudits.AsNoTracking()
            .Where(x => x.SessionId == session.Id)
            .OrderBy(x => x.Id)
            .Select(x => new EditStamp(x.StudentId, x.ActorId, x.Actor!.FullName, x.CreatedAt))
            .ToListAsync(ct);

        // Ortidan kelgani oldingisini almashtiradi — `Id` bo'yicha o'sish
        // tartibida o'qilgani uchun oxirida OXIRGI tuzatish qoladi.
        var lastEdit = new Dictionary<long, EditStamp>(stamps.Count);
        foreach (var stamp in stamps)
            lastEdit[stamp.StudentId] = stamp;

        var rows = new Dictionary<long, AttendanceRowDto>(records.Count + members.Count);

        // ★ AVVAL a'zolar — hatto yozuvi YO'Q o'quvchi ham varaqda ko'rinsin
        //   (aks holda ustoz uni belgilay olmasdi: qatorni ko'rmaydi).
        foreach (var member in members)
        {
            rows[member.StudentId] = new AttendanceRowDto(
                member.StudentId, member.FullName,
                Status: null, IsManual: false, Reason: null,
                FirstJoinAt: null, LeftAt: null, DurationSeconds: 0,
                EditedById: null, EditedByName: null, EditedAt: null);
        }

        // ★ KEYIN yozuvlar — ular a'zo qatorining ustiga yoziladi.
        //
        // Yozuvi bor, lekin endi FAOL A'ZO BO'LMAGAN o'quvchi ham qoladi:
        // u dars o'tgan paytda guruhda edi va uning bahosi hisobotga
        // kirgan. Uni yashirish "davomat foizi qayerdan chiqdi?" degan
        // javobsiz savol tug'dirardi.
        foreach (var record in records)
        {
            lastEdit.TryGetValue(record.StudentId, out var edit);

            rows[record.StudentId] = new AttendanceRowDto(
                record.StudentId,
                record.FullName,
                record.Status,
                record.IsManual,
                record.Reason,
                record.FirstJoinAt,
                record.LeftAt,
                record.DurationSeconds,
                edit?.ActorId,
                edit?.ActorName,
                edit?.At);
        }

        // Tartib ISM bo'yicha; `Ordinal` — madaniyatga bog'liq bo'lmagan,
        // ya'ni server sozlamasidan qat'i nazar BIR XIL natija
        // (`CA1305` bilan bir mulohaza). Teng ismlarda `Id` — tartib
        // so'rovdan so'rovga sakramasin.
        return rows.Values
            .OrderBy(r => r.StudentName, StringComparer.Ordinal)
            .ThenBy(r => r.StudentId)
            .ToList();
    }

    // ================================================================= ruxsat

    /// <summary>
    /// Darsni yuklaydi va davomatni BOSHQARISH huquqini tekshiradi.
    ///
    /// ★ RUXSATNING YAGONA JOYI — o'qish ham, yozish ham shu yerdan
    /// o'tadi. Ikki alohida tekshiruv bo'lsa, biri (odatda o'qish)
    /// vaqt o'tib zaifroq qolib ketardi.
    /// </summary>
    private async Task<(LiveSession Session, User Actor)> LoadAndAuthorizeAsync(
        long sessionId, long actorId, CancellationToken ct, bool tracking = false)
    {
        var query = db.LiveSessions
            .Include(s => s.Group)
            .ThenInclude(g => g!.CuratorGroup)
            .AsQueryable();

        query = tracking ? query.AsTracking() : query.AsNoTracking();

        var session = await query.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(nameof(LiveSession), sessionId);

        // Rol TOKEN'dan emas, BAZADAN: kirish tokeni 15 daqiqa yashaydi,
        // ya'ni endi o'chirilgan yoki roli pasaytirilgan xodim eski token
        // bilan davomat tuzata olmasligi kerak.
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        if (!CanManage(actor, session))
        {
            throw new ForbiddenException(
                "Davomatni faqat guruh ustozi, kuratori yoki o'quv bo'limi tuzata oladi.");
        }

        return (session, actor);
    }

    /// <summary>
    /// KIM TUZATA OLADI.
    ///
    /// ★ O'QUVCHI HECH QACHON — u ro'yxatda umuman yo'q. Bu shunchaki
    /// "ma'lumot ko'rinmasin" emas: o'quvchi o'z davomatini "kelgan"
    /// qilib qo'ya olsa, butun reyting va davomat hisoboti ma'nosini
    /// yo'qotardi.
    ///
    /// Ko'rish qoidasi `GroupService.EnsureCanRead` BILAN BIR XIL —
    /// guruh kartochkasini ko'ra oladigan xodim uning darsidagi davomatni
    /// ham ko'radi. Ikki joyda ikki xil bo'lsa, ustoz guruhni ko'rib
    /// davomatida 403 olardi (yoki teskarisi — bu esa ma'lumot sizishi).
    /// </summary>
    private static bool CanManage(User actor, LiveSession session)
    {
        if (actor.Role is UserRole.Admin or UserRole.Academic)
            return true;

        if (actor.Role is not (UserRole.Teacher or UserRole.Assistant))
            return false;

        // Darsni O'ZI o'tgan xodim (masalan o'rinbosar) — guruhga
        // biriktirilmagan bo'lsa ham.
        if (session.HostId == actor.Id)
            return true;

        if (session.Group is not { } group)
            return false;

        // Guruhning o'z ustozi/kuratori, yoki guruh BOG'LANGAN kurator
        // guruhining xodimi (uning darsida shu guruh o'quvchilari o'qiydi).
        return group.IsStaff(actor.Id)
            || (group.CuratorGroup is { } curator && curator.IsStaff(actor.Id));
    }

    // ================================================================= tekshiruv

    private static AttendanceStatus ValidateStatus(AttendanceStatus? status)
    {
        if (status is not { } value)
            throw Invalid("status", "Davomat holati tanlanishi shart.");

        // Enum'ga kirmaydigan son (`"status": 99`) JSON'dan o'tib ketishi
        // mumkin — u bazaga yozilsa jadval o'qib bo'lmaydigan bo'lardi.
        if (!Enum.IsDefined(value))
            throw Invalid("status", "Noma'lum davomat holati.");

        return value;
    }

    private static string? ValidateReason(string? reason)
    {
        var trimmed = reason?.Trim();

        if (string.IsNullOrEmpty(trimmed)) return null;

        if (trimmed.Length > ReasonMaxLength)
            throw Invalid("reason", $"Sabab {ReasonMaxLength} belgidan oshmasin.");

        return trimmed;
    }

    /// <summary>
    /// O'quvchi shu darsga TEGISHLIMI va ismi nima.
    ///
    /// ★ TEKSHIRUV MAJBURIY: aks holda xodim istalgan foydalanuvchi
    /// Id'sini yuborib, o'zi ko'rmaydigan guruhdagi o'quvchiga (yoki
    /// hatto boshqa ustozga) davomat yozib qo'yardi.
    ///
    /// A'zolik holati TEKSHIRILMAYDI (faol/pauza/to'xtatgan — farqi yo'q):
    /// dars O'TGAN paytda o'quvchi guruhda edi va o'sha kunning bahosini
    /// keyin ham tuzatish kerak bo'ladi.
    /// </summary>
    private async Task<string> LoadStudentOfSessionAsync(
        LiveSession session, long studentId, CancellationToken ct)
    {
        var belongs = await db.GroupMembers.AsNoTracking()
            .AnyAsync(m => m.GroupId == session.GroupId && m.StudentId == studentId, ct);

        if (!belongs)
        {
            // Yozuvi bor, lekin a'zolik qatori o'chirilgan holat ham
            // tuzatilishi kerak (baho hisobotda turibdi).
            belongs = await db.Attendances.AsNoTracking()
                .AnyAsync(a => a.SessionId == session.Id && a.StudentId == studentId, ct);
        }

        if (!belongs)
            throw new NotFoundException(nameof(GroupMember), studentId);

        return await db.Users.AsNoTracking()
            .Where(u => u.Id == studentId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), studentId);
    }

    private async Task<string?> ActorNameAsync(long actorId, CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct);

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    // ================================================================= tor proyeksiyalar

    private sealed record RecordRow(
        long StudentId,
        string FullName,
        AttendanceStatus Status,
        bool IsManual,
        string? Reason,
        DateTimeOffset? FirstJoinAt,
        DateTimeOffset? LeftAt,
        int DurationSeconds);

    private sealed record MemberRow(long StudentId, string FullName);

    private sealed record EditStamp(
        long StudentId, long ActorId, string ActorName, DateTimeOffset At);
}
