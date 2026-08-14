using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.LiveSessions.Dtos;
using Zinnur.Application.LiveSessions.Services;
using Zinnur.Application.Payments.Services;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
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
    ISettingsResolver settings,
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
            return await MapWithReviewAsync(existing, ct).ConfigureAwait(false);

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

        return await MapWithReviewAsync(recording, ct).ConfigureAwait(false);
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

            return await MapWithReviewAsync(recording, ct).ConfigureAwait(false);
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

        return await MapWithReviewAsync(recording, ct).ConfigureAwait(false);
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

        // 🔴 R5: bo'lim global sozlama bilan yopilgan bo'lsa, o'quvchi
        //    uchun ro'yxat BO'SH. Xodimga hech qanday cheklov yo'q.
        if (!isStaff && !await SectionOpenAsync(ct).ConfigureAwait(false))
            return [];

        var rows = await ApplyVisibility(
                db.SessionRecordings.AsNoTracking().Where(r => r.SessionId == sessionId),
                isStaff)
            .OrderByDescending(r => r.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var verdicts = await LoadVerdictsAsync(rows, isStaff, ct).ConfigureAwait(false);

        return rows.ConvertAll(r => Map(r, isStaff, verdicts));
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

        // 🔴 R5: global kalit o'chiq bo'lsa o'quvchi uchun bo'lim YO'Q.
        if (!isStaff && !await SectionOpenAsync(ct).ConfigureAwait(false))
            return [];

        var rows = await ApplyVisibility(
                db.SessionRecordings.AsNoTracking().Where(r => sessionIds.Contains(r.SessionId)),
                isStaff)
            .OrderByDescending(r => r.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var verdicts = await LoadVerdictsAsync(rows, isStaff, ct).ConfigureAwait(false);

        var items = new List<RecordingListItemDto>(rows.Count);

        foreach (var row in rows)
        {
            var session = sessions[row.SessionId];

            items.Add(new RecordingListItemDto(
                Map(row, isStaff, verdicts),
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
        {
            // ═══════════════════════════════════════════════════════════
            // 🔴 R5 — KO'RINISH DARVOZASI: TO'LOVDAN OLDIN, AYNI JOYDA
            //
            // ★ NIMA UCHUN AYNAN SHU YERDA VA NIMA UCHUN RO'YXAT YETMAYDI:
            //   ro'yxat yashirilgan yozuvni bermaydi, LEKIN o'quvchi
            //   yozuv Id'sini ALLAQACHON bilishi mumkin — kecha ochiq
            //   sahifada turgan, xatcho'pga solingan yoki oddiygina
            //   brauzer tarixida qolgan. Faqat ro'yxatni filtrlash
            //   "ko'rinmasin" ni emas, "izlash biroz qiyinroq bo'lsin" ni
            //   anglatardi. Havola berilgandan keyin esa serverning "yo'q"
            //   deyishiga imkon YO'Q: brauzer to'g'ridan-to'g'ri omborga
            //   boradi (pastdagi to'lov darvozasi bilan AYNI mulohaza).
            //
            // ★ TARTIB ATAYLAB: KO'RINISH — TO'LOVDAN OLDIN. Yopilgan
            //   yozuvni so'ragan qarzdor o'quvchiga "qarzingiz bor" deyish
            //   ikki marta noto'g'ri bo'lardi — qarzini to'lasa ham yozuv
            //   ochilmasdi.
            //
            // 🔴 XABAR MATNI TO'LOV XABARIDAN ATAYLAB BOShQAcha: ular ikki
            //    XIL nosozlik va pleyer ikkalasini bir xil ko'rsatsa,
            //    o'quvchi yopilgan darsni "qarz" deb tushunib, buxgalteriyaga
            //    borardi. Matn serverdan keladi va `toUserMessage` uni
            //    o'zgarishsiz ko'rsatadi — ya'ni farq FRONTENDDA emas, SHU
            //    YERDA tug'iladi.
            // ═══════════════════════════════════════════════════════════
            if (!await IsVisibleToStudentAsync(recording, ct).ConfigureAwait(false))
            {
                throw new ForbiddenException(
                    "Bu dars yozuvi hozircha yopilgan. Savol bo'lsa o'quv bo'limiga murojaat qiling.");
            }

            await paymentBlock.EnsureAllowedAsync(actorId, PaymentBlockScope.Video, ct).ConfigureAwait(false);
        }

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

    /// <inheritdoc />
    public async Task<RecordingDto> SetVisibilityAsync(
        long recordingId, bool visible, long actorId, CancellationToken ct = default)
    {
        var recording = await db.SessionRecordings
            .AsTracking()
            .FirstOrDefaultAsync(r => r.Id == recordingId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(SessionRecording), recordingId);

        // Ruxsat — darsning O'ZI orqali (a'zo o'quvchi ham o'tadi, shuning
        // uchun pastda rol darvozasi ALOHIDA turadi).
        await liveSessions.GetAsync(recording.SessionId, actorId, ct).ConfigureAwait(false);

        var role = await RoleOfAsync(actorId, ct).ConfigureAwait(false);

        // 🔴 O'quvchi o'z darsini KO'RA oladi, lekin ko'rinishni BOSHQARA
        //    olmaydi. Controller atributi ham shuni aytadi — bu ikkinchi
        //    qatlam (hub yoki kelajakdagi boshqa chaqiruvchi atributdan
        //    o'tmaydi).
        if (role == UserRole.Student)
            throw new ForbiddenException("Yozuv ko'rinishini faqat xodimlar boshqaradi.");

        var now = clock.GetUtcNow();

        if (visible)
        {
            await EnsureCanRevealAsync(recording, role, ct).ConfigureAwait(false);
            recording.ShowToStudents(actorId, now);
        }
        else
        {
            // ★ YASHIRISHDA HECH QANDAY USTUNLIK TEKSHIRUVI YO'Q — bu
            //   "eng qattig'i yutadi" qoidasining to'g'ridan-to'g'ri
            //   natijasi: HAR IKKALA tomon ham yopa oladi.
            recording.HideFromStudents(actorId, now);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return await MapWithReviewAsync(recording, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RecordingSectionDto> GetSectionAsync(
        long actorId, CancellationToken ct = default)
    {
        // Xodim uchun bo'lim HECH QACHON yopilmaydi: global va guruh
        // kalitlari o'quvchiga qaratilgan, arxiv esa xodimga har doim
        // kerak ("nega bu darsning yozuvi yo'q?").
        if (await IsStaffAsync(actorId, ct).ConfigureAwait(false))
            return new RecordingSectionDto(true);

        if (!await SectionOpenAsync(ct).ConfigureAwait(false))
            return new RecordingSectionDto(false);

        // ★ GLOBAL KALIT YOQIQ BO'LSA HAM O'QUVCHINING HAMMA GURUHI
        //   YOPIQ BO'LISHI MUMKIN — bunda ham kartochka ko'rsatilmaydi.
        //   So'rov `IX_GroupMembers_*` bo'yicha faol a'zoliklarni oladi va
        //   qatorlarni YUKLAMAYDI (`AnyAsync`).
        var anyOpenGroup = await db.GroupMembers
            .AsNoTracking()
            .AnyAsync(
                m => m.StudentId == actorId
                  && m.Status == MemberStatus.Active
                  && m.Group!.RecordingsVisibleToStudents,
                ct)
            .ConfigureAwait(false);

        return new RecordingSectionDto(anyOpenGroup);
    }

    // ================================================================= ichki

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// 🔴 R5 — USTUNLIK QOIDASI: "OCHISH" ENG QATTIQ AMAL
    /// ════════════════════════════════════════════════════════════════
    ///
    /// Talab ikkala tomonni ham boshqaruvchi qilib belgilaydi ("o'quv
    /// bo'limi VA teacher tarafidan"), lekin ular BIR-BIRIGA ZID qaror
    /// qilganda nima bo'lishini AYTMAYDI. Tanlangan qoida:
    ///
    ///   • YASHIRISH — ikkala tomon ham, har doim, shartsiz;
    ///   • OCHISH — o'quv bo'limi yopganini FAQAT o'quv bo'limi ochadi.
    ///
    /// ★ NIMA UCHUN "OXIRGI YOZGAN YUTADI" EMAS: o'quv bo'limi yozuvni
    /// odatda AYNI R29 sababi bilan yopadi ("darsda muammo bor, bu
    /// yozuv tarqalmasin"). Oxirgi yozgan yutsa, ustoz bir bosish bilan
    /// uni qaytarib ochardi — ya'ni sifat nazoratining yagona amaliy
    /// vositasi kuchsiz maslahatga aylanardi.
    ///
    /// ★ NIMA UCHUN AKSINCHA EMAS (ustoz yopganini faqat ustoz ochsin):
    /// o'quv bo'limi — eskalatsiya nuqtasi. Ustoz ta'tilda bo'lsa,
    /// ishdan ketgan bo'lsa yoki oddiygina xato bosgan bo'lsa, tuzata
    /// oladigan kimdir QOLISHI shart.
    ///
    /// ⚠️ NARXI: ustoz o'zi yopgan yozuvni ochishi mumkin, lekin
    /// hamkasbi yopganini emas — bu ham ONGLI (kim yopgan bo'lsa, sababni
    /// ham o'sha biladi).
    ///
    /// ★ Rol so'rovi FAQAT SHU YO'LDA bajariladi (yozuvni ochish — nodir
    /// amal), ya'ni o'qish yo'llariga hech qanday narx qo'shmaydi.
    /// </summary>
    private async Task EnsureCanRevealAsync(
        SessionRecording recording, UserRole role, CancellationToken ct)
    {
        if (role is UserRole.Academic or UserRole.Admin) return;

        // Hech kim tegmagan yoki allaqachon ochiq — to'sadigan narsa yo'q.
        if (recording.IsVisibleToStudents || recording.VisibilityChangedById is not { } lastId)
            return;

        var closedByManagement = await db.Users
            .AsNoTracking()
            .AnyAsync(
                u => u.Id == lastId
                  && (u.Role == UserRole.Academic || u.Role == UserRole.Admin),
                ct)
            .ConfigureAwait(false);

        if (closedByManagement)
        {
            throw new ForbiddenException(
                "Bu yozuvni o'quv bo'limi yopgan — uni faqat o'quv bo'limi qayta ocha oladi.");
        }
    }

    /// <summary>
    /// "Dars yozuvlari bo'limi umuman ochiqmi" — GLOBAL kalit
    /// (<c>recordings.visible_to_students</c>).
    ///
    /// ⚠️ HAR SO'ROVDA O'QILADI, ishga tushishda emas: `ISettingsResolver`
    /// keshdan javob beradi, ya'ni narxi sezilmaydi, lekin paneldan
    /// o'zgartirilgan qiymat DARHOL kuchga kiradi. Aks holda panel
    /// "saqlandi" derdi-yu, o'quvchilarda hech nima o'zgarmasdi —
    /// registrdagi eng qattiq qoida shu turdagi jimgina yolg'onni
    /// taqiqlaydi.
    ///
    /// ★ NOTO'G'RI QIYMAT (`"ha"`, bo'sh satr) — OCHIQ deb o'qiladi.
    ///   Buzuq sozlama butun bo'limni jimgina o'chirib qo'ymasin: bu
    ///   bayroq YOPISH uchun ATAYLAB bosiladi, tasodifan emas.
    /// </summary>
    private async Task<bool> SectionOpenAsync(CancellationToken ct)
    {
        var resolved = await settings.ResolveAsync(SectionSetting, ct).ConfigureAwait(false);

        return !SettingValueParser.TryReadBool(resolved.Value, out var enabled) || enabled;
    }

    private static readonly SettingDefinition SectionSetting =
        SettingsRegistry.TryGet(SettingsRegistry.Keys.RecordingsVisibleToStudents, out var d)
            ? d
            : throw new InvalidOperationException(
                "`recordings.visible_to_students` registrda topilmadi.");

    /// <summary>
    /// O'quvchi ko'radigan yozuvlar filtri — UCHTA shartning ko'paytmasi.
    ///
    /// ★ O'QUVCHIGA FAQAT TAYYOR YOZUV KO'RINADI (eski qoida, o'zgarmadi):
    ///   unga "urinish yiqildi" degan qator hech narsa bermaydi. Xodimga
    ///   esa AKSINCHA — aynan o'sha qatorlar "nega bu darsning yozuvi
    ///   yo'q?" degan savolga javob.
    ///
    /// ★ R5 QO'SHGANI: guruh kaliti va yozuvning O'Z kaliti. Global kalit
    ///   bu yerda EMAS — u so'rovdan oldin qaraladi va butun ro'yxatni
    ///   bo'sh qaytaradi (bazaga borish shart emas).
    ///
    /// ⚠️ `Session!.Group!` — ikkala navigatsiya ham NOT NULL FK
    ///   (`SessionRecordings -> LiveSessions -> Groups`, ikkalasi ham
    ///   Cascade), ya'ni bu `INNER JOIN` ga tushadi va hech qanday qator
    ///   "yo'qolib qolmaydi".
    /// </summary>
    private static IQueryable<SessionRecording> ApplyVisibility(
        IQueryable<SessionRecording> query, bool isStaff) =>
        isStaff
            ? query
            : query.Where(r =>
                r.Status == RecordingStatus.Completed
                && r.IsVisibleToStudents
                && r.Session!.Group!.RecordingsVisibleToStudents);

    /// <summary>
    /// BITTA yozuv o'quvchiga ko'rinadimi — havola yo'li uchun.
    ///
    /// 🔴 Bu <see cref="ApplyVisibility"/> ning AYNI qoidasi, lekin bitta
    /// qator uchun. Ikki ta'rif ajralib ketmasligi kerak: ro'yxatda
    /// ko'rinmaydigan yozuv havolasi ham berilmasin va aksincha.
    /// (<c>Status</c> shartini chaqiruvchi allaqachon tekshirgan —
    /// <c>IsPlayable</c> darvozasi yuqorida.)
    /// </summary>
    private async Task<bool> IsVisibleToStudentAsync(
        SessionRecording recording, CancellationToken ct)
    {
        if (!recording.IsVisibleToStudents) return false;

        if (!await SectionOpenAsync(ct).ConfigureAwait(false)) return false;

        return await db.LiveSessions
            .AsNoTracking()
            .Where(s => s.Id == recording.SessionId)
            .Select(s => s.Group!.RecordingsVisibleToStudents)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Ro'yxatdagi darslarning tahlil xulosalari — BITTA so'rov bilan (R29).
    ///
    /// 🔴 O'QUVCHIGA UMUMAN SO'RALMAYDI: tahlil undan yopiq va uning
    /// BORLIGI haqidagi ishora ham berilmaydi (<c>SessionReview</c> izohi).
    /// Ya'ni bu — nafaqat tejash, balki chegaraning O'ZI.
    ///
    /// ★ N+1 YO'Q: 30 ta yozuvli sahifa uchun bitta `WHERE SessionId IN (…)`
    ///   so'rovi va u `UX_SessionReviews_SessionId` indeksiga tushadi.
    /// </summary>
    private async Task<IReadOnlyDictionary<long, SessionReviewVerdict>> LoadVerdictsAsync(
        List<SessionRecording> rows, bool isStaff, CancellationToken ct)
    {
        if (!isStaff || rows.Count == 0)
            return ReadOnlyDictionary<long, SessionReviewVerdict>.Empty;

        var sessionIds = rows.Select(r => r.SessionId).Distinct().ToArray();

        var pairs = await db.SessionReviews
            .AsNoTracking()
            .Where(r => sessionIds.Contains(r.SessionId))
            .Select(r => new { r.SessionId, r.Verdict })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return pairs.ToDictionary(p => p.SessionId, p => p.Verdict);
    }

    /// <summary>
    /// Bitta yozuv -> DTO, tahlil xulosasi bilan.
    ///
    /// ★ FAQAT XODIM YO'LLARIDA CHAQIRILADI (boshlash, to'xtatish,
    ///   ko'rinishni o'zgartirish) — ularning hammasi rol darvozasidan
    ///   o'tgan. Ro'yxatlar bu metodni ISHLATMAYDI: ular xulosalarni
    ///   BITTA to'plamli so'rov bilan oladi (<see cref="LoadVerdictsAsync"/>),
    ///   aks holda har qator uchun alohida so'rov ketardi.
    ///
    /// ⚠️ Amalda bu so'rov deyarli har doim bo'sh qaytadi: dars endi
    ///    boshlanayotganda tahlil hali yozilmagan bo'ladi. Shunga qaramay
    ///    u O'TKAZIB YUBORILMAYDI — DTO'ning bir yo'lda to'g'ri, boshqa
    ///    yo'lda "har doim `false`" bo'lishi jimgina yolg'on bo'lardi va
    ///    kelajakda kimdir bu javobga ishonib qolardi.
    /// </summary>
    private async Task<RecordingDto> MapWithReviewAsync(
        SessionRecording recording, CancellationToken ct)
    {
        var verdict = await db.SessionReviews
            .AsNoTracking()
            .Where(r => r.SessionId == recording.SessionId)
            .Select(r => (SessionReviewVerdict?)r.Verdict)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return Map(recording, includeError: true, verdict);
    }

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

    /// <summary>
    /// Yozuv -> DTO.
    /// </summary>
    /// <param name="includeError">
    /// Xato sababi ICHKI tafsilot (Egress xabari) — faqat xodimga.
    /// </param>
    /// <param name="verdict">
    /// Darsning tahlil xulosasi yoki <c>null</c>. 🔴 O'QUVCHI yo'lida bu
    /// DOIM <c>null</c> bo'ladi — chaqiruvchi uni umuman so'ramaydi
    /// (<see cref="LoadVerdictsAsync"/>), ya'ni "tahlil bor" degan ishora
    /// ham o'quvchiga yetib bormaydi.
    /// </param>
    private static RecordingDto Map(
        SessionRecording r, bool includeError, SessionReviewVerdict? verdict) => new(
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
        r.CreatedAt,
        r.IsVisibleToStudents,
        verdict is not null,
        verdict?.ToString());

    private static RecordingDto Map(
        SessionRecording r,
        bool includeError,
        IReadOnlyDictionary<long, SessionReviewVerdict> verdicts) =>
        Map(
            r,
            includeError,
            verdicts.TryGetValue(r.SessionId, out var verdict)
                ? verdict
                : null);

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
