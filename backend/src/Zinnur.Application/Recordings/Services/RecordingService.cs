using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.LiveSessions.Dtos;
using Zinnur.Application.LiveSessions.Services;
using Zinnur.Application.Payments.Services;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// <see cref="IRecordingService"/> ning amalga oshirilishi.
///
/// ── RUXSAT: BITTA MANBA ─────────────────────────────────────────────────
///
/// Har use-case <see cref="ILiveSessionService"/> ni chaqiradi va u
/// darsga kirish huquqini ALLAQACHON tekshiradi: a'zolik, faol guruh,
/// faol profil, host'lik. Bu yerda ikkinchi nusxa YOZILMAYDI — vaqt o'tib
/// ular ajralib ketardi va bir yo'lda zaifroq tekshiruv qolardi (aynan
/// shu eski tizimning `LiveSessionService.IsActive` teshigi edi).
///
/// ⚠️ Bu ikkita so'rov degani (dars + huquq, keyin xona nomi uchun
/// entity). Bu ONGLI narx: yozuv oqimi vaqt-tanqis emas (tugma bosiladi,
/// sahifa ochiladi), ruxsat qoidasi esa ikkilanmasligi kerak.
///
/// ── HOLAT O'ZGARISHLARI DOMAIN'DA ───────────────────────────────────────
///
/// Bu servis <c>Status</c> ga TO'G'RIDAN-TO'G'RI hech qachon tegmaydi —
/// faqat <see cref="SessionRecording"/> metodlarini chaqiradi. Shu tufayli
/// "tugallangan yozuvni orqaga qaytarish" kabi xatolar bu yerda YUZAGA
/// KELA OLMAYDI.
/// </summary>
public sealed class RecordingService(
    IApplicationDbContext db,
    ILiveSessionService liveSessions,
    ILiveKitEgress egress,
    IRecordingStorage storage,
    IPaymentBlockService paymentBlock,
    TimeProvider clock,
    ILogger<RecordingService> logger) : IRecordingService
{
    /// <summary>
    /// Ro'yxat so'rovining eng uzun oralig'i (kun).
    ///
    /// <c>ILiveSessionService.GetCalendarAsync</c> dagi 92 kunlik chegara
    /// bilan AYNI — u baribir shu qiymatda rad etadi va bu yerda undan
    /// kattaroq oraliqni qabul qilish faqat chalg'ituvchi xato xabari
    /// berardi.
    /// </summary>
    private const int MaxRangeDays = 92;

    /// <inheritdoc />
    public async Task<RecordingDto> StartAsync(
        long sessionId, long actorId, CancellationToken ct = default)
    {
        var (session, view) = await LoadAsync(sessionId, actorId, ct).ConfigureAwait(false);

        if (!view.IsHost)
            throw new ForbiddenException("Yozuvni faqat dars hosti boshlay oladi.");

        if (session.Status != SessionStatus.Live)
        {
            throw new ConflictException(
                "Yozuvni faqat JONLI dars uchun boshlash mumkin. Avval darsni boshlang.");
        }

        // Ombor VA LiveKit — ikkalasi ham kerak (sabab: `ILiveKitEgress`).
        // 503 ataylab: bu bizning bug'imiz emas, sozlanmagan bog'liqlik.
        if (!egress.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Yozuv xizmati sozlanmagan (`LiveKit:*` yoki `Storage:*`). "
                + "Dars odatdagidek davom etadi.");
        }

        var existing = await db.SessionRecordings
            .AsTracking()
            .Where(r => r.SessionId == sessionId
                     && r.Status != RecordingStatus.Completed
                     && r.Status != RecordingStatus.Failed)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        // IDEMPOTENT: tugma ikki marta bosilsa (yoki ikki qurilmadan
        // bosilsa) ikkinchi egress BOSHLANMAYDI — u alohida fayl yozib,
        // ikkalasi ham tarmoq va disk yeb qo'yardi.
        if (existing is not null)
            return Map(existing);

        var now = clock.GetUtcNow();

        var recording = new SessionRecording
        {
            SessionId = sessionId,
            RequestedBy = actorId,
            ObjectKey = storage.BuildObjectKey(sessionId),
        };

        db.SessionRecordings.Add(recording);

        // 🔴 QATOR EGRESS'GA MUROJAATDAN OLDIN SAQLANADI.
        //
        // Sabab: shu lahzada jarayon qulasa yoki Egress javobni yo'qotsa,
        // "boshlangan, lekin hech qayerda yozilmagan" yozuv qolib
        // ketardi — ya'ni fayl omborga tushardi-yu, uni hech kim topa
        // olmasdi. Endi eng yomon holatda qator `Requested` bo'lib qoladi
        // va watchdog uni ko'radi.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await RecordingStarter
            .TryAsync(egress, recording, session.RoomName, now, logger, ct)
            .ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Map(recording);
    }

    /// <inheritdoc />
    public async Task<RecordingDto> StopAsync(
        long sessionId, long actorId, CancellationToken ct = default)
    {
        var (_, view) = await LoadAsync(sessionId, actorId, ct).ConfigureAwait(false);

        if (!view.IsHost)
            throw new ForbiddenException("Yozuvni faqat dars hosti to'xtata oladi.");

        var recording = await db.SessionRecordings
            .AsTracking()
            .Where(r => r.SessionId == sessionId
                     && r.Status != RecordingStatus.Completed
                     && r.Status != RecordingStatus.Failed)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new ConflictException("Bu darsda faol yozuv yo'q.");

        var now = clock.GetUtcNow();

        if (string.IsNullOrWhiteSpace(recording.EgressId))
        {
            // Egress umuman boshlanmagan (birinchi urinish yiqilgan).
            // To'xtatadigan narsa yo'q — yozuvni YAKUNIY xato deb yopamiz,
            // aks holda watchdog ustoz ataylab bekor qilgan yozuvni qayta
            // urib turaverardi.
            recording.MarkFailed("Yozuv boshlanmasdan bekor qilindi.", now);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Map(recording);
        }

        // ⚠️ To'xtatish DARHOL fayl degani emas: yakuniy holat webhook
        // bilan keladi. Shuning uchun bu yerda `MarkCompleted` YO'Q —
        // u fayl hali yozilmagan yozuvni "tayyor" deb ko'rsatardi.
        var accepted = await egress
            .StopRecordingAsync(recording.EgressId, ct)
            .ConfigureAwait(false);

        recording.MarkStopRequested(now);

        RecordingLog.StopRequested(logger, recording.Id, recording.EgressId, accepted);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Map(recording);
    }

    /// <inheritdoc />
    public async Task<RecordingLiveStatusDto> GetLiveStatusAsync(
        long sessionId, long actorId, CancellationToken ct = default)
    {
        // Ruxsat — darsning O'ZI orqali (istisno bilan rad etadi). Rol
        // TEKSHIRILMAYDI: indikatorni aynan o'quvchi ko'rishi kerak
        // (sabab: `IRecordingService` izohi, 1-dalil).
        await liveSessions.GetAsync(sessionId, actorId, ct).ConfigureAwait(false);

        // ★ AYNI FILTR `StartAsync` VA `StopAsync` DAGIDEK: "yakunlanmagan
        //   qator". Uchta joyda uchta xil ta'rif bo'lsa, tugma yozuvni
        //   to'xtatib, indikator esa yonib turgan holat kelib chiqardi.
        //
        // So'rov `IX_SessionRecordings_SessionId_Id` indeksiga tushadi va
        // faqat IKKI ustunni o'qiydi — qator umuman yuklanmaydi.
        var row = await db.SessionRecordings
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId
                     && r.Status != RecordingStatus.Completed
                     && r.Status != RecordingStatus.Failed)
            .OrderByDescending(r => r.Id)
            .Select(r => new { r.StartedAt })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return row is null
            ? new RecordingLiveStatusDto(false, null)
            : new RecordingLiveStatusDto(true, row.StartedAt);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordingDto>> ListForSessionAsync(
        long sessionId, long actorId, CancellationToken ct = default)
    {
        // Ruxsat — darsning O'ZI orqali (istisno bilan rad etadi).
        await liveSessions.GetAsync(sessionId, actorId, ct).ConfigureAwait(false);

        var isStaff = await IsStaffAsync(actorId, ct).ConfigureAwait(false);

        var rows = await db.SessionRecordings
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId)
            .Where(r => isStaff || r.Status == RecordingStatus.Completed)
            .OrderByDescending(r => r.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.ConvertAll(r => Map(r, isStaff));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordingListItemDto>> ListAsync(
        long actorId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (fromDate > toDate)
            throw Invalid("fromDate", "Boshlanish sanasi tugash sanasidan keyin bo'lishi mumkin emas.");

        if (toDate.DayNumber - fromDate.DayNumber + 1 > MaxRangeDays)
            throw Invalid("toDate", $"Oraliq {MaxRangeDays} kundan oshmasin.");

        // ★ QAMROV KALENDARDAN: u foydalanuvchi ko'ra oladigan darslarni
        //   ROL bo'yicha allaqachon filtrlaydi va testlar bilan qoplangan.
        //   Bu yerda ikkinchi (va albatta bir kun ajralib ketadigan)
        //   ruxsat so'rovi yozilmaydi.
        var calendar = await liveSessions
            .GetCalendarAsync(actorId, fromDate, toDate, ct)
            .ConfigureAwait(false);

        if (calendar.Count == 0)
            return [];

        var sessions = calendar.ToDictionary(s => s.Id);
        var sessionIds = sessions.Keys.ToArray();

        var isStaff = await IsStaffAsync(actorId, ct).ConfigureAwait(false);

        var rows = await db.SessionRecordings
            .AsNoTracking()
            .Where(r => sessionIds.Contains(r.SessionId))

            // ★ O'QUVCHIGA FAQAT TAYYOR YOZUV KO'RINADI.
            //
            //   Unga "urinish yiqildi" degan qator hech narsa bermaydi:
            //   u baribir hech narsa qila olmaydi, lekin ro'yxat "buzuq"
            //   ko'rinardi. Xodimga esa AKSINCHA — aynan o'sha qatorlar
            //   "nega bu darsning yozuvi yo'q?" degan savolga javob.
            .Where(r => isStaff || r.Status == RecordingStatus.Completed)
            .OrderByDescending(r => r.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = new List<RecordingListItemDto>(rows.Count);

        foreach (var row in rows)
        {
            var session = sessions[row.SessionId];

            items.Add(new RecordingListItemDto(
                Map(row, isStaff),
                session.GroupId,
                session.GroupName,
                session.Title,
                session.LocalDate,
                session.ScheduledStart));
        }

        return items;
    }

    /// <inheritdoc />
    public async Task<RecordingLinkDto> CreateViewLinkAsync(
        long recordingId, long actorId, CancellationToken ct = default)
    {
        var recording = await db.SessionRecordings
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recordingId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(SessionRecording), recordingId);

        // Ruxsat — darsning O'ZI orqali (a'zo o'quvchi, ustoz/kurator,
        // o'quv bo'limi/admin). Rad etilsa istisno ko'tariladi.
        await liveSessions.GetAsync(recording.SessionId, actorId, ct).ConfigureAwait(false);

        if (!recording.IsPlayable)
        {
            throw new ConflictException(recording.Status == RecordingStatus.Failed
                ? "Bu darsning yozuvi chiqmadi."
                : "Yozuv hali tayyor emas.");
        }

        // ═══════════════════════════════════════════════════════════════
        // 🔴 TO'LOV DARVOZASI — AYNAN SHU YERDA, HAVOLA BERILISHIDAN OLDIN
        //
        // Havola chiqarilgandan keyin serverning "yo'q" deyishi MUMKIN
        // EMAS: brauzer to'g'ridan-to'g'ri omborga boradi. Bu
        // `CreateJoinTokenAsync` dagi AYNI mulohaza (LiveKit tokeni ham
        // shunday).
        //
        // ★ QAMROV `Video` — YANGISI TO'QILMADI. Dars yozuvi mohiyatan
        //   VIDEO DARS: o'quvchi uni istagan paytda, jonli darsdan
        //   TASHQARIDA ko'radi. Sozlamaning standart qiymati ham `Video`
        //   (`finance.block_scope`), ya'ni qarzdor uchun eng avval aynan
        //   shu yopiladi. `Live` qo'yilsa qoida teskari bo'lardi: jonli
        //   darsdan chetlatilgan o'quvchi uning YOZUVINI bemalol
        //   ko'raverardi.
        //
        // ★ FAQAT O'QUVCHIGA: ustoz, kurator va o'quv bo'limi hech qachon
        //   bloklanmaydi (`CreateJoinTokenAsync` bilan bir xil qoida).
        // ═══════════════════════════════════════════════════════════════
        if (await RoleOfAsync(actorId, ct).ConfigureAwait(false) == UserRole.Student)
            await paymentBlock.EnsureAllowedAsync(actorId, PaymentBlockScope.Video, ct).ConfigureAwait(false);

        if (!storage.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori sozlanmagan (`Storage:*`). Yozuvni ochib bo'lmadi.");
        }

        var ttl = IRecordingStorage.DefaultLinkTtl;
        var url = storage.CreateViewLink(recording.ObjectKey, ttl);

        // ⚠️ Havola BAZAGA YOZILMAYDI va keshlanmaydi — u har so'rovda
        //    yangidan imzolanadi (izoh: `IRecordingStorage`).
        return new RecordingLinkDto(url.ToString(), clock.GetUtcNow().Add(ttl));
    }

    // ================================================================= ichki

    /// <summary>
    /// Darsni yuklaydi VA huquqni tekshiradi.
    ///
    /// Tekshiruv <see cref="ILiveSessionService.GetAsync"/> da (u istisno
    /// ko'taradi); entity esa <c>RoomName</c> va aniq <c>Status</c> uchun
    /// kerak — DTO'da xona nomi ATAYLAB yo'q (u LiveKit ichki nomi va
    /// klientga berilmaydi).
    /// </summary>
    private async Task<(LiveSession Session, LiveSessionDto View)> LoadAsync(
        long sessionId, long actorId, CancellationToken ct)
    {
        var view = await liveSessions.GetAsync(sessionId, actorId, ct).ConfigureAwait(false);

        var session = await db.LiveSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(LiveSession), sessionId);

        return (session, view);
    }

    private async Task<UserRole> RoleOfAsync(long actorId, CancellationToken ct) =>
        await db.Users
            .AsNoTracking()
            .Where(u => u.Id == actorId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    /// <summary>
    /// Xodimmi (o'quvchi emasmi). Yozuvning XATO SABABI faqat xodimga
    /// ko'rsatiladi: u ichki tafsilot (Egress xabari) va o'quvchiga hech
    /// narsa bermaydi.
    /// </summary>
    private async Task<bool> IsStaffAsync(long actorId, CancellationToken ct) =>
        await RoleOfAsync(actorId, ct).ConfigureAwait(false) != UserRole.Student;

    private static RecordingDto Map(SessionRecording r, bool includeError = true) => new(
        r.Id,
        r.SessionId,
        r.Status.ToString(),
        r.IsPlayable,
        r.StartedAt,
        r.EndedAt,
        r.DurationSeconds,
        r.SizeBytes,
        r.Attempts,
        includeError ? r.Error : null,
        r.CreatedAt);

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
