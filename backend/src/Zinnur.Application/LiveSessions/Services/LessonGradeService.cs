using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.LiveSessions.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.LiveSessions.Services;

/// <inheritdoc cref="ILessonGradeService"/>
public sealed class LessonGradeService(
    IApplicationDbContext db,
    TimeProvider clock) : ILessonGradeService
{
    /// <inheritdoc />
    public async Task<SessionLessonGradesDto> GetSessionGradesAsync(
        long sessionId, long actorId, CancellationToken ct = default)
    {
        var session = await LoadAndAuthorizeAsync(sessionId, actorId, ct);

        var rows = await BuildRowsAsync(session, ct);

        return new SessionLessonGradesDto(
            session.Id,
            session.GroupId,
            session.Group?.Name ?? string.Empty,
            session.Title,
            session.Type.ToString(),
            session.Status.ToString(),
            session.ScheduledStart,
            session.ScheduledEnd,
            LessonGrade.DefaultMaxScore,

            // Hozircha "ko'ra oladigan qo'ya ham oladi" — biz bu yerga faqat
            // ruxsat tekshiruvidan O'TIB kelamiz.
            CanEdit: true,
            rows);
    }

    /// <inheritdoc />
    public async Task<LessonGradeRowDto> UpsertAsync(
        long sessionId,
        long studentId,
        UpsertLessonGradeRequest request,
        long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var score = ValidateScore(request.Score, request.MaxScore);
        var maxScore = ValidateMaxScore(request.MaxScore);
        var comment = ValidateComment(request.Comment);

        var session = await LoadAndAuthorizeAsync(sessionId, actorId, ct, tracking: true);

        // BEKOR QILINGAN dars: baho qo'yish ma'nosiz va zararli — bunday
        // dars o'tilmagan, ya'ni baholanadigan ish ham yo'q. `AttendanceService`
        // dagi bilan AYNI qoida.
        if (session.Status == SessionStatus.Cancelled)
            throw new ConflictException("Bekor qilingan dars uchun baho qo'yilmaydi.");

        var studentName = await LoadStudentOfSessionAsync(session, studentId, ct);

        var grade = await db.LessonGrades
            .AsTracking()
            .FirstOrDefaultAsync(g => g.SessionId == sessionId && g.StudentId == studentId, ct);

        // ★ ESKI QIYMATLAR AVVAL olinadi: `Apply` ularni yozib yuborgandan
        //   keyin auditga yozadigan narsa qolmasdi.
        var existed = grade is not null;
        var oldScore = existed ? grade!.Score : (decimal?)null;
        var oldMaxScore = existed ? grade!.MaxScore : null;
        var oldComment = existed ? grade!.Comment : null;

        if (grade is null)
        {
            grade = new LessonGrade { SessionId = sessionId, StudentId = studentId };
            db.LessonGrades.Add(grade);
        }

        var now = clock.GetUtcNow();

        // Domain invarianti (ball ≤ maxraj) SHU YERDA ham tekshiriladi —
        // yuqoridagi `Validate*` esa foydalanuvchiga aniq 400 beradi.
        grade.Apply(score, maxScore, comment, actorId, now);

        // ★ IZ BAHO QATORIGA EMAS, DARS+O'QUVCHIGA bog'lanadi — shuning
        //   uchun bu yerda `AttendanceService` dagidek navigatsiya hiylasi
        //   KERAK EMAS (yangi qatorning `Id` si `SaveChanges` gacha 0
        //   bo'lishi bizga umuman tegmaydi). Sabab `LessonGradeAudit`
        //   izohida: baho O'CHIRILGANDA ham iz qolishi kerak.
        db.LessonGradeAudits.Add(new LessonGradeAudit
        {
            SessionId = sessionId,
            StudentId = studentId,
            ActorId = actorId,
            OldScore = oldScore,
            NewScore = score,
            OldMaxScore = oldMaxScore,
            NewMaxScore = maxScore,
            OldComment = oldComment,
            NewComment = grade.Comment,
            CreatedAt = now,
        });

        // ★ BITTA TRANZAKSIYA: baho va uning izi birga saqlanadi.
        await SaveAsync(ct);

        return new LessonGradeRowDto(
            studentId,
            studentName,
            grade.Score,
            grade.MaxScore,
            grade.Percent,
            grade.Comment,
            actorId,
            await ActorNameAsync(actorId, ct),
            grade.GradedAt);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        long sessionId, long studentId, long actorId, CancellationToken ct = default)
    {
        // ★ BEKOR QILINGAN DARS BU YERDA TO'SILMAYDI (`UpsertAsync` dan
        //   FARQI): dars baholangandan KEYIN bekor qilingan bo'lsa,
        //   endi ma'nosiz bo'lib qolgan bahoni olib tashlashning yagona
        //   yo'li shu amal.
        await LoadAndAuthorizeAsync(sessionId, actorId, ct, tracking: true);

        var grade = await db.LessonGrades
            .AsTracking()
            .FirstOrDefaultAsync(g => g.SessionId == sessionId && g.StudentId == studentId, ct);

        // IDEMPOTENT: bahosi yo'q katakni "o'chirish" — xato emas, shunchaki
        // bajariladigan ish yo'q. Audit ham yozilmaydi (bo'lmagan narsaning
        // o'chirilishi haqidagi yozuv izni SHOVQINGA to'ldirardi).
        if (grade is null) return;

        var now = clock.GetUtcNow();

        db.LessonGradeAudits.Add(new LessonGradeAudit
        {
            SessionId = sessionId,
            StudentId = studentId,
            ActorId = actorId,
            OldScore = grade.Score,

            // `null` — "baho OLIB TASHLANDI". `0` bo'lsa iz "0 qo'yildi"
            // deb o'qilardi va aynan farqni yo'qotardi.
            NewScore = null,
            OldMaxScore = grade.MaxScore,
            NewMaxScore = null,
            OldComment = grade.Comment,
            NewComment = null,
            CreatedAt = now,
        });

        // ★ IZ QATORNI O'CHIRISHDAN OMON QOLADI — u baho qatoriga FK bilan
        //   bog'lanmagan (sabab `LessonGradeAudit` izohida). Bog'langan
        //   bo'lsa Cascade aynan shu yerda butun tarixni o'chirib yuborardi.
        db.LessonGrades.Remove(grade);

        await SaveAsync(ct);
    }

    // ================================================================= o'qish

    /// <summary>
    /// Varaq qatorlari.
    ///
    /// ★ N+1 YO'Q — jami IKKITA so'rov, o'quvchilar sonidan QAT'I NAZAR:
    ///   1) shu darsning baholari (o'quvchi va baholovchi ismi bilan);
    ///   2) guruhning FAOL a'zolari (hali bahosi yo'qlar uchun bo'sh qator).
    ///
    /// ★ AUDIT UMUMAN O'QILMAYDI (davomat varag'idan FARQI): "kim oxirgi
    /// qo'ydi" savoliga qatorning O'ZI javob beradi
    /// (<c>GradedById</c>/<c>GradedAt</c>). Davomatda bu maydonlar yo'q edi,
    /// chunki u yerda odatiy holat — PLATFORMA o'lchovi, ya'ni "odam"
    /// tushunchasi faqat auditda paydo bo'lardi.
    /// </summary>
    private async Task<IReadOnlyList<LessonGradeRowDto>> BuildRowsAsync(
        LiveSession session, CancellationToken ct)
    {
        var records = await db.LessonGrades.AsNoTracking()
            .Where(g => g.SessionId == session.Id)
            .Select(g => new RecordRow(
                g.StudentId,
                g.Student!.FullName,
                g.Score,
                g.MaxScore,
                g.Comment,
                g.GradedById,
                g.GradedBy!.FullName,
                g.GradedAt))
            .ToListAsync(ct);

        // ★ A'ZOLIK FILTRI `AttendanceService` BILAN BIR XIL (faol a'zolik):
        //   ikki varaqda ikki xil o'quvchilar ro'yxati bo'lsa, ustoz bir
        //   tabda ko'rgan o'quvchini ikkinchisida topa olmasdi.
        var members = await db.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == session.GroupId && m.Status == MemberStatus.Active)
            .Select(m => new MemberRow(m.StudentId, m.Student!.FullName))
            .ToListAsync(ct);

        var rows = new Dictionary<long, LessonGradeRowDto>(records.Count + members.Count);

        // ★ AVVAL a'zolar — bahosi YO'Q o'quvchi ham varaqda ko'rinsin
        //   (aks holda ustoz unga baho qo'ya olmasdi: qatorni ko'rmaydi).
        foreach (var member in members)
        {
            rows[member.StudentId] = new LessonGradeRowDto(
                member.StudentId, member.FullName,
                Score: null, MaxScore: null, Percent: null, Comment: null,
                GradedById: null, GradedByName: null, GradedAt: null);
        }

        // ★ KEYIN baholar — ular a'zo qatorining ustiga yoziladi.
        //
        // Bahosi bor, lekin endi FAOL A'ZO BO'LMAGAN o'quvchi ham qoladi:
        // dars o'tgan paytda u guruhda edi va bahosi reytingga kirgan.
        foreach (var record in records)
        {
            rows[record.StudentId] = new LessonGradeRowDto(
                record.StudentId,
                record.FullName,
                record.Score,
                record.MaxScore,
                PercentOf(record.Score, record.MaxScore),
                record.Comment,
                record.GradedById,
                record.GradedByName,
                record.GradedAt);
        }

        // Tartib ISM bo'yicha, `Ordinal` — davomat varag'i bilan AYNI
        // qoida, ya'ni ikki tabdagi qatorlar bir xil tartibda turadi.
        return rows.Values
            .OrderBy(r => r.StudentName, StringComparer.Ordinal)
            .ThenBy(r => r.StudentId)
            .ToList();
    }

    /// <summary>
    /// Foiz — <c>LessonGrade.Percent</c> ning proyeksiya uchun nusxasi.
    ///
    /// Nima uchun entity metodi chaqirilmaydi: qatorlar `Select` bilan
    /// TOR proyeksiyaga olinadi (butun entity emas), ya'ni qo'lda
    /// obyekt yasash kerak bo'lardi. Formula bir joyda o'zgarsa
    /// ikkinchisi ham o'zgarishi shart — shuning uchun ikkalasi ham
    /// <c>Math.Round(..., 1)</c> ni ishlatadi.
    /// </summary>
    private static decimal PercentOf(decimal score, decimal? maxScore)
    {
        var max = maxScore ?? LessonGrade.DefaultMaxScore;
        return max > 0 ? Math.Round(score / max * 100m, 1) : 0m;
    }

    // ================================================================= saqlash

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // `UX_LessonGrades_SessionId_StudentId` — ikki xodim bir katakni
            // bir vaqtda BIRINCHI marta baholaganda. 500 emas, 409: bu
            // bizning bug emas va "qayta urinib ko'ring" to'g'ri maslahat.
            throw new ConflictException(
                "Bu baho ayni paytda boshqa xodim tomonidan o'zgartirildi. "
                + "Sahifani yangilab, qaytadan urinib ko'ring.", ex);
        }
    }

    // ================================================================= ruxsat

    /// <summary>
    /// Darsni yuklaydi va bahoni BOSHQARISH huquqini tekshiradi.
    ///
    /// ★ RUXSATNING YAGONA JOYI — o'qish ham, yozish ham, o'chirish ham
    /// shu yerdan o'tadi.
    /// </summary>
    private async Task<LiveSession> LoadAndAuthorizeAsync(
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
        // bilan baho qo'ya olmasligi kerak.
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        if (!CanManage(actor, session))
        {
            throw new ForbiddenException(
                "Bahoni faqat guruh ustozi, kuratori yoki o'quv bo'limi qo'ya oladi.");
        }

        return session;
    }

    /// <summary>
    /// KIM BAHO QO'YA OLADI.
    ///
    /// ★ QOIDA `AttendanceService.CanManage` NING AYNAN NUSXASI —
    /// ataylab. Ikki varaq (davomat va baho) bir xil ekranning ikki tabi;
    /// qoidalar ayrilsa ustoz bir tabni ko'rib ikkinchisida 403 olardi va
    /// buni hech kim tushuntira olmasdi.
    ///
    /// ★ O'QUVCHI HECH QACHON — u ro'yxatda umuman yo'q. O'z bahosini
    /// o'zi qo'ya olsa reyting ham, hisobot ham ma'nosini yo'qotardi.
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

        return group.IsStaff(actor.Id)
            || (group.CuratorGroup is { } curator && curator.IsStaff(actor.Id));
    }

    // ================================================================= tekshiruv

    private static decimal ValidateScore(decimal? score, decimal? maxScore)
    {
        if (score is not { } value)
            throw Invalid("score", "Baho kiritilishi shart.");

        if (value < 0)
            throw Invalid("score", "Baho manfiy bo'lmaydi.");

        // Maxraj tekshiruvi SHU YERDA ham: aks holda `LessonGrade.Apply`
        // `DomainException` ko'tarardi, u esa global xaritada 409 ga
        // tushadi — so'rov QATORIDAGI xato uchun noto'g'ri javob va
        // frontend uni "qayta urinib ko'ring" deb tushunardi.
        var effectiveMax = maxScore ?? LessonGrade.DefaultMaxScore;

        if (effectiveMax > 0 && value > effectiveMax)
            throw Invalid("score", $"Baho maksimal balldan ({effectiveMax}) oshmasin.");

        return value;
    }

    private static decimal? ValidateMaxScore(decimal? maxScore)
    {
        if (maxScore is not { } value) return null;

        if (value <= 0)
            throw Invalid("maxScore", "Maksimal ball noldan katta bo'lishi kerak.");

        return value;
    }

    private static string? ValidateComment(string? comment)
    {
        var trimmed = comment?.Trim();

        if (string.IsNullOrEmpty(trimmed)) return null;

        if (trimmed.Length > LessonGrade.MaxCommentLength)
            throw Invalid("comment", $"Izoh {LessonGrade.MaxCommentLength} belgidan oshmasin.");

        return trimmed;
    }

    /// <summary>
    /// O'quvchi shu darsga TEGISHLIMI va ismi nima.
    ///
    /// ★ TEKSHIRUV MAJBURIY: aks holda xodim istalgan foydalanuvchi Id'sini
    /// yuborib, o'zi ko'rmaydigan guruhdagi o'quvchiga baho yozib qo'yardi.
    ///
    /// A'zolik HOLATI tekshirilmaydi (faol/pauza/to'xtatgan — farqi yo'q):
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
            // Bahosi bor, lekin a'zolik qatori o'chirilgan holat ham
            // tuzatilishi kerak (baho reytingda turibdi).
            belongs = await db.LessonGrades.AsNoTracking()
                .AnyAsync(g => g.SessionId == session.Id && g.StudentId == studentId, ct);
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
        decimal Score,
        decimal? MaxScore,
        string? Comment,
        long GradedById,
        string GradedByName,
        DateTimeOffset GradedAt);

    private sealed record MemberRow(long StudentId, string FullName);
}
