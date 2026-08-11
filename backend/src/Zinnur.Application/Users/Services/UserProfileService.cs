using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Groups;
using Zinnur.Application.Payments.Dtos;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.StudentNotes.Dtos;
using Zinnur.Application.StudentNotes.Services;
using Zinnur.Application.Users.Dtos;
using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Finance;
using Zinnur.Domain.Progress;

namespace Zinnur.Application.Users.Services;

/// <summary>
/// <see cref="IUserProfileService"/> ning amalga oshirilishi.
///
/// ════════════════════════════════════════════════════════════════════════
/// ★ N+1 GA QARSHI QOIDA: HAR BLOK — BITTA SO'ROV
///
/// Agregat 5 blokdan iborat va ularning har biri BITTA so'rovda o'qiladi.
/// Ro'yxatlar ichidagi qo'shimcha nomlar (guruh nomi, ustoz ismi, muallif
/// ismi) navigatsiya yoki korrelyatsiyalangan ichki so'rov bilan AYNI SQL
/// ichida olinadi — sikl ichida so'rov YO'Q. Eski tizimda profil oynasi
/// aynan shu sababdan sekin edi: har guruh uchun ustoz ismi alohida
/// so'roв bilan olinardi.
///
/// Umumiy so'rov soni (o'quv bo'limi uchun): ~13, hammasi CHEGARALANGAN.
/// Ro'yxatlar 50 ta bilan qirqiladi, oylar soni esa tabiiy chegarali
/// (kurs oylari × guruhlar).
/// ════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class UserProfileService(
    IApplicationDbContext db,
    IFinanceSettingsStore financeSettings,
    IScheduleTimeZoneProvider timeZone) : IUserProfileService
{
    /// <summary>
    /// Uzun ro'yxatlar chegarasi. To'liq ma'lumot mavjud maxsus
    /// endpointlarda (jurnal — <c>/payments/students/{id}/transactions</c>).
    /// </summary>
    private const int ListLimit = 50;

    public async Task<UserProfileDto> GetAsync(
        long userId, long actorId, CancellationToken ct = default)
    {
        // 🔴 RUXSAT — birinchi qadam. Undan keyin hech bir blok "kim
        //    so'rayapti" degan savolni qaytadan hal qilmaydi.
        var (student, audience) = await StudentAccess.AuthorizeAsync(db, actorId, userId, ct);

        var groups = await LoadGroupsAsync(userId, ct);

        var telegram = await LoadTelegramAsync(student, audience, ct);

        // 🔴 USTOZ/KURATOR — MOLIYA BLOKI UMUMAN YUKLANMAYDI.
        //    Maydon `null` bo'lib javobga tushadi, ya'ni ma'lumot serverdan
        //    CHIQMAYDI. Frontendda yashirish yetarli emasligi shundan:
        //    javobni ko'rish uchun brauzer konsoli yetarli bo'lardi.
        var finance = audience == StudentAudience.Staff
            ? null
            : await LoadFinanceAsync(student, audience, ct);

        var study = await LoadStudyAsync(userId, groups, ct);

        // 🔴 O'QUVCHI O'Z IZOHLARINI KO'RMAYDI.
        var notes = audience == StudentAudience.Self
            ? null
            : await StudentNoteQueries
                .Project(db, userId, actorId, canEditAll: audience == StudentAudience.Manage)
                .ToListAsync(ct);

        return new UserProfileDto(
            new UserDetailsDto(
                student.Id,
                student.FullName,
                student.Email,
                student.Phone,
                student.TelegramId,
                student.TelegramUsername,
                student.Role.ToString(),
                student.IsActive,
                student.CreatedAt,
                student.UpdatedAt),
            telegram,
            groups.ConvertAll(ToDto),
            finance,
            study,
            notes);
    }

    // ================================================================= TELEGRAM

    private async Task<ProfileTelegramDto> LoadTelegramAsync(
        StudentSubject student, StudentAudience audience, CancellationToken ct)
    {
        // Uzish izi — faqat XODIMGA. O'quvchiga "sizni Aziz Karimov uzgan"
        // deb ko'rsatish ichki ish tartibini oshkor qilardi va uning
        // profilida hech qanday amaliy foyda bermasdi.
        if (audience == StudentAudience.Self)
        {
            return new ProfileTelegramDto(
                student.TelegramId is not null,
                student.TelegramId,
                student.TelegramUsername,
                student.TelegramLinkedAt,
                UnlinkedAt: null,
                UnlinkedByName: null,
                UnlinkReason: null);
        }

        var lastUnlink = await db.TelegramUnlinkAudits.AsNoTracking()
            .Where(a => a.UserId == student.Id)
            .OrderByDescending(a => a.Id)
            .Select(a => new UnlinkRow(a.CreatedAt, a.Actor!.FullName, a.Reason))
            .FirstOrDefaultAsync(ct);

        return new ProfileTelegramDto(
            student.TelegramId is not null,
            student.TelegramId,
            student.TelegramUsername,
            student.TelegramLinkedAt,
            lastUnlink?.CreatedAt,
            lastUnlink?.ActorName,
            lastUnlink?.Reason);
    }

    // ================================================================= GURUHLAR

    /// <summary>
    /// A'zoliklar — HAMMA holat bilan (faol, pauzada, chiqarilgan,
    /// ko'chirilgan). Talab aynan shu: "qaysi guruhda faol o'qiyapti,
    /// qaysilaridan chiqarib yuborilgan".
    ///
    /// Ustoz ismi korrelyatsiyalangan ichki so'rov bilan olinadi:
    /// <c>Group.TeacherId</c> — navigatsiyasiz FK, shuning uchun
    /// <c>Include</c> ishlamaydi. Ichki so'rov AYNI SQL ichida bajariladi.
    /// </summary>
    private async Task<List<GroupRow>> LoadGroupsAsync(long studentId, CancellationToken ct) =>
        await db.GroupMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId)
            // FAOL a'zolik tepada, keyin qo'shilish vaqti bo'yicha yangisidan
            // eskisiga: drawer'da "hozir qayerda o'qiyapti" birinchi ko'rinadi.
            .OrderBy(m => m.Status)
            .ThenByDescending(m => m.JoinedAt)
            .Select(m => new GroupRow(
                m.GroupId,
                m.Group!.Name,
                db.Users.Where(u => u.Id == m.Group.TeacherId).Select(u => u.FullName).FirstOrDefault(),
                m.Status,
                m.JoinedAt,
                m.UpdatedAt,
                // SOYA ustun — entity'da property yo'q
                // (sabab `GroupMemberFields` izohida).
                EF.Property<DateOnly?>(m, GroupMemberFields.PausedUntil),
                m.Group.IsActive))
            .ToListAsync(ct);

    private static ProfileGroupDto ToDto(GroupRow row) => new(
        row.GroupId,
        row.GroupName,
        row.TeacherName,
        row.Status,
        row.JoinedAt,
        // ⚠️ TAXMINIY "chiqish vaqti" — batafsil `ProfileGroupDto.LeftAt` izohida.
        row.Status is MemberStatus.Stopped or MemberStatus.Moved ? row.UpdatedAt : null,
        // ⚠️ Modelda saqlanmaydi — sabab `ProfileGroupDto.MovedToGroupId` izohida.
        MovedToGroupId: null,
        MovedToGroupName: null,
        row.Status == MemberStatus.Paused ? row.PausedUntil : null);

    // ================================================================= MOLIYA

    private async Task<ProfileFinanceDto> LoadFinanceAsync(
        StudentSubject student, StudentAudience audience, CancellationToken ct)
    {
        var balance = await db.StudentAccounts.AsNoTracking()
            .Where(a => a.StudentId == student.Id)
            .Select(a => (decimal?)a.Balance)
            .FirstOrDefaultAsync(ct) ?? 0m;

        var periods = await db.Payments.AsNoTracking()
            .Where(p => p.StudentId == student.Id)
            .OrderByDescending(p => p.Period)
            .ThenBy(p => p.Group!.Name)
            .Select(p => new PeriodRow(
                p.Period, p.GroupId, p.Group!.Name, p.Amount, p.PaidAmount, p.Status))
            .ToListAsync(ct);

        var sessionCounts = await LoadSessionCountsAsync(periods, ct);

        // ★ FORMULA MOLIYA MODULI BILAN AYNI (`PaymentBlockService.DebtOfAsync`
        //   va `GetStudentAccountAsync`): qarz — faqat OCHIQ oylarning qolgan
        //   qismi. Qisman to'langan oy TO'LIQ qarz deb sanalmaydi, kechirilgan
        //   oy esa umuman qarz emas. Ikkinchi formula yozilsa profilda 540 000,
        //   moliya panelida 0 chiqishi mumkin edi.
        var totalDue = periods
            .Where(p => p.Status is PaymentStatus.Due or PaymentStatus.Partial)
            .Sum(p => Math.Max(0m, p.Amount - p.PaidAmount));

        var totalPaid = periods.Sum(p => p.PaidAmount);

        var settings = await financeSettings.GetAsync(ct);

        // AMALDAGI qamrov: qoida Domain'da (`PaymentBlockPolicy`) — bu yerda
        // shartlar qayta yozilmaydi. `requested = configured`: "shu o'quvchiga
        // hozir eng keng qanday blok tushadi" degan savolga javob.
        var blockScope = PaymentBlockPolicy.IsBlocked(
            totalDue,
            settings.BlockThreshold,
            settings.BlockScope,
            settings.BlockScope,
            student.PaymentExempt,
            settings.Enforce)
            ? settings.BlockScope
            : PaymentBlockScope.None;

        // 🔴 O'QUVCHIGA JURNAL YUBORILMAYDI (talab). U o'z to'lovlarini
        //    moliya bo'limining alohida endpointida ko'radi.
        var transactions = audience == StudentAudience.Self
            ? null
            : await LoadTransactionsAsync(student.Id, ct);

        return new ProfileFinanceDto(
            balance,
            totalPaid,
            totalDue,
            blockScope,
            periods.ConvertAll(p => new ProfilePeriodDto(
                p.Period,
                p.GroupId,
                p.GroupName,
                p.Amount,
                p.PaidAmount,
                Math.Max(0m, p.Amount - p.PaidAmount),
                p.Status,
                sessionCounts.GetValueOrDefault((p.GroupId, p.Period)))),
            transactions?.Take(ListLimit).ToList(),
            HasMoreTransactions: transactions?.Count > ListLimit);
    }

    /// <summary>
    /// Oxirgi 50 ta jurnal yozuvi + "yana bormi" ni aniqlash uchun BITTA
    /// ortiq (<c>51</c>). Alohida <c>COUNT</c> so'rovi ATAYLAB yo'q: drawer'ga
    /// umumiy son kerak emas, faqat "Hammasini ko'rish" tugmasini
    /// ko'rsatish/ko'rsatmaslik kerak.
    ///
    /// Proyeksiya <c>/payments/students/{id}/transactions</c> bilan AYNI
    /// maydonlarni beradi — frontend bitta turdan foydalanadi.
    /// </summary>
    private async Task<List<PaymentTransactionDto>> LoadTransactionsAsync(
        long studentId, CancellationToken ct) =>
        await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.StudentId == studentId)
            .OrderByDescending(t => t.Id)
            .Take(ListLimit + 1)
            .Select(t => new PaymentTransactionDto(
                t.Id,
                t.StudentId,
                t.GroupId,
                t.Group == null ? null : t.Group.Name,
                t.Kind,
                t.Amount,
                t.ReceiptNo,
                t.Method,
                t.Note,
                t.ActorId,
                db.Users.Where(u => u.Id == t.ActorId).Select(u => u.FullName).FirstOrDefault(),
                t.CreatedAt))
            .ToListAsync(ct);

    /// <summary>
    /// Har (guruh × oy) uchun O'TKAZILGAN darslar soni — BITTA so'rovda.
    ///
    /// ★ NIMA UCHUN GURUHLASH XOTIRADA, SQL'da EMAS: oy chegarasi MAHALLIY
    /// vaqt bo'yicha aniqlanadi (Asia/Tashkent), UTC bo'yicha emas.
    /// 1-avgust 00:30 (mahalliy) — UTC'da 31-iyul 19:30, ya'ni SQL'da
    /// <c>date_trunc('month', ...)</c> bilan guruhlash darsni AVVALGI oyga
    /// tushirib qo'yardi va "iyulda 9 dars" degan noto'g'ri son chiqardi.
    /// Zonani hisobga oladigan konvertatsiyani esa EF SQL'ga tarjima
    /// qilmaydi. Shuning uchun oraliqdagi darslarning faqat IKKI ustuni
    /// o'qiladi va guruhlash .NET tomonda bajariladi.
    ///
    /// Hajm chegarali: o'quvchining guruhlari × oylik davrlari
    /// (amalda ~8 oy × 2 guruh × 8-12 dars).
    /// </summary>
    private async Task<Dictionary<(long GroupId, string Period), int>> LoadSessionCountsAsync(
        List<PeriodRow> periods, CancellationToken ct)
    {
        var result = new Dictionary<(long, string), int>();

        if (periods.Count == 0) return result;

        var groupIds = periods.Select(p => p.GroupId).Distinct().ToList();
        var months = periods.Select(p => BillingPeriod.Parse(p.Period)).ToList();

        var zone = timeZone.TimeZone;
        var fromUtc = LocalWallClock.StartOfDayUtc(months.Min().FirstDay(), zone);
        var toUtc = LocalWallClock.StartOfDayUtc(months.Max().AddMonths(1).FirstDay(), zone);

        var sessions = await db.LiveSessions.AsNoTracking()
            .Where(s => groupIds.Contains(s.GroupId)
                     && s.Status == SessionStatus.Ended
                     && s.ScheduledStart >= fromUtc
                     && s.ScheduledStart < toUtc)
            .Select(s => new SessionRow(s.GroupId, s.ScheduledStart))
            .ToListAsync(ct);

        foreach (var session in sessions)
        {
            var localDate = LocalWallClock.LocalDate(session.ScheduledStart, zone);
            var key = (session.GroupId, BillingPeriod.FromDate(localDate).ToString());

            result[key] = result.GetValueOrDefault(key) + 1;
        }

        return result;
    }

    // ================================================================= O'QUV NATIJALARI

    private async Task<ProfileStudyDto> LoadStudyAsync(
        long studentId, List<GroupRow> groups, CancellationToken ct)
    {
        var submissions = await db.Submissions.AsNoTracking()
            .Where(s => s.StudentId == studentId)
            .OrderByDescending(s => s.SubmittedAt)
            .ThenByDescending(s => s.Id)
            .Take(ListLimit + 1)
            .Select(s => new ProfileAssignmentDto(
                s.Id,
                s.AssignmentId,
                s.Assignment!.Title,
                // Vazifa YOKI guruhga, YOKI kurs darsiga biriktiriladi
                // (`Assignment.Validate`) — shuning uchun ikkisidan biri
                // doim `null`. EF ikkalasini `LEFT JOIN` qiladi.
                s.Assignment.Group == null ? null : s.Assignment.Group.Name,
                s.Assignment.ModuleLesson == null ? null : s.Assignment.ModuleLesson.Name,
                s.Score,
                s.Assignment.MaxScore,
                s.Status,
                s.SubmittedAt,
                s.IsLate,
                // 🔴 FAQAT SON: `objectKey` javobga CHIQMAYDI (ichki ombor
                //    kaliti). Fayl himoyalangan endpoint orqali ochiladi.
                s.Files.Count))
            .ToListAsync(ct);

        var attempts = await db.TestAttempts.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Status == AttemptStatus.Submitted)
            .OrderByDescending(a => a.SubmittedAt)
            .ThenByDescending(a => a.Id)
            .Take(ListLimit + 1)
            .Select(a => new AttemptRow(
                a.Id,
                a.TestId,
                a.Test!.Title,
                a.Test.Kind,
                a.Score,
                a.MaxScore,
                a.ClosedByTimeout,
                a.SubmittedAt))
            .ToListAsync(ct);

        return new ProfileStudyDto(
            submissions.Take(ListLimit).ToList(),
            HasMoreAssignments: submissions.Count > ListLimit,
            attempts.Take(ListLimit).Select(ToDto).ToList(),
            HasMoreTests: attempts.Count > ListLimit,
            await LoadAttendanceAsync(studentId, groups, ct));
    }

    /// <summary>
    /// Foiz XOTIRADA hisoblanadi: <c>Math.Round(decimal, int)</c> ni
    /// Postgres'ga tarjima qilish provayder versiyasiga bog'liq, natija esa
    /// bir xil — qatorlar soni 50 ta bilan chegaralangan.
    /// </summary>
    private static ProfileTestDto ToDto(AttemptRow row) => new(
        row.AttemptId,
        row.TestId,
        row.Title,
        row.Kind,
        row.Score,
        row.MaxScore,
        row.Score is { } score && row.MaxScore is { } max && max > 0
            ? Math.Round(score / max * 100m, 1)
            : null,
        row.ClosedByTimeout,
        row.FinishedAt);

    /// <summary>
    /// Davomat doirasi.
    ///
    /// Qamrov <c>AttendanceSummaryService</c> bilan AYNI: FAOL a'zolikdagi
    /// FAOL guruhlarning YAKUNLANGAN darslari. Aks holda o'quvchi o'z
    /// ilovasida bir foizni, xodim profil drawer'ida boshqasini ko'rardi.
    ///
    /// Davomat yozuvi YO'QLIGI "kelmagan" degani (yozuv faqat xonaga kirgan
    /// o'quvchi uchun yaratiladi) — shuning uchun <c>LEFT JOIN</c> naqshi
    /// va <c>null</c> ni "qoldirgan" deb sanash.
    /// </summary>
    private async Task<ProfileAttendanceDto> LoadAttendanceAsync(
        long studentId, List<GroupRow> groups, CancellationToken ct)
    {
        var groupIds = groups
            .Where(g => g.Status == MemberStatus.Active && g.GroupIsActive)
            .Select(g => g.GroupId)
            .ToList();

        if (groupIds.Count == 0)
            return Map(AttendanceTally.Empty);

        var statuses = await db.LiveSessions.AsNoTracking()
            .Where(s => groupIds.Contains(s.GroupId) && s.Status == SessionStatus.Ended)
            .Select(s => db.Attendances
                .Where(a => a.SessionId == s.Id && a.StudentId == studentId)
                .Select(a => (AttendanceStatus?)a.Status)
                .FirstOrDefault())
            .ToListAsync(ct);

        var tally = AttendanceTally.Empty;

        foreach (var status in statuses)
            tally = tally.Add(status is { } value && AttendanceMath.IsAttended(value));

        return Map(tally);
    }

    private static ProfileAttendanceDto Map(AttendanceTally tally) =>
        new(tally.Total, tally.Attended, tally.Missed, tally.Percent);

    // ================================================================= ichki qatorlar

    private sealed record GroupRow(
        long GroupId,
        string GroupName,
        string? TeacherName,
        MemberStatus Status,
        DateTimeOffset JoinedAt,
        DateTimeOffset? UpdatedAt,
        DateOnly? PausedUntil,
        bool GroupIsActive);

    private sealed record PeriodRow(
        string Period,
        long GroupId,
        string GroupName,
        decimal Amount,
        decimal PaidAmount,
        PaymentStatus Status);

    private sealed record SessionRow(long GroupId, DateTimeOffset ScheduledStart);

    private sealed record AttemptRow(
        long AttemptId,
        long TestId,
        string Title,
        TestKind Kind,
        decimal? Score,
        decimal? MaxScore,
        bool ClosedByTimeout,
        DateTimeOffset? FinishedAt);

    private sealed record UnlinkRow(DateTimeOffset CreatedAt, string ActorName, string? Reason);
}
