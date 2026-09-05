using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Groups.Dtos;
using Zinnur.Application.Scheduling.Dtos;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.Students.Services;
using Zinnur.Domain.Common;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Staffing;

namespace Zinnur.Application.Groups.Services;

/// <summary>
/// Guruhlarni boshqarish use-case'lari.
/// HTTP haqida HECH NARSA bilmaydi — faqat Application/Domain xatolarini ko'taradi.
///
/// Jadval mantiqi bu yerda TAKRORLANMAYDI: qoida
/// <see cref="Zinnur.Domain.Entities.Group"/> da, generatsiya
/// <see cref="Zinnur.Domain.Scheduling.ScheduleGenerator"/> da, yozish esa
/// <see cref="IScheduleService"/> da. Bu servis faqat QAROR qabul qiladi:
/// "jadvalga tegilsinmi va qanday".
/// </summary>
public sealed class GroupService(
    IApplicationDbContext db,
    IScheduleService schedule,
    IScheduleTimeZoneProvider timeZone,
    IStudiedLessonCounter studiedLessons,
    TimeProvider clock) : IGroupService
{
    // ================================================================= o'qish

    public async Task<PagedResult<GroupDto>> ListAsync(
        GroupListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanBrowse(actor);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = db.Groups.AsNoTracking();

        // USTOZ/KURATOR uchun ro'yxat AVTOMATIK o'z guruhlariga cheklanadi.
        //
        // NIMA UCHUN ALOHIDA `/groups/mine` ENDPOINTI YO'Q: ikkita endpoint
        // ikkita filtr mantiqi degani va ular vaqt o'tib bir-biridan ajralib
        // ketardi (masalan yangi filtr faqat bittasiga qo'shilardi). Bitta
        // endpoint + roldan kelib chiqadigan filtr — bitta haqiqat manbai.
        if (!CanReadAll(actor))
            rows = rows.Where(VisibleTo(actor.Id));

        if (query.Type is { } type)
            rows = rows.Where(g => g.Type == type);

        if (query.IsActive is { } isActive)
            rows = rows.Where(g => g.IsActive == isActive);

        /* ===== R21b · KATEGORIYA FILTRI =====

           ★ MAVJUDLIK TEKSHIRILMAYDI (ataylab): yo'q kategoriya so'ralsa
           natija BO'SH ro'yxat bo'ladi, 404 emas. Bu GET ro'yxat so'rovi —
           u hech qachon 404 bermasligi kerak (`ListCuratorCandidatesAsync`
           bilan AYNI mulohaza). Qo'shimcha `EXISTS` so'rovi esa har
           sahifalashda bekorga ketardi. */
        if (query.CategoryId is { } categoryId)
            rows = rows.Where(g => g.CategoryId == categoryId);

        rows = ApplySearch(rows, query.Search);

        // Ikkita so'rov (COUNT + sahifa) — `Total` bo'lmasa frontend paginator
        // sahifalar sonini bila olmaydi.
        var total = await rows.CountAsync(ct);

        var items = await Project(rows
                .OrderBy(g => g.Name)
                .ThenBy(g => g.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(ct);

        return new PagedResult<GroupDto>(items.ConvertAll(Map), page, pageSize, total);
    }

    public async Task<GroupDto> GetAsync(long id, long actorId, CancellationToken ct = default)
    {
        await LoadForReadAsync(id, actorId, ct);
        return await GetDtoAsync(id, ct);
    }

    // ================================================================= yaratish

    public async Task<CreateGroupResponse> CreateAsync(
        CreateGroupRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var name = RequireName(request.Name);
        var weekdays = RequireWeekdays(request.Weekdays);
        RequireKnownType(request.Type);

        await EnsureCourseExistsAsync(request.CourseId, ct);
        await EnsureCategoryExistsAsync(request.CategoryId, ct);
        await EnsureStaffAsync(request.TeacherId, request.AssistantId, ct);
        await EnsureVideoStartLessonAsync(request.CourseId, request.VideoStartLessonId, ct);

        var group = new Group
        {
            Name = name,
            Type = request.Type,
            CourseId = request.CourseId,
            CategoryId = request.CategoryId,
            VideoStartLessonId = request.VideoStartLessonId,
            TeacherId = request.TeacherId,
            AssistantId = request.AssistantId,
            CuratorGroupId = request.CuratorGroupId,
            StartDate = request.StartDate,
            CourseMonths = request.CourseMonths,
            Weekdays = [.. weekdays],
            StartTime = request.StartTime,
            DurationMinutes = request.DurationMinutes,
            RecordEnabled = request.RecordEnabled,
            RecordingsVisibleToStudents = request.RecordingsVisibleToStudents,
            RecordingPipeline = request.RecordingPipeline,
            AssignmentGraderRole = request.AssignmentGraderRole,
            QuestionResponderRole = request.QuestionResponderRole,
            IsActive = request.IsActive,
        };

        await EnsureCuratorLinkAsync(group, request.CuratorGroupId, ct);

        await EnsureResponsibleSeatsFilledAsync(group, ct);

        // Domain qoidasi (kun soni, davomiylik, kurator mas'uli va h.k.).
        // Buzilsa DomainException -> HTTP 409.
        group.ValidateScheduleRule();

        db.Groups.Add(group);

        // JADVAL DARHOL TUZILADI va guruh bilan BITTA SaveChanges'da yoziladi.
        // Aks holda "jadvali yo'q guruh" holati paydo bo'lardi va uni qo'lda
        // tuzatish kerak bo'lardi.
        var created = await schedule.GenerateForNewGroupAsync(group, ct);

        await SaveWithUniqueGuardAsync(ct);

        return new CreateGroupResponse(await GetDtoAsync(group.Id, ct), created);
    }

    // ================================================================= tahrirlash

    /// <summary>
    /// ========================================================================
    /// ★ JADVAL QAYTA TUZISH QARORI — MODULNING ENG NOZIK JOYI
    /// ========================================================================
    ///
    /// ESKI TIZIM BUGI: guruh tahrirlanganda jadval SHARTSIZ qayta tuzilardi.
    /// Ya'ni faqat kursni yoki kuratorni almashtirsangiz ham butun kelajak
    /// jadval o'chib qayta yaratilardi — dars Id'lari, LiveKit xona nomlari va
    /// tarqatilgan havolalar o'zgarardi.
    ///
    /// ENDIGI QAROR JADVALI:
    ///
    /// | O'zgargan maydon                                   | Jadvalga ta'sir              |
    /// |----------------------------------------------------|------------------------------|
    /// | StartDate, Weekdays, StartTime, DurationMinutes,   | QAYTA TUZILADI               |
    /// | CourseMonths, Type  (`ScheduleRuleDiffersFrom`)    | (faqat kelajak `Scheduled`)  |
    /// | TeacherId / AssistantId                            | `HostId` O'RNIDA yangilanadi |
    /// | Name                                               | sarlavha O'RNIDA yangilanadi |
    /// | CourseId, CuratorGroupId, RecordEnabled, IsActive  | TEGILMAYDI                   |
    ///
    /// "O'rnida" degani: dars Id, xona nomi, davomat va chat SAQLANADI.
    ///
    /// Taqqoslash <c>Group.ScheduleRuleDiffersFrom</c> ga topshirilgan —
    /// qaysi maydon "jadvalga ta'sir qiluvchi" ekani Domain'da BIR joyda
    /// yozilgan, shu tufayli yangi maydon qo'shilganda bu yerda unutib
    /// bo'lmaydi.
    /// </summary>
    public async Task<UpdateGroupResponse> UpdateAsync(
        long id, UpdateGroupRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var group = await LoadForManageAsync(id, ct);

        var name = RequireName(request.Name);
        var weekdays = RequireWeekdays(request.Weekdays);
        RequireKnownType(request.Type);

        await EnsureCourseExistsAsync(request.CourseId, ct);
        await EnsureCategoryExistsAsync(request.CategoryId, ct);
        await EnsureStaffAsync(request.TeacherId, request.AssistantId, ct);
        await EnsureCuratorLinkAsync(group, request.CuratorGroupId, ct);

        // ★★ VIDEO BOSHLANISH NUQTASI **YANGI** KURSGA QARAB TEKSHIRILADI.
        //
        // 🔴 KURS ALMASHGANDA MAYDON ESKI QIYMATDA QOLIB KETMASLIGI kerak:
        // begona kursning darsi guruhda qolsa gating uni umuman topa olmasdi
        // ("dars kursga tegishli emas") va o'quvchi uchun butun kurs
        // tushunarsiz qulflanib qolardi.
        //
        // BU YERDA U O'ZI-O'ZIDAN HAL BO'LADI, chunki `PUT` = TO'LIQ
        // ALMASHTIRISH: yuborilmagan maydon `null` ga tushadi, ya'ni kursni
        // almashtirgan klient boshlanish nuqtasini yubormasa u TOZALANADI.
        // Yuborsa esa tekshiruv `request.CourseId` (YANGI kurs) bo'yicha
        // ketadi — eski kursning darsi 400 bo'lib qaytadi va bazaga HECH
        // NARSA yozilmaydi. Ya'ni "eski kursning darsi qolib ketishi"
        // holati mumkin emas: u yoki tozalanadi, yoki 400 bo'ladi.
        //
        // Jimgina tozalash (400 o'rniga) ATAYLAB TANLANMADI: klient aniq
        // yuborgan qiymatni indamay tashlab yuborish UI xatosini yashirardi
        // va o'quv bo'limi xodimi sozlama saqlanmaganini sezmasdi.
        await EnsureVideoStartLessonAsync(request.CourseId, request.VideoStartLessonId, ct);

        // ---- QAROR: taqqoslash O'ZGARTIRISHDAN OLDIN bajariladi ----
        var scheduleChanged = group.ScheduleRuleDiffersFrom(
            request.StartDate,
            weekdays,
            request.StartTime,
            request.DurationMinutes,
            request.CourseMonths,
            request.Type);

        var hostChanged = group.TeacherId != request.TeacherId
                       || group.AssistantId != request.AssistantId;

        var nameChanged = !string.Equals(group.Name, name, StringComparison.Ordinal);

        // ---- Endi qiymatlarni qo'yamiz ----
        group.Name = name;
        group.Type = request.Type;
        group.CourseId = request.CourseId;

        // 🔴 R21b · PUT = TO'LIQ ALMASHTIRISH. Yuborilmagan `categoryId`
        // shu qatorda `null` bo'lib yoziladi va guruh yorlig'ini YO'QOTADI.
        // Bu — `RecordingsVisibleToStudents` va `VideoStartLessonId` bilan
        // AYNI tuzoq; frontendda u `buildPayload` (uchala bo'limdan yig'ish)
        // bilan yopilgan.
        group.CategoryId = request.CategoryId;

        group.VideoStartLessonId = request.VideoStartLessonId;
        group.TeacherId = request.TeacherId;
        group.AssistantId = request.AssistantId;
        group.CuratorGroupId = request.CuratorGroupId;
        group.StartDate = request.StartDate;
        group.CourseMonths = request.CourseMonths;
        group.Weekdays = [.. weekdays];
        group.StartTime = request.StartTime;
        group.DurationMinutes = request.DurationMinutes;
        group.RecordEnabled = request.RecordEnabled;
        group.RecordingsVisibleToStudents = request.RecordingsVisibleToStudents;

        // Yozuv MEXANIZMI (SPEC-RECORDING-V2 §2.6) — `RecordEnabled` dan
        // MUSTAQIL ustun va u ham AYNI PUT qoidasiga bo'ysunadi:
        // yuborilmasa `RoomComposite` ga, ya'ni bugungi xatti-harakatga
        // qaytadi. Jadvalga ta'siri YO'Q, shuning uchun
        // `ScheduleRuleDiffersFrom` ga kirmaydi.
        group.RecordingPipeline = request.RecordingPipeline;

        // 🔴 R33 + R40 · PUT = TO'LIQ ALMASHTIRISH — yuqoridagi `categoryId`
        // bilan AYNI tuzoq. Ikkala maydonning standartlari ATAYLAB bugungi
        // xatti-harakat qilib tanlangan (`Both` va `Assistant`), ya'ni eski
        // klient ularni yubormasa ham hech narsa buzilmaydi.
        group.AssignmentGraderRole = request.AssignmentGraderRole;
        group.QuestionResponderRole = request.QuestionResponderRole;

        group.IsActive = request.IsActive;

        group.ValidateScheduleRule();

        await EnsureResponsibleSeatsFilledAsync(group, ct);

        var summary = await ApplyScheduleDecisionAsync(
            group, scheduleChanged, hostChanged, nameChanged, ct);

        // BITTA SaveChanges: guruh maydonlari va jadval o'zgarishi bitta
        // tranzaksiyada. Yarim holat (guruh yangilangan, jadval eski) bo'lmaydi.
        await SaveWithUniqueGuardAsync(ct);

        return new UpdateGroupResponse(await GetDtoAsync(group.Id, ct), summary);
    }

    /// <summary>Qaror jadvalini bajaradi (yuqoridagi izohdagi to'rt yo'l).</summary>
    private async Task<ScheduleChangeSummary> ApplyScheduleDecisionAsync(
        Group group, bool scheduleChanged, bool hostChanged, bool nameChanged, CancellationToken ct)
    {
        // 1) Jadval qoidasi o'zgardi -> qayta tuzish. Yangi darslar allaqachon
        //    yangi nom va yangi host bilan yaratiladi, shuning uchun "o'rnida
        //    tahrirlash" qadamlari KERAK EMAS (ular faqat qoida o'zgarmaganda).
        if (scheduleChanged)
            return await schedule.RegenerateAsync(group, ct);

        // 2) Faqat ustoz/kurator va/yoki nom o'zgardi -> darslar O'RNIDA
        //    tahrirlanadi: Id, xona nomi, davomat va chat saqlanadi.
        var hosts = hostChanged ? await schedule.RetargetHostAsync(group, ct) : 0;
        var titles = nameChanged ? await schedule.RenameFutureSessionsAsync(group, ct) : 0;

        if (hosts > 0 || titles > 0)
            return ScheduleChangeSummary.InPlace(hosts, titles, InPlaceReason);

        // 3) Jadvalga ta'sir qiluvchi hech narsa o'zgarmadi (kurs, kurator
        //    bog'lanishi, yozuv bayrog'i, faollik) -> TEGILMAYDI.
        return ScheduleChangeSummary.Untouched(
            hostChanged || nameChanged ? NothingToUpdateReason : UntouchedReason);
    }

    public async Task<GroupDto> SetActiveAsync(
        long id, bool isActive, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var group = await LoadForManageAsync(id, ct);

        if (group.IsActive != isActive)
        {
            // Arxivlash jadvalga TEGMAYDI: guruh keyin tiklanishi mumkin va
            // o'sha paytda jadval o'z joyida turishi kerak.
            group.IsActive = isActive;
            await db.SaveChangesAsync(ct);
        }

        return await GetDtoAsync(id, ct);
    }

    // ================================================================= a'zolik

    /// <summary>
    /// Guruh a'zolari.
    ///
    /// 🔴 YAGONA USTOZGA OCHIQ PROYEKSIYA — kontakt SHU YERDA kesiladi
    /// (talab R27). Qolgan a'zolik metodlari <c>EnsureCanManage</c> bilan
    /// qulflangan, ya'ni ularga faqat o'quv bo'limi va admin yetadi va
    /// u yerda kesish shart emas.
    /// </summary>
    public async Task<IReadOnlyList<GroupMemberDto>> ListMembersAsync(
        long id, long actorId, CancellationToken ct = default)
    {
        var (actor, group) = await LoadForReadAsync(id, actorId, ct);

        return await ProjectMembers(
                MembersOf(group)
                    .OrderBy(m => m.Student!.FullName)
                    .ThenBy(m => m.Id),
                // ⚠️ KURATOR ISTISNO: unga telefon KERAK (qo'ng'iroq —
                //    uning asosiy amali). Sabab `StudentAudience` izohida.
                withContact: actor.Role != UserRole.Teacher)
            .ToListAsync(ct);
    }

    public async Task<GroupMemberDto> AddMemberAsync(
        long id, AddMemberRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var group = await LoadGroupAsync(id, ct);
        EnsureAcceptsDirectMembers(group);

        if (!group.IsActive)
            throw new ConflictException("Arxivlangan guruhga o'quvchi qo'shilmaydi.");

        var student = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.StudentId, ct)
            ?? throw new NotFoundException(nameof(User), request.StudentId);

        // Faqat O'QUVCHI. Aks holda ustoz yoki admin a'zo bo'lib qolib,
        // davomat va to'lov hisobotlariga tushib ketardi.
        if (student.Role != UserRole.Student)
            throw Invalid(nameof(request.StudentId), "Guruhga faqat 'Student' rolidagi foydalanuvchi qo'shiladi.");

        if (!student.IsActive)
            throw new ConflictException("Foydalanuvchi profili faol emas.");

        // ══════════════════════════════════════════════════════════════
        // ★★ BIR VAQTDA FAQAT BITTA GURUH (2026-08-17, loyiha egasi:
        //    "o'quvchi bir vaqtda faqatgina bitta o'qituvchi guruhida
        //    bo'lishi mumkin").
        //
        // Ilgari bu yerda faqat "AYNI guruhda ikkinchi marta" tekshirilardi
        // — o'quvchini boshqa (uchinchi) ustoz guruhiga qo'shishga hech
        // narsa to'sqinlik qilmasdi. Amalda bu ikkita "faol" a'zolik
        // beradi va davomat/to'lov/"Mening guruhim" kabi HAR joyda
        // ikkilanish paydo qiladi (aynan shunday holat demo ma'lumotida
        // topilgan edi).
        //
        // ★ NEGA `MoveMemberAsync` GA TEGILMAYDI: u bitta tranzaksiyada
        //   ESKI a'zolikni `Moved` qiladi VA YANGISINI ochadi — natijada
        //   invariant o'zi saqlanadi. Bu tekshiruv faqat `AddMemberAsync`
        //   uchun kerak, chunki u ESKINI YOPMAYDI.
        //
        // ★ KURATOR GURUHI HISOBGA OLINMAYDI (`Type != Curator`): bu
        //   metodning O'ZI kurator guruhiga to'g'ridan-to'g'ri qo'shishga
        //   yo'l qo'ymaydi (`EnsureAcceptsDirectMembers`, yuqorida) — ya'ni
        //   bu yerga yetib kelgan `group` HAR DOIM ustoz/yakka guruh.
        // ══════════════════════════════════════════════════════════════
        // ⚠️ `Paused` HAM HISOBGA OLINADI, faqat `Active` EMAS: pauza —
        //    vaqtinchalik to'xtash, TO'LIQ chiqish emas (`IsActive`
        //    hisoblanuvchisi buni "nofaol" desa ham, o'quvchi hamon o'sha
        //    guruhning a'zosi). Faqat `Stopped`/`Moved` — haqiqiy chiqish.
        var otherGroup = await db.GroupMembers.AsNoTracking()
            .Where(m => m.StudentId == student.Id
                     && (m.Status == MemberStatus.Active || m.Status == MemberStatus.Paused)
                     && m.GroupId != group.Id
                     && m.Group!.Type != GroupType.Curator)
            .Select(m => m.Group!.Name)
            .FirstOrDefaultAsync(ct);

        if (otherGroup is not null)
        {
            throw new ConflictException(
                $"O'quvchi allaqachon boshqa guruhda: \"{otherGroup}\". "
                + "Bir vaqtda faqat bitta guruhda bo'lishi mumkin — avval o'sha guruhdan "
                + "chiqaring yoki \"Ko'chirish\" (Move) funksiyasidan foydalaning.");
        }

        var member = await db.GroupMembers.AsTracking()
            .FirstOrDefaultAsync(m => m.GroupId == group.Id && m.StudentId == student.Id, ct);

        if (member is null)
        {
            member = new GroupMember
            {
                GroupId = group.Id,
                StudentId = student.Id,
                Status = MemberStatus.Active,
                JoinedAt = clock.GetUtcNow(),
            };

            db.GroupMembers.Add(member);
        }
        else if (member.Status == MemberStatus.Active)
        {
            throw new ConflictException("O'quvchi allaqachon shu guruhda.");
        }
        else
        {
            // TIKLASH, yangi qator EMAS: `UX_GroupMembers_GroupId_StudentId`
            // unikal indeksi ikkinchi qatorga yo'l bermaydi. Eski tizim shu
            // yerda dublikat yozuv yaratardi va davomat ikki marta sanalardi.
            member.Status = MemberStatus.Active;
            member.JoinedAt = clock.GetUtcNow();

            // ARXIV IZI TOZALANADI: o'quvchi endi FAOL, eski "chiqarilgan/
            // ko'chirilgan" izi hozirgi holatga aloqasi yo'q — qolib ketsa
            // arxiv jadvalida ko'rinmasa ham, keyingi safar chiqarilganda
            // eski sababni yangisiga aralashtirib qo'yardi.
            member.LeftAt = null;
            member.LeftById = null;
            member.MovedToGroupId = null;
            member.Reason = null;
        }

        SetPausedUntil(member, null);

        // ★ TARIXGA YOZILADI (2026-08-17): arxiv izi yuqorida tozalangani
        //   uchun "qaytdi" fakti FAQAT shu jurnalda qoladi.
        await RecordMembershipEventAsync(
            member, group, MembershipEventKind.Joined,
            reason: null, movedToGroupId: null, actorId, ct);

        await SaveWithUniqueGuardAsync(ct);
        return await GetMemberDtoAsync(member.Id, ct);
    }

    public async Task<GroupMemberDto> PauseMemberAsync(
        long id, long studentId, PauseMemberRequest request, long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (member, group) = await LoadMemberForManageAsync(id, studentId, actorId, ct);

        if (member.Status == MemberStatus.Stopped)
            throw new ConflictException("Guruhdan chiqarilgan o'quvchini pauzaga qo'yish mumkin emas.");

        if (member.Status == MemberStatus.Moved)
            throw new ConflictException("Boshqa guruhga ko'chirilgan o'quvchini pauzaga qo'yish mumkin emas.");

        if (request.PausedUntil is { } until && until < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            throw Invalid(nameof(request.PausedUntil), "Pauza muddati o'tgan sana bo'lishi mumkin emas.");

        // MAJBURIY SABAB (loyiha egasi, 2026-08-17): "to'kilishlar" paneli
        // muzlatishni ham ko'rsatadi va sababsiz qator u yerda ma'nosiz
        // bo'lardi. Ko'chirishdagi AYNI qoida (2026-08-15) endi muzlatish
        // va chiqarishga ham yoyildi.
        var reason = RequireReason(request.Reason, nameof(request.Reason), "Muzlatish sababini kiriting.");
        await EnsureReasonUsableAsync(request.ReasonId, ct);

        member.Status = MemberStatus.Paused;
        SetPausedUntil(member, request.PausedUntil);

        await RecordMembershipEventAsync(
            member, group, MembershipEventKind.Paused, reason, movedToGroupId: null, actorId, ct,
            request.ReasonId);

        await db.SaveChangesAsync(ct);
        return await GetMemberDtoAsync(member.Id, ct);
    }

    public async Task<GroupMemberDto> ResumeMemberAsync(
        long id, long studentId, long actorId, CancellationToken ct = default)
    {
        var (member, group) = await LoadMemberForManageAsync(id, studentId, actorId, ct);

        // Chiqarilgan yoki ko'chirilgan a'zolik "tiklanmaydi" — bu boshqa
        // amal (qayta qo'shish), aks holda ko'chirish tarixini jimgina
        // buzib qo'yardi.
        if (member.Status is MemberStatus.Stopped or MemberStatus.Moved)
            throw new ConflictException(
                "Bu a'zolik pauzada emas. O'quvchini qaytadan qo'shish uchun "
                + "\"a'zo qo'shish\" amalidan foydalaning.");

        if (member.Status != MemberStatus.Active)
        {
            member.Status = MemberStatus.Active;
            SetPausedUntil(member, null);

            await RecordMembershipEventAsync(
                member, group, MembershipEventKind.Resumed,
                reason: null, movedToGroupId: null, actorId, ct);

            await db.SaveChangesAsync(ct);
        }

        return await GetMemberDtoAsync(member.Id, ct);
    }

    public async Task<GroupMemberDto> RemoveMemberAsync(
        long id, long studentId, RemoveMemberRequest request, long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (member, group) = await LoadMemberForManageAsync(id, studentId, actorId, ct);

        // MAJBURIY SABAB (loyiha egasi, 2026-08-17) — sabab TEKSHIRUVI holat
        // tekshiruvidan OLDIN: allaqachon chiqarilgan o'quvchida ham bo'sh
        // sabab yuborilsa, xodim "nega hech nima bo'lmadi?" degan holatga
        // tushmasin, aniq xato olsin.
        var reason = RequireReason(request.Reason, nameof(request.Reason), "Chiqarish sababini kiriting.");
        await EnsureReasonUsableAsync(request.ReasonId, ct);

        // YUMSHOQ o'chirish: yozuv qoladi. Davomat, to'lov va hisobotlar
        // a'zolikka ishora qiladi — qator o'chirilsa ular yetim qolardi.
        if (member.Status != MemberStatus.Stopped)
        {
            member.Status = MemberStatus.Stopped;
            member.LeftAt = clock.GetUtcNow();
            member.LeftById = actorId;

            // Sabab endi a'zolik qatoriga HAM yoziladi: arxiv jadvali
            // (`GroupMembersPanel`) uni o'sha yerdan o'qiydi. Tarixiy,
            // o'chmaydigan nusxa esa hodisa jurnalida.
            member.Reason = reason;
            SetPausedUntil(member, null);

            await RecordMembershipEventAsync(
                member, group, MembershipEventKind.Stopped, reason, movedToGroupId: null, actorId, ct,
                request.ReasonId);

            await db.SaveChangesAsync(ct);
        }

        return await GetMemberDtoAsync(member.Id, ct);
    }

    public async Task<MoveMemberResponse> MoveMemberAsync(
        long id, long studentId, MoveMemberRequest request, long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TargetGroupId == id)
            throw new ConflictException("Manba va nishon guruh bir xil.");

        // MAJBURIY SABAB (loyiha egasi, 2026-08-15): ko'chirish — boshqa
        // xodim keyinroq "nega bu o'quvchi shu yerda emas?" deb so'raganda
        // javob topadigan yagona joy.
        //
        // ⚠️ 2026-08-17: bu qoida endi FAQAT ko'chirishga tegishli emas —
        // chiqarish va muzlatish ham sabab talab qiladi, shuning uchun
        // tekshiruv umumiy `RequireReason` yordamchisiga ko'chirildi.
        var reason = RequireReason(request.Reason, nameof(request.Reason), "Ko'chirish sababini kiriting.");
        await EnsureReasonUsableAsync(request.ReasonId, ct);

        var (member, group) = await LoadMemberForManageAsync(id, studentId, actorId, ct);

        var target = await LoadGroupAsync(request.TargetGroupId, ct);
        EnsureAcceptsDirectMembers(target);

        if (!target.IsActive)
            throw new ConflictException("Arxivlangan guruhga ko'chirib bo'lmaydi.");

        var arrived = await db.GroupMembers.AsTracking()
            .FirstOrDefaultAsync(m => m.GroupId == target.Id && m.StudentId == studentId, ct);

        if (arrived is { Status: MemberStatus.Active })
            throw new ConflictException("O'quvchi allaqachon nishon guruhda faol.");

        var now = clock.GetUtcNow();

        member.Status = MemberStatus.Moved;
        member.LeftAt = now;
        member.LeftById = actorId;
        member.MovedToGroupId = target.Id;
        member.Reason = reason;
        SetPausedUntil(member, null);

        // ★ IKKI HODISA, IKKI GURUH: manba guruhdan "ko'chirildi", nishon
        //   guruhga "qo'shildi". Ikkinchisi ham kerak — aks holda nishon
        //   guruhning tarixida o'quvchi qayerdan paydo bo'lgani ko'rinmasdi.
        //   `Moved` hodisasi manba guruh MA'LUMOTI bilan yoziladi (ustoz
        //   surati ham manba ustozi) — to'kilish hisoboti aynan shuni sanaydi.
        await RecordMembershipEventAsync(
            member, group, MembershipEventKind.Moved, reason, target.Id, actorId, ct,
            request.ReasonId);

        if (arrived is null)
        {
            arrived = new GroupMember
            {
                GroupId = target.Id,
                StudentId = studentId,
                Status = MemberStatus.Active,
                JoinedAt = now,
            };

            db.GroupMembers.Add(arrived);
        }
        else
        {
            arrived.Status = MemberStatus.Active;
            arrived.JoinedAt = now;

            // Nishon guruhda ILGARI chiqarilgan/ko'chirilgan qator bo'lsa
            // (o'quvchi shu guruhga QAYTA ko'chirilmoqda) — eski arxiv izi
            // endi noto'g'ri, `AddMemberAsync`dagi AYNI tozalash.
            arrived.LeftAt = null;
            arrived.LeftById = null;
            arrived.MovedToGroupId = null;
            arrived.Reason = null;
        }

        SetPausedUntil(arrived, null);

        await RecordMembershipEventAsync(
            arrived, target, MembershipEventKind.Joined,
            reason: null, movedToGroupId: null, actorId, ct);

        // ATOMIK: bitta SaveChanges = bitta tranzaksiya. "Eski guruhdan
        // chiqib, yangisiga kirmagan" yarim holat MUMKIN EMAS — eski tizimda
        // bu ikki alohida so'rov edi va ikkinchisi yiqilsa o'quvchi
        // hech qaysi guruhda qolmasdi.
        await SaveWithUniqueGuardAsync(ct);

        return new MoveMemberResponse(
            await GetMemberDtoAsync(member.Id, ct),
            await GetMemberDtoAsync(arrived.Id, ct));
    }

    // ================================================================= jadval

    public async Task<IReadOnlyList<ScheduledSessionDto>> GetScheduleAsync(
        long id, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, long actorId,
        CancellationToken ct = default)
    {
        await LoadForReadAsync(id, actorId, ct);

        if (fromUtc is { } start && toUtc is { } end && end < start)
            throw Invalid("to", "Oraliq oxiri boshidan oldin bo'lishi mumkin emas.");

        return await schedule.ListAsync(id, fromUtc, toUtc, ct);
    }

    public async Task<ScheduleChangeSummary> RegenerateScheduleAsync(
        long id, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var group = await LoadForManageAsync(id, ct);

        var summary = await schedule.RegenerateAsync(group, ct);
        await db.SaveChangesAsync(ct);

        return summary;
    }

    // ================================================================= kurator

    public async Task<IReadOnlyList<CuratorCandidateDto>> ListCuratorCandidatesAsync(
        long id, long actorId, CancellationToken ct = default)
    {
        var (_, group) = await LoadForReadAsync(id, actorId, ct);

        // Kurator guruhi boshqa kurator guruhiga bog'lanmaydi (Domain qoidasi),
        // shuning uchun uning uchun nomzod bo'lishi mumkin emas. Bu XATO emas —
        // shunchaki bo'sh ro'yxat (GET so'rov 409 bermasligi kerak).
        if (group.IsCuratorGroup) return [];

        var rows = await db.Groups.AsNoTracking()
            .Where(c => c.Type == GroupType.Curator
                     && c.IsActive
                     && c.Id != group.Id
                     // Zanjir bo'lmasin: o'zi boshqa kuratorga bog'langan
                     // guruh nomzod bo'lmaydi.
                     && c.CuratorGroupId == null)
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Select(c => new CandidateProjection(
                c.Id,
                c.Name,
                c.AssistantId,
                db.Users.Where(u => u.Id == c.AssistantId).Select(u => u.FullName).FirstOrDefault(),
                c.CourseId,
                c.Course == null ? null : c.Course.Name,
                c.Weekdays,
                c.StartTime,
                db.Groups.Count(t => t.CuratorGroupId == c.Id)))
            .ToListAsync(ct);

        return rows.ConvertAll(c => new CuratorCandidateDto(
            c.Id, c.Name, c.AssistantId, c.AssistantName,
            c.CourseId, c.CourseName, c.Weekdays, c.StartTime, c.LinkedGroupCount));
    }

    // ================================================================= RUXSAT QOIDASI

    /// <summary>
    /// ================================================================
    /// GURUHLARNI BOSHQARISHNING YAGONA RUXSAT QOIDASI
    /// ================================================================
    /// O'zgartiruvchi HAR BIR metod shu tekshiruvdan o'tadi (yaratish,
    /// tahrirlash, arxivlash, a'zolik, jadvalni qayta tuzish).
    ///
    /// Controller'dagi <c>[Authorize(Roles=...)]</c> faqat DARVOZA — u
    /// "umuman kira oladimi" degan savolga javob beradi. Haqiqiy qoida shu
    /// yerda, chunki servis SignalR hub'idan yoki fon vazifasidan ham
    /// chaqirilishi mumkin (o'sha yerda atribut ishlamaydi).
    /// </summary>
    private static void EnsureCanManage(User actor)
    {
        if (actor.Role is not (UserRole.Admin or UserRole.Academic))
        {
            throw new ForbiddenException(
                "Guruhlarni faqat o'quv bo'limi xodimi yoki administrator o'zgartira oladi.");
        }
    }

    /// <summary>Ro'yxatni umuman ko'ra oladimi (o'quvchi ko'ra olmaydi).</summary>
    private static void EnsureCanBrowse(User actor)
    {
        if (actor.Role is UserRole.Student)
            throw new ForbiddenException("Guruhlar ro'yxatiga ruxsatingiz yo'q.");
    }

    private static bool CanReadAll(User actor) =>
        actor.Role is UserRole.Admin or UserRole.Academic;

    /// <summary>
    /// Ustoz/kurator KO'RA oladigan guruhlar filtri (ro'yxat va kartochka
    /// uchun bitta ifoda — ikki joyda ajralib ketmasin).
    ///
    /// Kurator o'z guruhiga BOG'LANGAN ustoz guruhlarini ham ko'radi: uning
    /// darsida aynan o'sha guruhlarning o'quvchilari qatnashadi.
    /// </summary>
    private static System.Linq.Expressions.Expression<Func<Group, bool>> VisibleTo(long userId) =>
        g => g.TeacherId == userId
          || g.AssistantId == userId
          || (g.CuratorGroup != null
              && (g.CuratorGroup.TeacherId == userId || g.CuratorGroup.AssistantId == userId));

    private static void EnsureCanRead(User actor, Group group)
    {
        if (CanReadAll(actor)) return;

        if (group.IsStaff(actor.Id)) return;

        if (group.CuratorGroup is { } curator && curator.IsStaff(actor.Id)) return;

        throw new ForbiddenException("Bu guruhga ruxsatingiz yo'q.");
    }

    /// <summary>Kurator guruhida o'quvchilar BEVOSITA a'zo bo'lmaydi.</summary>
    private static void EnsureAcceptsDirectMembers(Group group)
    {
        if (group.IsCuratorGroup)
        {
            throw new ConflictException(
                "Kurator guruhiga o'quvchi to'g'ridan-to'g'ri qo'shilmaydi. "
                + "Uning o'quvchilari bog'langan ustoz guruhlaridan keladi — "
                + "ustoz guruhini shu kuratorga bog'lang.");
        }
    }

    // ================================================================= ichki yordamchi

    private async Task<User> LoadActorAsync(long actorId, CancellationToken ct)
    {
        // Rol TOKEN'dan emas, BAZADAN olinadi: kirish tokeni 15 daqiqa
        // yashaydi, shuning uchun endi o'chirilgan yoki roli pasaytirilgan
        // xodim eski token bilan amal bajara olmasligi kerak.
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return actor;
    }

    /// <summary>Ko'rish uchun: kurator havolasi bilan (ruxsat tekshiruvi shunga tayanadi).</summary>
    private async Task<(User Actor, Group Group)> LoadForReadAsync(
        long id, long actorId, CancellationToken ct)
    {
        var actor = await LoadActorAsync(actorId, ct);

        var group = await db.Groups
            .AsNoTracking()
            .Include(g => g.CuratorGroup)
            .FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new NotFoundException(nameof(Group), id);

        EnsureCanRead(actor, group);
        return (actor, group);
    }

    /// <summary>
    /// Tahrirlash uchun: KUZATILADIGAN (tracked) guruh, `CuratorGroup`
    /// navigatsiyasi ATAYLAB yuklanmaydi.
    ///
    /// NIMA UCHUN: navigatsiya yuklangan holda `CuratorGroupId` ni
    /// o'zgartirsak, EF navigatsiya bilan FK orasidagi ziddiyatni o'zi
    /// "hal qilishga" urinadi va bog'lanishni kutilmaganda tiklab yoki
    /// bo'shatib qo'yishi mumkin. FK ni yolg'iz o'zgartirish — bir ma'noli.
    /// </summary>
    private async Task<Group> LoadForManageAsync(long id, CancellationToken ct) =>
        await db.Groups.AsTracking().FirstOrDefaultAsync(g => g.Id == id, ct)
        ?? throw new NotFoundException(nameof(Group), id);

    private async Task<Group> LoadGroupAsync(long id, CancellationToken ct) =>
        await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct)
        ?? throw new NotFoundException(nameof(Group), id);

    /// <summary>A'zolikni boshqarish uchun yuklaydi (ruxsat + kurator tekshiruvi bilan).</summary>
    /// <remarks>
    /// ★ GURUH HAM QAYTADI (2026-08-17): a'zolik hodisasi jurnaliga ustoz
    /// SURATI yozilishi kerak (<c>Group.TeacherId</c>), guruh esa baribir
    /// shu yerda yuklanadi — ikkinchi marta so'ramaslik uchun qaytariladi.
    /// </remarks>
    private async Task<(GroupMember Member, Group Group)> LoadMemberForManageAsync(
        long groupId, long studentId, long actorId, CancellationToken ct)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var group = await LoadGroupAsync(groupId, ct);
        EnsureAcceptsDirectMembers(group);

        var member = await db.GroupMembers.AsTracking()
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.StudentId == studentId, ct)
            ?? throw new NotFoundException(nameof(GroupMember), studentId);

        return (member, group);
    }

    /// <summary>
    /// Guruh a'zolari so'rovi.
    ///
    /// KURATOR guruhi uchun a'zolar BOG'LANGAN ustoz guruhlaridan yig'iladi
    /// (kurator guruhida o'zining a'zosi bo'lmaydi) — eski tizimda bu havola
    /// hisobga olinmagani uchun kurator darsida ro'yxat bo'sh chiqardi.
    /// </summary>
    private IQueryable<GroupMember> MembersOf(Group group) =>
        group.IsCuratorGroup
            ? db.GroupMembers.AsNoTracking().Where(m => m.Group!.CuratorGroupId == group.Id)
            : db.GroupMembers.AsNoTracking().Where(m => m.GroupId == group.Id);

    /// <summary>
    /// Majburiy sababni tekshiradi va tozalaydi (2026-08-17).
    ///
    /// ★ BITTA JOYDA: chiqarish, muzlatish va ko'chirish — uchalasi ham
    /// sabab talab qiladi. Uch joyda takrorlansa, chegara yoki xato matni
    /// biriga qo'shilib, ikkinchisiga qo'shilmay qolardi.
    /// </summary>
    private static string RequireReason(string? value, string field, string emptyMessage)
    {
        var reason = (value ?? string.Empty).Trim();

        if (reason.Length == 0)
            throw Invalid(field, emptyMessage);

        if (reason.Length > GroupMember.MaxReasonLength)
            throw Invalid(field, $"Sabab {GroupMember.MaxReasonLength} belgidan oshmasin.");

        return reason;
    }

    // ---------------------------------------------------------------- a'zolik tarixi (2026-08-17)

    /// <summary>
    /// A'zolik hodisasini O'CHMAYDIGAN jurnalga yozadi.
    ///
    /// ★ <c>SaveChanges</c> BU YERDA CHAQIRILMAYDI: hodisa a'zolikning
    /// o'zgarishi bilan BITTA tranzaksiyada saqlanishi shart. Aks holda
    /// "o'quvchi chiqarildi, lekin tarixda yo'q" yarim holati paydo
    /// bo'lardi — aynan shu narsa eski yechimning asosiy nuqsoni edi.
    /// </summary>
    private async Task RecordMembershipEventAsync(
        GroupMember member,
        Group group,
        MembershipEventKind kind,
        string? reason,
        long? movedToGroupId,
        long actorId,
        CancellationToken ct,
        long? reasonId = null)
    {
        var lessons = await CountCompletedLessonsAsync(member.StudentId, ct);

        db.GroupMembershipEvents.Add(GroupMembershipEvent.Create(
            member.StudentId,
            group.Id,
            // Ustoz SURATGA olinadi: `Group.TeacherId` keyinroq almashishi
            // mumkin va o'shanda eski to'kilish yangi ustozga yozilib qolardi.
            group.TeacherId,
            kind,
            reason,
            movedToGroupId,
            actorId,
            lessons,
            clock.GetUtcNow(),
            reasonId));
    }

    /// <summary>
    /// Sabab tasnifi haqiqatan mavjud va FAOL ekanini tekshiradi (2026-08-18).
    ///
    /// ★ NEGA KERAK: bo'lmagan yoki arxivlangan tasnif yozilsa, hisobotdagi
    /// foizlar jimgina "Belgilanmagan" ga qo'shilib ketardi va buni hech
    /// kim sezmasdi.
    /// </summary>
    private async Task EnsureReasonUsableAsync(long? reasonId, CancellationToken ct)
    {
        if (reasonId is not { } id) return;

        var reason = await db.AttritionReasons.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.Label, r.IsActive })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(AttritionReason), id);

        if (!reason.IsActive)
            throw new ConflictException($"\"{reason.Label}\" sababi arxivlangan.");
    }

    /// <summary>
    /// O'quvchi nechta darsni HAQIQATAN o'tagan — "probniy" hisobining
    /// asosi (chegara <c>GroupMembershipEvent.TrialLessonCount</c>).
    ///
    /// ⚠️ 2026-08-18 DA QOIDA O'ZGARDI: ilgari bu yerda "a'zolikka
    /// qo'shilgandan keyingi yakunlangan darslar" sanalardi va u
    /// <c>GroupMember.JoinedAt</c> ga tayanardi — o'quvchi guruhga qayta
    /// qo'shilganda yoki ko'chirilganda esa u sana BUGUNGA tushib,
    /// tajribasi jimgina nolga qaytardi. Endi manba — DAVOMAT
    /// (<see cref="IStudiedLessonCounter"/>), qoidaning o'zi esa o'sha
    /// portning izohida BITTA joyda tushuntirilgan.
    /// </summary>
    private Task<int> CountCompletedLessonsAsync(long studentId, CancellationToken ct) =>
        studiedLessons.CountAsync(studentId, ct);

    private void SetPausedUntil(GroupMember member, DateOnly? value) =>
        db.GroupMembers
            .Entry(member)
            .Property<DateOnly?>(GroupMemberFields.PausedUntil)
            .CurrentValue = value;

    /// <summary>
    /// Bitta a'zolik yozuvi — a'zolikni O'ZGARTIRGAN metodlar javobi uchun.
    /// Ularning hammasi <c>EnsureCanManage</c> dan o'tadi (o'quv bo'limi /
    /// admin), shuning uchun kontakt kesilmaydi.
    /// </summary>
    private async Task<GroupMemberDto> GetMemberDtoAsync(long memberId, CancellationToken ct) =>
        await ProjectMembers(
                db.GroupMembers.AsNoTracking().Where(m => m.Id == memberId),
                withContact: true)
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(GroupMember), memberId);

    /// <param name="withContact">
    /// 🔴 <c>false</c> — email va telefon <c>null</c> bo'lib qaytadi (ustoz).
    ///
    /// ★ TERNAR AYNAN PROYEKSIYA ICHIDA, natijani keyin "tozalash" emas:
    /// shu shaklda EF <c>CASE WHEN</c> yasaydi va ustunlar SQL javobiga ham
    /// tushmaydi. Tashqarida tozalansa ma'lumot bazadan chiqib, keyin
    /// tashlab yuborilardi — va kelajakda kimdir "tozalash" qadamini
    /// tushirib qoldirsa, sirg'anish JIMGINA bo'lardi.
    /// </param>
    private static IQueryable<GroupMemberDto> ProjectMembers(
        IQueryable<GroupMember> rows, bool withContact) =>
        rows.Select(m => new GroupMemberDto(
            m.Id,
            m.StudentId,
            m.Student!.FullName,
            withContact ? m.Student.Email : null,
            withContact ? m.Student.Phone : null,
            m.Status,
            m.JoinedAt,
            EF.Property<DateOnly?>(m, GroupMemberFields.PausedUntil),
            m.GroupId,
            m.Group!.Name,
            m.LeftAt,
            m.LeftBy!.FullName,
            m.MovedToGroupId,
            m.MovedToGroup!.Name,
            m.Reason));

    /// <summary>
    /// ========================================================================
    /// QIDIRUV — GURUH NOMI, USTOZ, KURATOR, KURATOR GURUHI VA KURS NOMI
    /// ========================================================================
    ///
    /// R22 (2026-08-13 talabi): *"guruhlar bo'limida qidiruv barcha
    /// parametrlar bo'yicha ishlasin"*.
    ///
    /// ★ "BARCHA PARAMETRLAR" NIMA DEGANI — QABUL QILINGAN O'QILISH:
    /// erkin matn qidiruvi jadvaldagi MATNLI ustunlar bo'yicha ishlaydi.
    /// SONLI/VAQT/ENUM maydonlari (davomiylik, boshlanish soati, hafta
    /// kunlari, holat, tur) bu yerga QO'SHILMADI va ular tuzilgan FILTR
    /// bo'lib qoladi (`Type`, `IsActive` — allaqachon bor, R21a).
    ///
    /// Uch sabab:
    ///   1) NOANIQLIK: "80" deb yozilganda foydalanuvchi 80 daqiqalik
    ///      darsnimi yoki "80-guruh" nomlimi izlayotgani NOMA'LUM, natija
    ///      esa ikkalasini aralashtirib berardi va qaysi ustun mos
    ///      kelganini KO'RSATIB bo'lmasdi;
    ///   2) TIL: hafta kunlari bazada raqam (`DayOfWeek`), UI'da esa
    ///      o'zbekcha ("Du", "Chor"). "Dushanba" bo'yicha matnli qidiruv
    ///      SQL'da o'zbekcha nomni bilishni talab qilardi — ya'ni tarjima
    ///      jadvali bazaga ko'chirilardi;
    ///   3) NAQSH: `UserService.ApplySearch` AYNAN shunday ishlaydi —
    ///      matnli ustunlar (F.I.Sh., email) erkin qidiruvda, telefon esa
    ///      normalizatsiya bilan; sonli filtrlar alohida parametrlarda.
    ///
    /// ⚠️ SO'ROV SHAKLI O'ZGARDI. Ilgari bu bitta ustun ustidagi `LIKE` edi;
    /// endi qatorga 4 ta qo'shimcha shart qo'shiladi, ulardan IKKITASI
    /// korrelyatsion `EXISTS` (ustoz va kurator ismi). Bu ATAYLAB qabul
    /// qilindi: `Groups` KICHIK jadval (yuzlarcha qator, 100 mingta emas),
    /// ya'ni ketma-ket skan bu yerda arzon va `pg_trgm` GIN indeksi hamon
    /// kerak emas. `Users` da esa aksincha — o'sha jadval yuz minglab
    /// qatorga o'sadi va indeks MAJBURIY (`UserService.ApplySearch`).
    ///
    /// 🔴 SHU SABABLI USTOZ VA KURATOR IKKI ALOHIDA `Any(...)` BILAN
    /// IZLANADI, `u.Id == g.TeacherId || u.Id == g.AssistantId` BILAN EMAS.
    /// Bitta `EXISTS` ichidagi `OR` Postgres'ni birlamchi kalit indeksidan
    /// voz kechishga va `Users` ni HAR GURUH QATORI UCHUN ketma-ket
    /// skanerlashga majbur qilishi mumkin. Ikki alohida shartda esa har
    /// biri bitta qatorlik PK qidiruvi bo'lib qoladi — guruhlar soni ×
    /// 2 ta indeks tegishi, ya'ni yuzlarcha arzon murojaat.
    ///
    /// KURS va KURATOR GURUHI nomi navigatsiya orqali olinadi (`JOIN`) —
    /// ular uchun `EXISTS` shart emas, chunki FK bevosita guruh qatorida.
    /// </summary>
    private IQueryable<Group> ApplySearch(IQueryable<Group> rows, string? search)
    {
        var trimmed = search?.Trim();

        if (string.IsNullOrEmpty(trimmed)) return rows;

        if (trimmed.Length < MinSearchLength)
        {
            throw Invalid("search", "Qidiruv uchun kamida "
                + MinSearchLength.ToString(CultureInfo.InvariantCulture) + " belgi kiriting.");
        }

        var term = "%" + Escape(trimmed.ToLowerInvariant()) + "%";

        // `g.Name.ToLower()` .NET satrida ISHLAMAYDI — u ifoda daraxti ichida
        // va EF uni Postgres'ning `lower()` ga aylantiradi.
        // `ToLowerInvariant()` ni EF tarjima QILA OLMAYDI, shuning uchun
        // globalizatsiya analizatori shu blokda ataylab o'chirilgan.
#pragma warning disable CA1304, CA1311
        return rows.Where(g =>
            EF.Functions.Like(g.Name.ToLower(), term)
            || (g.Course != null && EF.Functions.Like(g.Course.Name.ToLower(), term))
            || (g.CuratorGroup != null && EF.Functions.Like(g.CuratorGroup.Name.ToLower(), term))
            || db.Users.Any(u => u.Id == g.TeacherId
                              && EF.Functions.Like(u.FullName.ToLower(), term))
            || db.Users.Any(u => u.Id == g.AssistantId
                              && EF.Functions.Like(u.FullName.ToLower(), term)));
#pragma warning restore CA1304, CA1311
    }

    /// <summary>LIKE metabelgilarini zararsizlantiradi (aks holda '%' butun jadvalni tortadi).</summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);

    private static string RequireName(string? name)
    {
        var value = name?.Trim();

        if (string.IsNullOrEmpty(value))
            throw Invalid(nameof(Group.Name), "Guruh nomi kiritilishi shart.");

        if (value.Length > MaxNameLength)
            throw Invalid(nameof(Group.Name), "Guruh nomi juda uzun.");

        return value;
    }

    /// <summary>
    /// Dars kunlari. SONI tekshirilmaydi — u guruh TURIGA bog'liq va qoida
    /// Domain'da (<c>Group.ValidateScheduleRule</c>). Bu yerda faqat JSON'dan
    /// kelgan qiymatning o'zi haqiqiy <c>DayOfWeek</c> ekani tekshiriladi.
    /// </summary>
    private static IReadOnlyList<DayOfWeek> RequireWeekdays(IReadOnlyList<DayOfWeek>? weekdays)
    {
        if (weekdays is null || weekdays.Count == 0)
            throw Invalid(nameof(Group.Weekdays), "Kamida bitta dars kuni tanlanishi kerak.");

        // `JsonStringEnumConverter` RAQAMni ham qabul qiladi va uni
        // TEKSHIRMAYDI: `[9]` yuborilsa DayOfWeek(9) hosil bo'lardi va
        // generator hech qanday kunga to'g'ri kelmasdan BO'SH jadval qurardi.
        foreach (var day in weekdays)
        {
            if (!Enum.IsDefined(day))
                throw Invalid(nameof(Group.Weekdays), "Dars kuni noto'g'ri (Monday..Sunday kutiladi).");
        }

        return weekdays;
    }

    private static void RequireKnownType(GroupType type)
    {
        if (!Enum.IsDefined(type))
            throw Invalid(nameof(Group.Type), "Guruh turi noto'g'ri (Group, Individual, Curator).");
    }

    private async Task EnsureCourseExistsAsync(long? courseId, CancellationToken ct)
    {
        if (courseId is null) return;

        if (!await db.Courses.AsNoTracking().AnyAsync(c => c.Id == courseId, ct))
            throw new NotFoundException(nameof(Course), courseId);
    }

    /* ===== R21b · KATEGORIYA MAVJUDMI ===== */

    /// <summary>
    /// Kategoriya bazada bormi.
    ///
    /// ★ 404 (400 EMAS) — <see cref="EnsureCourseExistsAsync"/> bilan AYNI
    /// naqsh va AYNI sabab: bu murojaat tanasidagi "yaroqsiz qiymat" emas,
    /// MAVJUD BO'LMAGAN resursga havola. Loyihada guruhning FK'lari uchun
    /// qoida shu (kurs, ustoz, kurator guruhi — hammasi 404).
    ///
    /// ⚠️ FAOLLIK TEKSHIRILMAYDI (ataylab): arxivlangan kategoriya bilan
    /// guruhni SAQLASH mumkin bo'lishi SHART. Aks holda o'quv bo'limi
    /// "IELTS" yorlig'ini arxivlagan zahoti, o'sha yorliqdagi 40 guruhning
    /// birortasini ham tahrirlab bo'lmay qolardi (PUT to'liq almashtirish,
    /// ya'ni forma joriy kategoriyani qaytarib yuboradi va 400 olardi).
    /// Tanlagichdan chiqarib tashlash — UI ning ishi (`isActive=true`
    /// filtri), server esa mavjud bog'lanishni buzmaydi.
    /// </summary>
    private async Task EnsureCategoryExistsAsync(long? categoryId, CancellationToken ct)
    {
        if (categoryId is null) return;

        if (!await db.GroupCategories.AsNoTracking().AnyAsync(c => c.Id == categoryId, ct))
            throw new NotFoundException(nameof(GroupCategory), categoryId);
    }

    /* ===== /R21b ===== */

    /// <summary>
    /// ========================================================================
    /// ★ VIDEO BOSHLANISH DARSI GURUHNING KURSIGA TEGISHLIMI
    /// ========================================================================
    ///
    /// Bu tekshiruv Domain'da BO'LMAYDI: "dars qaysi kursning modulida"
    /// degan faktni faqat baza biladi (dars -> modul -> kurs zanjiri).
    /// Domain esa kurssiz guruhda boshlanish nuqtasi bo'lmasligini
    /// o'zi qo'riqlaydi (<c>Group.ValidateScheduleRule</c>).
    ///
    /// XATO TURI — <see cref="ValidationException"/> (HTTP 400), 409 emas:
    /// bu murojaat TANASIDAGI yaroqsiz qiymat, mavjud ma'lumot bilan
    /// to'qnashuv emas. Sabab `problem.errors["videoStartLessonId"]` da
    /// tushunarli o'zbekcha matn bo'lib qaytadi.
    ///
    /// MAVJUD BO'LMAGAN dars ham 404 emas, AYNI 400 ni oladi: tashqi
    /// kuzatuvchi uchun "yo'q dars" va "begona kursning darsi" bir xil
    /// javob berishi kerak — aks holda javob kodi boshqa kurslarda qanday
    /// dars Id'lari borligini oshkor qilardi.
    /// </summary>
    private async Task EnsureVideoStartLessonAsync(
        long? courseId, long? videoStartLessonId, CancellationToken ct)
    {
        if (videoStartLessonId is not { } lessonId) return;

        if (courseId is null)
        {
            throw Invalid(VideoStartLessonField,
                "Guruhga kurs biriktirilmagan. Video darslar boshlanish nuqtasini "
                + "tanlash uchun avval guruhga kurs biriktiring.");
        }

        var belongs = await db.ModuleLessons.AsNoTracking()
            .AnyAsync(l => l.Id == lessonId && l.Module!.CourseId == courseId, ct);

        if (!belongs)
        {
            throw Invalid(VideoStartLessonField,
                "Tanlangan dars guruhning kursiga tegishli emas. Video darslar "
                + "boshlanish nuqtasi FAQAT shu guruhga biriktirilgan kursning "
                + "darslaridan tanlanadi. Kursni almashtirgan bo'lsangiz, "
                + "boshlanish darsini ham yangi kursdan qaytadan tanlang.");
        }
    }

    /// <summary>
    /// Ustoz va kurator MAVJUD va O'QUVCHI EMAS ekanini tekshiradi
    /// (bitta so'rovda — ikki alohida so'rov shart emas).
    /// </summary>
    private async Task EnsureStaffAsync(long? teacherId, long? assistantId, CancellationToken ct)
    {
        var ids = new List<long>(2);

        if (teacherId is { } teacher) ids.Add(teacher);
        if (assistantId is { } assistant && assistant != teacherId) ids.Add(assistant);

        if (ids.Count == 0) return;

        var found = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Role })
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            var user = found.Find(u => u.Id == id)
                ?? throw new NotFoundException(nameof(User), id);

            // O'quvchini ustoz/kurator qilib qo'yish — ruxsat matritsasini
            // buzadi: u o'z guruhining barcha darslarini boshlay olardi.
            if (user.Role == UserRole.Student)
            {
                throw Invalid(nameof(Group.TeacherId),
                    "Ustoz yoki kurator sifatida 'Student' rolidagi foydalanuvchi biriktirilmaydi.");
            }
        }
    }

    /// <summary>
    /// Kurator guruhiga bog'lanishni tekshiradi.
    ///
    /// Domain o'zini-o'ziga bog'lash va "kurator guruhi kuratorga bog'lanmaydi"
    /// qoidalarini biladi, lekin NISHON guruh haqidagi faktlarni (mavjudmi,
    /// turi qanday, o'zi bog'langanmi) faqat baza biladi — shuning uchun bu
    /// tekshiruv shu yerda.
    /// </summary>
    private async Task EnsureCuratorLinkAsync(Group group, long? curatorGroupId, CancellationToken ct)
    {
        if (curatorGroupId is null) return;

        if (curatorGroupId == group.Id && group.Id != 0)
            throw new ConflictException("Guruh o'zini o'ziga bog'lay olmaydi.");

        var target = await db.Groups.AsNoTracking()
            .Where(g => g.Id == curatorGroupId)
            .Select(g => new { g.Id, g.Type, g.CuratorGroupId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Group), curatorGroupId);

        if (target.Type != GroupType.Curator)
            throw new ConflictException("Faqat KURATOR turidagi guruhga bog'lash mumkin.");

        // ZANJIR TAQIQI: A -> B -> C bo'lsa "kimning o'quvchisi kim" degan
        // savol bir ma'noli bo'lmay qoladi va davomat rekursiv hisoblash
        // talab qilardi. Bir pog'onalik bog'lanish — qat'iy qoida.
        if (target.CuratorGroupId is not null)
        {
            throw new ConflictException(
                "Zanjir bog'lanish taqiqlanadi: tanlangan kurator guruhi o'zi "
                + "boshqa kurator guruhiga bog'langan.");
        }
    }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// R33 + R40 — «TANLANGAN O'RINDIQ BO'SHMI» TEKSHIRUVI
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// 🔴 NIMA UCHUN: <c>TeacherId</c> ham, <c>AssistantId</c> ham NULL
    /// bo'lishi mumkin. "Savollarga kurator javob bersin" deb qo'yilgan,
    /// lekin kuratori yo'q guruhda o'quvchi HECH KIMGA yoza olmasdi va
    /// ekranda sababsiz "kurator biriktirilmagan" ko'rinardi — o'quv
    /// bo'limi esa sozlamani SAQLAGAN deb o'ylab yurardi.
    ///
    /// ★ TEKSHIRUV BILVOSITA YO'LNI HAM HISOBGA OLADI: o'rindiqda odam
    /// bo'lishi shart emas — u BOG'LANGAN kurator guruhidan kelishi ham
    /// mumkin (<c>StaffResponsibility</c> dagi qoidaning aynan o'zi).
    /// Faqat to'g'ridan-to'g'ri maydonlar tekshirilsa, kurator guruhi
    /// orqali to'g'ri sozlangan guruh 400 olardi.
    ///
    /// ★ QO'SHIMCHA SO'ROV FAQAT KERAK BO'LGANDA: to'g'ridan-to'g'ri
    /// o'rindiq to'la bo'lsa bazaga umuman borilmaydi (odatiy hol).
    /// </summary>
    private async Task EnsureResponsibleSeatsFilledAsync(Group group, CancellationToken ct)
    {
        long? curatorGroupTeacherId = null;
        long? curatorGroupAssistantId = null;

        if (group.CuratorGroupId is { } curatorGroupId
            && (group.TeacherId is null || group.AssistantId is null))
        {
            var linked = await db.Groups.AsNoTracking()
                .Where(g => g.Id == curatorGroupId)
                .Select(g => new { g.TeacherId, g.AssistantId })
                .FirstOrDefaultAsync(ct);

            curatorGroupTeacherId = linked?.TeacherId;
            curatorGroupAssistantId = linked?.AssistantId;
        }

        var hasTeacher = group.TeacherId is not null || curatorGroupTeacherId is not null;
        var hasAssistant = group.AssistantId is not null || curatorGroupAssistantId is not null;

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        Check(
            group.AssignmentGraderRole,
            nameof(UpdateGroupRequest.AssignmentGraderRole),
            "tekshiruvchi");

        /*
          🔴 SAVOLGA JAVOB BERUVCHI TEKSHIRILMAYDI — VA BU ATAYLAB.

          Ikki ustun bir xil ko'rinadi, lekin ularning "xodim yo'q" holati
          BUTUNLAY BOSHQACHA tugaydi:

            • TEKSHIRUVCHI yo'q  → topshirilgan ish OSILIB QOLADI. Hech kim
              baho qo'ya olmaydi va o'quvchi kutib o'tiraveradi. Shuning
              uchun yuqoridagi `Check` o'z o'rnida.

            • SAVOLGA JAVOB BERUVCHI yo'q → o'quvchida shaxsiy suhbatdosh
              bo'lmaydi. Bu BUGUNGI XULQNING O'ZI: `CuratorDirectory
              .ResolveCuratorAsync` faqat `AssistantId` ga qaraydi va
              kurator biriktirilmagan guruhda hozir ham `null` qaytaradi —
              o'quvchi "chat yo'q" holatini ko'radi. Hech narsa yo'qolmaydi.

          ★ NEGA BU XATO EDI: ustun standarti `Assistant` (bugungi
          yo'naltirishni saqlash uchun — to'g'ri qaror). Lekin uni bu yerda
          tekshirish KURATORSIZ GURUH YARATISHNI butunlay to'sib qo'ydi:
          foydalanuvchi bu qiymatni TANLAMAGAN ham edi, u standart bo'lib
          kelgan. Integratsion testlarda 100 dan ortiq yiqilish shundan.
          Yuqoridagi `Both` uchun yozilgan izoh AYNI shu xavfni nazarda
          tutgan — standart qiymat uni chetlab o'tgan.

          ★ Standartni `Both` ga o'zgartirish YECHIM EMAS: u holda savollar
          ustozga ham yo'naltirilardi va bu o'quvchilar uchun ko'rinadigan
          xulq o'zgarishi bo'lardi (bugun ustoz shaxsiy savol olmaydi).
        */

        if (errors.Count > 0) throw new ValidationException(errors);

        void Check(GroupStaffRole role, string field, string what)
        {
            var missing = role switch
            {
                GroupStaffRole.Teacher => !hasTeacher,
                GroupStaffRole.Assistant => !hasAssistant,

                // `Both` — kamida bittasi bo'lsa yetadi. Ikkalasi ham
                // bo'sh guruh esa mavjud holat (hali xodim biriktirilmagan)
                // va uni bu yerda taqiqlash guruh YARATISHNI to'sib
                // qo'yardi — shtat odatda keyinroq biriktiriladi.
                _ => false,
            };

            if (!missing) return;

            errors[field] =
            [
                role == GroupStaffRole.Teacher
                    ? $"Guruhga ustoz biriktirilmagan — uni {what} qilib tanlab bo'lmaydi."
                    : $"Guruhga kurator biriktirilmagan — uni {what} qilib tanlab bo'lmaydi.",
            ];
        }
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
                "Yozuv boshqa so'rov bilan to'qnashdi (takroriy a'zolik yoki xona nomi). "
                + "Qaytadan urinib ko'ring.");
        }
    }

    private async Task<GroupDto> GetDtoAsync(long id, CancellationToken ct)
    {
        var row = await Project(db.Groups.AsNoTracking().Where(g => g.Id == id))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Group), id);

        return Map(row);
    }

    /// <summary>
    /// Guruh -> ustunlar to'plami. Nomlar (kurs, ustoz, kurator) va sanoqlar
    /// BAZADA hisoblanadi — aks holda ro'yxatning har qatori uchun alohida
    /// so'rov ketardi (N+1).
    /// </summary>
    private IQueryable<Projection> Project(IQueryable<Group> rows) =>
        rows.Select(g => new Projection(
            g.Id,
            g.Name,
            g.Type,
            g.CourseId,
            g.Course == null ? null : g.Course.Name,

            // R21b · KATEGORIYA. Navigatsiya orqali (`LEFT JOIN`) — kursdagi
            // bilan AYNI naqsh, ya'ni ro'yxatning har qatori uchun alohida
            // so'rov ketmaydi.
            g.CategoryId,
            g.Category == null ? null : g.Category.Name,

            // VIDEO BOSHLANISH NUQTASI. Nomlar ichki `SELECT` bilan olinadi —
            // navigatsiya property'si ataylab yo'q (sabab: `GroupConfiguration`).
            // UI ikkisini "3-modul · 2-dars" ko'rinishida birga ko'rsatadi,
            // shuning uchun modul nomi ham AYNI so'rovda keladi (N+1 yo'q).
            g.VideoStartLessonId,
            db.ModuleLessons.Where(l => l.Id == g.VideoStartLessonId)
                .Select(l => l.Name).FirstOrDefault(),
            db.ModuleLessons.Where(l => l.Id == g.VideoStartLessonId)
                .Select(l => l.Module!.Name).FirstOrDefault(),

            g.TeacherId,
            db.Users.Where(u => u.Id == g.TeacherId).Select(u => u.FullName).FirstOrDefault(),
            g.AssistantId,
            db.Users.Where(u => u.Id == g.AssistantId).Select(u => u.FullName).FirstOrDefault(),
            g.CuratorGroupId,
            g.CuratorGroup == null ? null : g.CuratorGroup.Name,
            g.StartDate,
            g.CourseMonths,
            g.Weekdays,
            g.StartTime,
            g.DurationMinutes,
            g.IsActive,
            g.RecordEnabled,
            g.RecordingsVisibleToStudents,
            g.RecordingPipeline,

            // R33 + R40 — guruh ustunlari (izohi `Group` entity'sida).
            g.AssignmentGraderRole,
            g.QuestionResponderRole,

            // KURATOR guruhida a'zolar bevosita yo'q — ular bog'langan ustoz
            // guruhlaridan sanaladi. Ikki shart bitta ifodada: oddiy guruhda
            // ikkinchi shart hech qachon rost bo'lmaydi va aksincha.
            db.GroupMembers.Count(m => m.Status == MemberStatus.Active
                && (m.GroupId == g.Id
                    || (g.Type == GroupType.Curator && m.Group!.CuratorGroupId == g.Id))),

            // ArchivedCount — ko'chirilgan (Moved) + muzlatilgan (Paused) +
            // chiqarilgan (Stopped). Hisoblash doirasi yuqoridagi faol
            // a'zolar soni bilan bir xil (kurator guruhida bog'langan ustoz
            // guruhidan sanaladi).
            db.GroupMembers.Count(m => m.Status != MemberStatus.Active
                && (m.GroupId == g.Id
                    || (g.Type == GroupType.Curator && m.Group!.CuratorGroupId == g.Id))),

            db.LiveSessions.Count(s => s.GroupId == g.Id && s.Status != SessionStatus.Cancelled),

            // ★ BAYRAM KALENDARI (2026-08-16): `EndDate` endi HAQIQIY oxirgi
            // (bekor qilinmagan) darsdan olinadi, `StartDate.AddMonths(...)`
            // FORMULASIDAN emas — chunki bayram tufayli jadval bu formuladan
            // uzoqroqqa siljigan bo'lishi mumkin (`Group.EndDate` izohi).
            // `Max` bo'sh to'plamda `null` qaytaradi (istisno EMAS) — bu
            // "hali dars generatsiya qilinmagan" holatini tabiiy belgilaydi,
            // `Map` da formulaga ZAXIRA sifatida ishlatiladi.
            db.LiveSessions
                .Where(s => s.GroupId == g.Id && s.Status != SessionStatus.Cancelled)
                .Max(s => (DateTimeOffset?)s.ScheduledStart),

            g.CreatedAt,
            g.UpdatedAt));

    private GroupDto Map(Projection p) => new(
        p.Id,
        p.Name,
        p.Type,
        p.CourseId,
        p.CourseName,

        // ★ R21b · ATAYLAB NOMLI ARGUMENT (qolganlari pozitsion).
        //
        // `GroupDto` — POZITSION record va bu yerda ketma-ket TO'RTTA
        // `long? / string?` juftligi turibdi (kurs, kategoriya, video dars).
        // Yangi juftlikni bir pozitsiya adashtirib qo'yish KOMPILYATSIYA
        // XATOSI bermasdi — turlar mos tushardi va kategoriya nomi kurs
        // ustuniga jimgina o'tib ketardi. Nomli argument buni
        // KOMPILYATOR tekshiradigan holatga aylantiradi.
        CategoryId: p.CategoryId,
        CategoryName: p.CategoryName,

        p.VideoStartLessonId,
        p.VideoStartLessonName,
        p.VideoStartModuleName,
        p.TeacherId,
        p.TeacherName,
        p.AssistantId,
        p.AssistantName,
        p.CuratorGroupId,
        p.CuratorGroupName,
        p.StartDate,
        // ★ BAYRAM KALENDARI (2026-08-16): ILGARI `StartDate.AddMonths
        // (CourseMonths)` FORMULASI edi — bayram tufayli jadval bundan
        // uzoqroqqa siljigan bo'lishi mumkin (`Group.EndDate` izohi, R21b
        // dagi "aynan bir xil hisob" izohi endi FAQAT `ScheduleGenerator`
        // ning ICHKI "nechta dars kerak" hisobiga tegishli, bu yerga emas).
        // Haqiqiy oxirgi (bekor qilinmagan) darsning mahalliy sanasi
        // olinadi; hali dars generatsiya qilinmagan bo'lsa (`LastSessionStart
        // == null`) — formulaga ZAXIRA sifatida qaytiladi.
        p.LastSessionStart is { } last
            ? LocalWallClock.LocalDate(last, timeZone.TimeZone)
            : p.StartDate.AddMonths(p.CourseMonths),
        p.CourseMonths,
        p.Weekdays,
        p.StartTime,
        p.DurationMinutes,
        p.IsActive,
        p.RecordEnabled,
        p.RecordingsVisibleToStudents,
        p.RecordingPipeline,

        // ★ NOMLI ARGUMENT — `CategoryId` dagi bilan AYNI sabab: bu yerda
        // ketma-ket IKKI `GroupStaffRole` turadi va ularni almashtirib
        // qo'yish KOMPILYATSIYA XATOSI bermasdi. Natijada baholash
        // sozlamasi savollarga, savollar sozlamasi baholashga o'tib
        // ketardi — va buni faqat foydalanuvchi sezardi.
        AssignmentGraderRole: p.AssignmentGraderRole,
        QuestionResponderRole: p.QuestionResponderRole,

        p.MemberCount,
        p.ArchivedCount,
        p.SessionCount,
        p.CreatedAt,
        p.UpdatedAt);

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    // ---------------------------------------------------------------- doimiylar va ichki turlar

    private const int MaxPageSize = 100;
    private const int MinSearchLength = 2;
    private const int MaxNameLength = 150;

    /// <summary>
    /// `problem.errors` kaliti — JSON maydon nomi bilan AYNAN bir xil
    /// (camelCase), aks holda frontend xatoni maydon yoniga qo'ya olmasdi.
    /// </summary>
    private const string VideoStartLessonField = "videoStartLessonId";

    private const string InPlaceReason =
        "Jadval qoidasi o'zgarmadi — mavjud darslar O'RNIDA tahrirlandi. "
        + "Dars Id'lari, LiveKit xona nomlari, davomat va chat saqlandi.";

    private const string UntouchedReason =
        "Jadvalga ta'sir qiluvchi maydon o'zgarmadi — jadvalga tegilmadi.";

    private const string NothingToUpdateReason =
        "Jadval qoidasi o'zgarmadi; yangilanishi kerak bo'lgan kelajak dars topilmadi.";

    /// <summary>
    /// Ro'yxat so'rovi uchun ustunlar to'plami (`EndDate` xotirada
    /// hisoblanadi — `LastSessionStart` dan yoki formuladan, `Map` izohi).
    /// </summary>
    private sealed record Projection(
        long Id,
        string Name,
        GroupType Type,
        long? CourseId,
        string? CourseName,
        long? CategoryId,
        string? CategoryName,
        long? VideoStartLessonId,
        string? VideoStartLessonName,
        string? VideoStartModuleName,
        long? TeacherId,
        string? TeacherName,
        long? AssistantId,
        string? AssistantName,
        long? CuratorGroupId,
        string? CuratorGroupName,
        DateOnly StartDate,
        int CourseMonths,
        List<DayOfWeek> Weekdays,
        TimeOnly StartTime,
        int DurationMinutes,
        bool IsActive,
        bool RecordEnabled,
        bool RecordingsVisibleToStudents,
        RecordingPipeline RecordingPipeline,
        GroupStaffRole AssignmentGraderRole,
        GroupStaffRole QuestionResponderRole,
        int MemberCount,
        int ArchivedCount,
        int SessionCount,
        DateTimeOffset? LastSessionStart,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record CandidateProjection(
        long Id,
        string Name,
        long? AssistantId,
        string? AssistantName,
        long? CourseId,
        string? CourseName,
        List<DayOfWeek> Weekdays,
        TimeOnly StartTime,
        int LinkedGroupCount);
}
