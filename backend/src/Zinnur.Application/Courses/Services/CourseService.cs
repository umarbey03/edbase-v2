using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Courses.Dtos;
using Zinnur.Application.Gating.Dtos;
using Zinnur.Application.Gating.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Courses.Services;

/// <summary>
/// ========================================================================
/// KURS KONTENTI (kurs -> modul -> dars) — CRUD va TARTIB
/// ========================================================================
///
/// HTTP haqida HECH NARSA bilmaydi — faqat Application/Domain xatolarini
/// ko'taradi. Bu modulning ikkita nozik joyi bor va ikkalasi ham
/// "jimgina ma'lumot yo'qotish" xavfi bilan bog'liq.
///
/// ── 1) TARTIB (`Position`) ─────────────────────────────────────────────
///
/// `GatingService.OrderedLessons` kurs darslarini AYNAN shu tartibda
/// o'qiydi:
///
///     Modul.Position -> Modul.Id -> Dars.Position -> Dars.Id
///
/// Ya'ni "N-dars" degan tushuncha butunlay SHU tartibga tayanadi: gating
/// "oldingi dars tugatilganmi" degan savolga javob berishda qo'shni
/// elementni aynan shundan oladi. Shuning uchun bu servis uch narsani
/// kafolatlaydi:
///
///   • YARATISHDA tartib raqami oxiriga qo'yiladi (MAX + 1) — Count EMAS.
///     Count bo'lsa, tartib zich bo'lmagan eski ma'lumotda (seed'da
///     `Position = 1` dan boshlanadi) yangi element MAVJUD raqamga
///     tushib qolardi.
///
///   • O'CHIRISHDA qolgan qo'shnilar QAYTA raqamlanadi — "teshik"
///     qolmaydi.
///
///   • REORDER butun ro'yxatni 0,1,2... qilib ZICH va NOYOB qiladi,
///     bitta `SaveChanges` = bitta TRANZAKSIYA ichida. Yarim tartib
///     (bir qismi ko'chgan, qolgani eski) MUMKIN EMAS.
///
/// NIMA UCHUN BAZADA UNIKAL INDEKS YO'Q: `(ModuleId, Position)` ga oddiy
/// unikal indeks qo'yilsa, tartibni almashtirish (A:0,B:1 -> A:1,B:0)
/// IMKONSIZ bo'lardi — EF `UPDATE` larni qatorma-qator yuboradi va
/// oraliq holatda ikki qator bir xil raqamga tushadi. Postgres'da buni
/// faqat `DEFERRABLE` unikal CONSTRAINT hal qiladi, uni esa EF model
/// differ'i qayta o'qiy olmaydi va `has-pending-model-changes` abadiy
/// "o'zgarish bor" deb qolardi. Shuning uchun noyoblik SHU YERDA
/// (yagona yozuv nuqtasi) ta'minlanadi, o'qishda esa `.ThenBy(Id)`
/// yakuniy tiebreaker sifatida turadi — hatto raqamlar to'qnashsa ham
/// tartib BARQAROR qoladi (so'rovdan so'rovga o'zgarmaydi).
///
/// ── 2) O'CHIRISH ───────────────────────────────────────────────────────
///
/// `ModuleLesson` o'chirilsa EF konfiguratsiyasi bo'yicha ZANJIR ketadi:
///
///     ModuleLesson -> Assignment -> Submission (+ fayllar)
///                  -> Test       -> TestAttempt (+ javoblar)
///                  -> LessonProgress
///
/// Ya'ni bitta darsni o'chirish o'quvchilarning TOPSHIRGAN JAVOBLARI va
/// BALLARINI ham o'chiradi — qaytarib bo'lmaydigan yo'qotish. Shuning
/// uchun javob yoki test urinishi bo'lsa 409 qaytadi. Ayni qoida modul
/// va kurs uchun ham: ular ichidagi HAR QANDAY dars tekshiriladi.
///
/// Kurs uchun QO'SHIMCHA to'siq bor: unga biriktirilgan guruh bo'lsa ham
/// 409. Sababi `Group.CourseId` FK'si `SetNull` — kurs o'chsa guruhlar
/// JIMGINA kurssiz qolardi va o'sha guruhdagi HAMMA o'quvchi uchun
/// gating `NotInCourse` bera boshlardi (butun kurs qulflanardi).
/// </summary>
public sealed class CourseService(
    IApplicationDbContext db,
    IGatingService gating) : ICourseService
{
    // ================================================================= o'qish

    public async Task<PagedResult<CourseDto>> ListAsync(
        CourseListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var actor = await LoadActorAsync(actorId, ct);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = db.Courses.AsNoTracking();

        // O'QUVCHI faqat O'Z kursini ko'radi (guruhi orqali).
        //
        // Guruhga kurs biriktirilmagan bo'lsa BO'SH ro'yxat qaytadi, 403 EMAS:
        // "kurs biriktirilmagan" — bu ruxsat xatosi emas, shunchaki hozircha
        // ko'rsatadigan narsa yo'q. 403 bo'lsa frontend xato ekranini
        // ko'rsatardi.
        if (actor.Role == UserRole.Student)
        {
            if (await StudentCourseIdAsync(actor.Id, ct) is not { } mine)
                return new PagedResult<CourseDto>([], page, pageSize, 0);

            rows = rows.Where(c => c.Id == mine);
        }

        if (query.IsActive is { } isActive)
            rows = rows.Where(c => c.IsActive == isActive);

        rows = ApplySearch(rows, query.Search);

        // Ikkita so'rov (COUNT + sahifa) — `Total` bo'lmasa frontend paginator
        // sahifalar sonini bila olmaydi.
        var total = await rows.CountAsync(ct);

        var items = await Project(rows
                .OrderBy(c => c.Position)
                .ThenBy(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(ct);

        return new PagedResult<CourseDto>(items, page, pageSize, total);
    }

    public async Task<CourseTreeDto> GetAsync(long id, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);

        var head = await db.Courses.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CourseHead(
                c.Id, c.Name, c.Description, c.IsActive, c.Position, c.CreatedAt, c.UpdatedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Course), id);

        // Gating xaritasi FAQAT o'quvchi uchun quriladi (xodimga `null`).
        var gates = await StudentGatesAsync(actor, id, ct);

        var modules = await ProjectModules(OrderedModules(id)).ToListAsync(ct);

        return new CourseTreeDto(
            head.Id,
            head.Name,
            head.Description,
            head.IsActive,
            head.Position,
            modules.ConvertAll(m => MapModule(m, gates)),
            head.CreatedAt,
            head.UpdatedAt);
    }

    // ================================================================= kurs: yozish

    public async Task<CourseDto> CreateAsync(
        CreateCourseRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var course = new Course
        {
            Name = RequireName(request.Name, "Kurs nomi"),
            Description = RequireDescription(request.Description),
            IsActive = request.IsActive,
            Position = await NextPositionAsync(db.Courses.AsNoTracking().Select(c => c.Position), ct),
        };

        db.Courses.Add(course);
        await SaveWithGuardAsync(ct);

        return await GetCourseDtoAsync(course.Id, ct);
    }

    public async Task<CourseDto> UpdateAsync(
        long id, UpdateCourseRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var course = await db.Courses.AsTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Course), id);

        course.Name = RequireName(request.Name, "Kurs nomi");
        course.Description = RequireDescription(request.Description);

        // `Position` ATAYLAB tegilmaydi — tartib faqat "reorder" amali orqali.
        course.IsActive = request.IsActive;

        await SaveWithGuardAsync(ct);

        return await GetCourseDtoAsync(id, ct);
    }

    public async Task DeleteAsync(long id, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var course = await db.Courses.AsTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Course), id);

        // ★ GURUHGA BIRIKTIRILGAN kurs o'chirilmaydi (izoh: sinf sarlavhasida).
        var groupCount = await db.Groups.AsNoTracking().CountAsync(g => g.CourseId == id, ct);

        if (groupCount > 0)
        {
            throw new ConflictException(
                "Bu kursga " + groupCount.ToString(CultureInfo.InvariantCulture)
                + " ta guruh biriktirilgan — o'chirib bo'lmaydi. Guruhlar kurssiz qolardi "
                + "va ulardagi barcha o'quvchilar uchun darslar qulflanardi. "
                + "Avval guruhlarni boshqa kursga o'tkazing yoki kursni arxivlang.");
        }

        await EnsureNoStudentWorkAsync(LessonIdsOfCourse(id), "Kursni", ct);

        db.Courses.Remove(course);

        // Qolgan kurslar tartibi ZICH qolsin (o'chirilgan raqam "teshik"
        // qoldirmasin) — o'chirish bilan BITTA tranzaksiyada.
        Reindex(
            await db.Courses.AsTracking()
                .Where(c => c.Id != id)
                .OrderBy(c => c.Position).ThenBy(c => c.Id)
                .ToListAsync(ct),
            c => c.Id,
            (c, position) => c.Position = position);

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PositionDto>> ReorderCoursesAsync(
        ReorderRequest request, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var rows = await db.Courses.AsTracking()
            .OrderBy(c => c.Position).ThenBy(c => c.Id)
            .ToListAsync(ct);

        var result = Reindex(
            ArrangeByRequest(rows, request, c => c.Id, "Kurslar"),
            c => c.Id,
            (c, position) => c.Position = position);

        await db.SaveChangesAsync(ct);
        return result;
    }

    // ================================================================= modul

    public async Task<CourseModuleDto> CreateModuleAsync(
        long courseId, CreateModuleRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);
        await EnsureCourseExistsAsync(courseId, ct);

        var module = new CourseModule
        {
            CourseId = courseId,
            Name = RequireName(request.Name, "Modul nomi"),
            Position = await NextPositionAsync(
                db.Modules.AsNoTracking().Where(m => m.CourseId == courseId).Select(m => m.Position), ct),
        };

        db.Modules.Add(module);
        await SaveWithGuardAsync(ct);

        return await GetModuleDtoAsync(module.Id, ct);
    }

    public async Task<CourseModuleDto> UpdateModuleAsync(
        long courseId, long moduleId, UpdateModuleRequest request, long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var module = await LoadModuleForManageAsync(courseId, moduleId, ct);

        module.Name = RequireName(request.Name, "Modul nomi");

        await SaveWithGuardAsync(ct);

        return await GetModuleDtoAsync(moduleId, ct);
    }

    public async Task DeleteModuleAsync(
        long courseId, long moduleId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var module = await LoadModuleForManageAsync(courseId, moduleId, ct);

        // Modul o'chsa ichidagi HAMMA dars ham o'chadi (Cascade) — shuning
        // uchun tekshiruv modulning BARCHA darslari bo'yicha.
        await EnsureNoStudentWorkAsync(
            db.ModuleLessons.AsNoTracking().Where(l => l.ModuleId == moduleId).Select(l => l.Id),
            "Modulni",
            ct);

        db.Modules.Remove(module);

        Reindex(
            await db.Modules.AsTracking()
                .Where(m => m.CourseId == courseId && m.Id != moduleId)
                .OrderBy(m => m.Position).ThenBy(m => m.Id)
                .ToListAsync(ct),
            m => m.Id,
            (m, position) => m.Position = position);

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PositionDto>> ReorderModulesAsync(
        long courseId, ReorderRequest request, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);
        await EnsureCourseExistsAsync(courseId, ct);

        var rows = await db.Modules.AsTracking()
            .Where(m => m.CourseId == courseId)
            .OrderBy(m => m.Position).ThenBy(m => m.Id)
            .ToListAsync(ct);

        var result = Reindex(
            ArrangeByRequest(rows, request, m => m.Id, "Modullar"),
            m => m.Id,
            (m, position) => m.Position = position);

        // ★ BITTA SaveChanges = BITTA tranzaksiya: barcha modul raqamlari
        //   birgalikda yoziladi. Yarim tartib bo'lsa gating darslarni
        //   noto'g'ri ketma-ketlikda o'qib, noto'g'ri darsni ochib qo'yardi.
        await db.SaveChangesAsync(ct);
        return result;
    }

    // ================================================================= dars

    public async Task<CourseLessonDto> CreateLessonAsync(
        long courseId, long moduleId, CreateLessonRequest request, long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);
        await EnsureModuleExistsAsync(courseId, moduleId, ct);

        var lesson = new ModuleLesson
        {
            ModuleId = moduleId,
            Name = RequireName(request.Name, "Dars nomi"),
            Description = RequireDescription(request.Description),
            DurationMin = RequireDuration(request.DurationMin),
            Position = await NextPositionAsync(
                db.ModuleLessons.AsNoTracking().Where(l => l.ModuleId == moduleId).Select(l => l.Position), ct),
        };

        db.ModuleLessons.Add(lesson);
        await SaveWithGuardAsync(ct);

        return await GetLessonDtoAsync(lesson.Id, ct);
    }

    public async Task<CourseLessonDto> UpdateLessonAsync(
        long courseId, long moduleId, long lessonId, UpdateLessonRequest request, long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var lesson = await LoadLessonForManageAsync(courseId, moduleId, lessonId, ct);

        lesson.Name = RequireName(request.Name, "Dars nomi");
        lesson.Description = RequireDescription(request.Description);
        lesson.DurationMin = RequireDuration(request.DurationMin);

        await SaveWithGuardAsync(ct);

        return await GetLessonDtoAsync(lessonId, ct);
    }

    public async Task DeleteLessonAsync(
        long courseId, long moduleId, long lessonId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);

        var lesson = await LoadLessonForManageAsync(courseId, moduleId, lessonId, ct);

        await EnsureNoStudentWorkAsync(
            db.ModuleLessons.AsNoTracking().Where(l => l.Id == lessonId).Select(l => l.Id),
            "Darsni",
            ct);

        db.ModuleLessons.Remove(lesson);

        Reindex(
            await db.ModuleLessons.AsTracking()
                .Where(l => l.ModuleId == moduleId && l.Id != lessonId)
                .OrderBy(l => l.Position).ThenBy(l => l.Id)
                .ToListAsync(ct),
            l => l.Id,
            (l, position) => l.Position = position);

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PositionDto>> ReorderLessonsAsync(
        long courseId, long moduleId, ReorderRequest request, long actorId,
        CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanManage(actor);
        await EnsureModuleExistsAsync(courseId, moduleId, ct);

        var rows = await db.ModuleLessons.AsTracking()
            .Where(l => l.ModuleId == moduleId)
            .OrderBy(l => l.Position).ThenBy(l => l.Id)
            .ToListAsync(ct);

        var result = Reindex(
            ArrangeByRequest(rows, request, l => l.Id, "Darslar"),
            l => l.Id,
            (l, position) => l.Position = position);

        await db.SaveChangesAsync(ct);
        return result;
    }

    // ================================================================= RUXSAT QOIDASI

    /// <summary>
    /// ================================================================
    /// KURS KONTENTINI O'ZGARTIRISHNING YAGONA RUXSAT QOIDASI
    /// ================================================================
    /// O'zgartiruvchi HAR BIR metod shu tekshiruvdan o'tadi.
    ///
    /// USTOZ VA KURATOR ATAYLAB CHETDA: kurs kontenti BARCHA guruhlarga
    /// tegishli — bitta ustoz darsni o'chirsa yoki tartibini almashtirsa,
    /// bu boshqa o'ntalab guruhning gating ketma-ketligini o'zgartirib
    /// yuborardi. Shuning uchun ular faqat KO'RADI.
    ///
    /// Controller'dagi <c>[Authorize(Roles=...)]</c> faqat DARVOZA — haqiqiy
    /// qoida shu yerda, chunki servis fon vazifasidan yoki SignalR hub'idan
    /// ham chaqirilishi mumkin (o'sha yerda atribut umuman ishlamaydi).
    /// </summary>
    private static void EnsureCanManage(User actor)
    {
        if (actor.Role is not (UserRole.Admin or UserRole.Academic))
        {
            throw new ForbiddenException(
                "Kurs kontentini faqat o'quv bo'limi xodimi yoki administrator o'zgartira oladi. "
                + "Ustoz va kurator uni faqat ko'ra oladi.");
        }
    }

    /// <summary>
    /// O'quvchi uchun gating xaritasi; xodim uchun <c>null</c>
    /// ("hammasi ochiq" degani).
    ///
    /// ★ KURS TANLASH `GatingService.ResolvePaceAsync` BILAN AYNAN BIR XIL
    /// bo'lishi SHART (<see cref="StudentCourseIdAsync"/>). Aks holda
    /// o'quvchiga A kursini ko'rishga ruxsat berilib, gating B kursi
    /// bo'yicha hisoblanardi — natijada BARCHA darslar `NotInCourse`
    /// bo'lib, kurs butunlay qulflanib qolardi.
    /// </summary>
    private async Task<Dictionary<long, LessonGateDto>?> StudentGatesAsync(
        User actor, long courseId, CancellationToken ct)
    {
        if (actor.Role != UserRole.Student) return null;

        if (await StudentCourseIdAsync(actor.Id, ct) != courseId)
            throw new ForbiddenException("Bu kurs sizning guruhingizga biriktirilmagan.");

        var gate = await gating.GetCourseGateAsync(actor.Id, ct);

        var map = new Dictionary<long, LessonGateDto>(gate.Lessons.Count);

        foreach (var lesson in gate.Lessons)
            map[lesson.LessonId] = lesson;

        return map;
    }

    /// <summary>
    /// O'quvchining kursi. Mantiq `GatingService.ResolvePaceAsync` dagi
    /// bilan bir xil: FAOL guruh, FAOL a'zolik, kursi bor, `GroupId`
    /// bo'yicha eng kichigi.
    /// </summary>
    private async Task<long?> StudentCourseIdAsync(long studentId, CancellationToken ct) =>
        await db.GroupMembers.AsNoTracking()
            .Where(m => m.StudentId == studentId
                     && m.Status == MemberStatus.Active
                     && m.Group!.IsActive
                     && m.Group.CourseId != null)
            .OrderBy(m => m.GroupId)
            .Select(m => m.Group!.CourseId)
            .FirstOrDefaultAsync(ct);

    // ================================================================= O'CHIRISH HIMOYASI

    /// <summary>
    /// ★★ O'QUVCHI MEHNATI BOR BO'LSA O'CHIRISHNI TO'XTATADI.
    ///
    /// FK'lar `Cascade` bo'lgani uchun o'chirish TEXNIK jihatdan mumkin,
    /// lekin u bilan birga topshirilgan javoblar, baholar, izohlar, fayl
    /// havolalari va test urinishlari ham ketardi — qaytarib bo'lmaydigan
    /// yo'qotish. `AssignmentService.DeleteAsync` da ham AYNI qoida.
    /// </summary>
    private async Task EnsureNoStudentWorkAsync(
        IQueryable<long> lessonIds, string what, CancellationToken ct)
    {
        var submissions = await db.Submissions.AsNoTracking()
            .CountAsync(s => s.Assignment!.ModuleLessonId != null
                          && lessonIds.Contains(s.Assignment.ModuleLessonId!.Value), ct);

        var attempts = await db.TestAttempts.AsNoTracking()
            .CountAsync(a => a.Test!.ModuleLessonId != null
                          && lessonIds.Contains(a.Test.ModuleLessonId!.Value), ct);

        if (submissions == 0 && attempts == 0) return;

        var counts = string.Create(
            CultureInfo.InvariantCulture,
            $"{submissions} ta topshirilgan vazifa va {attempts} ta test urinishi");

        throw new ConflictException(
            what + " o'chirib bo'lmaydi: unga o'quvchilarning " + counts + " bog'langan. "
            + "Ular baholari va yuklangan fayllari bilan birga YO'QOLARDI. "
            + "O'chirish o'rniga kursni arxivlang (isActive=false) yoki kontentni tahrirlang.");
    }

    /// <summary>Kursdagi BARCHA darslar (modul orqali) — o'chirish tekshiruvi uchun.</summary>
    private IQueryable<long> LessonIdsOfCourse(long courseId) =>
        db.ModuleLessons.AsNoTracking()
            .Where(l => l.Module!.CourseId == courseId)
            .Select(l => l.Id);

    // ================================================================= TARTIB

    /// <summary>
    /// Ro'yxatni 0,1,2... qilib ZICH qayta raqamlaydi va yangi raqamlarni
    /// qaytaradi.
    ///
    /// Kirish ro'yxati ALLAQACHON kerakli tartibda bo'lishi kerak — bu metod
    /// tartiblamaydi, faqat RAQAMLAYDI. Uchala tur (kurs, modul, dars) uchun
    /// bitta ishlanma: raqamlash mantiqi uch joyda takrorlansa, ular vaqt
    /// o'tib bir-biridan ajralib ketardi.
    /// </summary>
    private static List<PositionDto> Reindex<T>(
        IReadOnlyList<T> ordered, Func<T, long> id, Action<T, int> setPosition)
    {
        var result = new List<PositionDto>(ordered.Count);

        for (var index = 0; index < ordered.Count; index++)
        {
            setPosition(ordered[index], index);
            result.Add(new PositionDto(id(ordered[index]), index));
        }

        return result;
    }

    /// <summary>
    /// So'ralgan Id ketma-ketligi bo'yicha qatorlarni saflaydi.
    ///
    /// ★ TO'LIQLIK QAT'IY TEKSHIRILADI: takror Id, yetishmayotgan element
    /// yoki begona Id bo'lsa — 400 va HECH NARSA yozilmaydi.
    ///
    /// NIMA UCHUN SHUNCHALIK QAT'IY: yarim ro'yxat qabul qilinsa, yuborilmagan
    /// elementlarni qayerga qo'yish kerakligi noaniq bo'lardi (boshigami,
    /// oxirigami?) va ikki kurator bir vaqtda tartiblaganda natija
    /// aytib bo'lmaydigan bo'lardi. Gating esa aynan shu tartibga tayanadi.
    /// </summary>
    private static List<T> ArrangeByRequest<T>(
        List<T> rows, ReorderRequest request, Func<T, long> id, string what)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);

        var requested = request.OrderedIds;

        if (requested is null || requested.Count == 0)
            throw Invalid(OrderedIdsField, what + " tartibi uchun ro'yxat bo'sh bo'lmasligi kerak.");

        var seen = new HashSet<long>(requested.Count);

        foreach (var value in requested)
        {
            if (!seen.Add(value))
            {
                throw Invalid(OrderedIdsField,
                    "Ro'yxatda takrorlangan Id bor: " + value.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (requested.Count != rows.Count)
        {
            var mismatch = string.Create(
                CultureInfo.InvariantCulture,
                $"Ro'yxat to'liq emas: {rows.Count} ta element kutilgan edi, {requested.Count} ta keldi.");

            throw Invalid(OrderedIdsField,
                mismatch + " Tartiblashda BARCHA elementlar yuborilishi shart.");
        }

        var arranged = new List<T>(rows.Count);

        foreach (var value in requested)
        {
            var row = rows.Find(candidate => id(candidate) == value)
                ?? throw Invalid(OrderedIdsField,
                    what + " ro'yxatiga tegishli bo'lmagan Id: "
                    + value.ToString(CultureInfo.InvariantCulture));

            arranged.Add(row);
        }

        return arranged;
    }

    /// <summary>
    /// Yangi element uchun tartib raqami — MAVJUD maksimumdan keyingisi.
    ///
    /// `Count` EMAS: eski ma'lumotda tartib zich bo'lmasligi mumkin (seed
    /// `Position = 1` dan boshlaydi), o'shanda `Count` mavjud raqamga
    /// tushib qolardi va ikki element bir xil o'ringa da'vo qilardi.
    /// </summary>
    private static async Task<int> NextPositionAsync(
        IQueryable<int> positions, CancellationToken ct) =>
        (await positions.MaxAsync(position => (int?)position, ct) ?? -1) + 1;

    // ================================================================= proyeksiya

    /// <summary>
    /// Kurs -> ro'yxat qatori. Sanoqlar BAZADA hisoblanadi — aks holda
    /// ro'yxatning har qatori uchun alohida so'rov ketardi (N+1).
    /// </summary>
    private IQueryable<CourseDto> Project(IQueryable<Course> rows) =>
        rows.Select(c => new CourseDto(
            c.Id,
            c.Name,
            c.Description,
            c.IsActive,
            c.Position,
            db.Modules.Count(m => m.CourseId == c.Id),
            db.ModuleLessons.Count(l => l.Module!.CourseId == c.Id),
            db.Groups.Count(g => g.CourseId == c.Id),
            c.CreatedAt,
            c.UpdatedAt));

    /// <summary>
    /// ★ Modullar GATING BILAN AYNI tartibda (`Position` -> `Id`).
    /// `GatingService.OrderedLessons` modullarni `Module.Position` so'ng
    /// `ModuleId` bo'yicha saflaydi — shu yerda ham AYNAN shunday.
    /// </summary>
    private IQueryable<CourseModule> OrderedModules(long courseId) =>
        db.Modules
            .AsNoTracking()
            .Where(m => m.CourseId == courseId)
            .OrderBy(m => m.Position)
            .ThenBy(m => m.Id);

    /// <summary>
    /// Butun daraxt BITTA so'rovda: darslar va ularning vazifa/test faktlari
    /// ichki (correlated) so'rovlar bilan olinadi — modul boshiga alohida
    /// so'rov YO'Q.
    ///
    /// Darslar tartibi ham gating bilan bir xil: `Position` -> `Id`.
    /// </summary>
    private IQueryable<ModuleRow> ProjectModules(IQueryable<CourseModule> rows) =>
        rows.Select(m => new ModuleRow(
            m.Id,
            m.CourseId,
            m.Name,
            m.Position,
            m.Lessons
                .OrderBy(l => l.Position)
                .ThenBy(l => l.Id)
                .Select(l => new LessonRow(
                    l.Id,
                    l.ModuleId,
                    l.Name,
                    l.Description,
                    l.Position,
                    l.DurationMin,
                    db.Assignments.Any(a => a.ModuleLessonId == l.Id),

                    // Faqat E'LON QILINGAN test — `GatingService` ham
                    // aynan shunday sanaydi (qoralama test hech kimga
                    // ko'rinmaydi va hech narsani qulflamaydi).
                    db.Tests.Any(t => t.ModuleLessonId == l.Id && t.IsPublished)))
                .ToList()));

    private static CourseModuleDto MapModule(ModuleRow row, Dictionary<long, LessonGateDto>? gates) =>
        new(row.Id, row.CourseId, row.Name, row.Position,
            row.Lessons.ConvertAll(lesson => MapLesson(lesson, gates)));

    /// <summary>
    /// ★ QULFLANGAN DARS: sarlavha KO'RINADI, mazmun YO'Q.
    ///
    /// O'quvchi kursda nima borligini bilishi kerak (nimaga intilayotganini
    /// ko'rsin), lekin tavsif — bu darsning o'zi. Uni ochiq qoldirish
    /// gating'ning butun ma'nosini yo'qqa chiqarardi.
    /// </summary>
    private static CourseLessonDto MapLesson(LessonRow row, Dictionary<long, LessonGateDto>? gates)
    {
        // Xodim uchun gating YO'Q — u kontentni to'liq ko'radi.
        if (gates is null)
        {
            return new CourseLessonDto(
                row.Id, row.ModuleId, row.Name, row.Description, row.Position, row.DurationMin,
                Unlocked: true, LockReason: null, row.HasAssignment, row.HasTest);
        }

        var found = gates.TryGetValue(row.Id, out var gate);
        var unlocked = found && gate!.Unlocked;

        // Xaritada umuman yo'q dars = gating uni bu o'quvchining kursiga
        // tegishli deb bilmaydi -> YOPIQ (ochilib ketmasin).
        var reason = unlocked
            ? (LessonLockReason?)null
            : found ? gate!.LockReason ?? LessonLockReason.PreviousIncomplete
                    : LessonLockReason.NotInCourse;

        return new CourseLessonDto(
            row.Id,
            row.ModuleId,
            row.Name,
            unlocked ? row.Description : null,
            row.Position,
            row.DurationMin,
            unlocked,
            reason,
            row.HasAssignment,
            row.HasTest);
    }

    // ================================================================= ichki yordamchi

    private async Task<User> LoadActorAsync(long actorId, CancellationToken ct)
    {
        // Rol TOKEN'dan emas, BAZADAN olinadi: kirish tokeni 15 daqiqa
        // yashaydi, shuning uchun endi o'chirilgan yoki roli pasaytirilgan
        // xodim eski token bilan kontentni o'zgartira olmasligi kerak.
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return actor;
    }

    /// <summary>
    /// Modulni TAHRIRLASH uchun yuklaydi.
    ///
    /// ★ `CourseId` ham SHART tekshiriladi: aks holda klient `courses/3`
    /// manzili orqali 7-kursning modulini tahrirlay olardi va URL'dagi
    /// ierarxiya yolg'onga aylanardi.
    /// </summary>
    private async Task<CourseModule> LoadModuleForManageAsync(
        long courseId, long moduleId, CancellationToken ct) =>
        await db.Modules.AsTracking()
            .FirstOrDefaultAsync(m => m.Id == moduleId && m.CourseId == courseId, ct)
        ?? throw new NotFoundException(nameof(CourseModule), moduleId);

    private async Task<ModuleLesson> LoadLessonForManageAsync(
        long courseId, long moduleId, long lessonId, CancellationToken ct) =>
        await db.ModuleLessons.AsTracking()
            .FirstOrDefaultAsync(l => l.Id == lessonId
                                   && l.ModuleId == moduleId
                                   && l.Module!.CourseId == courseId, ct)
        ?? throw new NotFoundException(nameof(ModuleLesson), lessonId);

    private async Task EnsureCourseExistsAsync(long courseId, CancellationToken ct)
    {
        if (!await db.Courses.AsNoTracking().AnyAsync(c => c.Id == courseId, ct))
            throw new NotFoundException(nameof(Course), courseId);
    }

    private async Task EnsureModuleExistsAsync(long courseId, long moduleId, CancellationToken ct)
    {
        if (!await db.Modules.AsNoTracking()
                .AnyAsync(m => m.Id == moduleId && m.CourseId == courseId, ct))
        {
            throw new NotFoundException(nameof(CourseModule), moduleId);
        }
    }

    private async Task<CourseDto> GetCourseDtoAsync(long id, CancellationToken ct) =>
        await Project(db.Courses.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(Course), id);

    /// <summary>Bitta modul (darslari bilan) — xodim uchun, ya'ni gatingsiz.</summary>
    private async Task<CourseModuleDto> GetModuleDtoAsync(long moduleId, CancellationToken ct)
    {
        var row = await ProjectModules(db.Modules.AsNoTracking().Where(m => m.Id == moduleId))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(CourseModule), moduleId);

        return MapModule(row, gates: null);
    }

    private async Task<CourseLessonDto> GetLessonDtoAsync(long lessonId, CancellationToken ct)
    {
        var row = await db.ModuleLessons.AsNoTracking()
            .Where(l => l.Id == lessonId)
            .Select(l => new LessonRow(
                l.Id,
                l.ModuleId,
                l.Name,
                l.Description,
                l.Position,
                l.DurationMin,
                db.Assignments.Any(a => a.ModuleLessonId == l.Id),
                db.Tests.Any(t => t.ModuleLessonId == l.Id && t.IsPublished)))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(ModuleLesson), lessonId);

        return MapLesson(row, gates: null);
    }

    /// <summary>
    /// Kurs nomi bo'yicha qidiruv.
    ///
    /// `Courses` JUDA kichik jadval (o'nlab qator), shuning uchun `pg_trgm`
    /// indeksi qo'yilmagan — ketma-ket skan bu yerda arzon
    /// (`GroupService.ApplySearch` bilan bir xil mulohaza).
    /// </summary>
    private static IQueryable<Course> ApplySearch(IQueryable<Course> rows, string? search)
    {
        var trimmed = search?.Trim();

        if (string.IsNullOrEmpty(trimmed)) return rows;

        if (trimmed.Length < MinSearchLength)
        {
            throw Invalid("search", "Qidiruv uchun kamida "
                + MinSearchLength.ToString(CultureInfo.InvariantCulture) + " belgi kiriting.");
        }

        var term = "%" + Escape(trimmed.ToLowerInvariant()) + "%";

        // `c.Name.ToLower()` .NET satrida ISHLAMAYDI — u ifoda daraxti ichida
        // va EF uni Postgres'ning `lower()` ga aylantiradi.
        // `ToLowerInvariant()` ni EF tarjima QILA OLMAYDI, shuning uchun
        // globalizatsiya analizatori shu blokda ataylab o'chirilgan.
#pragma warning disable CA1304, CA1311
        return rows.Where(c => EF.Functions.Like(c.Name.ToLower(), term));
#pragma warning restore CA1304, CA1311
    }

    /// <summary>LIKE metabelgilarini zararsizlantiradi (aks holda '%' butun jadvalni tortadi).</summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);

    private static string RequireName(string? name, string what)
    {
        var value = name?.Trim();

        if (string.IsNullOrEmpty(value))
            throw Invalid("name", what + " kiritilishi shart.");

        if (value.Length > MaxNameLength)
        {
            throw Invalid("name", what + " juda uzun ("
                + MaxNameLength.ToString(CultureInfo.InvariantCulture) + " belgidan oshmasin).");
        }

        return value;
    }

    /// <summary>Bo'sh tavsif <c>null</c> ga keltiriladi — bazada "" va null aralashmasin.</summary>
    private static string? RequireDescription(string? description)
    {
        var value = description?.Trim();

        if (string.IsNullOrEmpty(value)) return null;

        if (value.Length > MaxDescriptionLength)
        {
            throw Invalid("description", "Tavsif juda uzun ("
                + MaxDescriptionLength.ToString(CultureInfo.InvariantCulture) + " belgidan oshmasin).");
        }

        return value;
    }

    private static int? RequireDuration(int? durationMin)
    {
        if (durationMin is null) return null;

        if (durationMin is <= 0 or > MaxDurationMin)
        {
            throw Invalid("durationMin", "Dars davomiyligi 1 dan "
                + MaxDurationMin.ToString(CultureInfo.InvariantCulture) + " daqiqagacha bo'lishi kerak.");
        }

        return durationMin;
    }

    /// <summary>
    /// Unikal indeks buzilishini tushunarli 409 ga aylantiradi
    /// (`GroupService.SaveWithUniqueGuardAsync` bilan bir xil naqsh).
    /// </summary>
    private async Task SaveWithGuardAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException(
                "Yozuv boshqa so'rov bilan to'qnashdi. Qaytadan urinib ko'ring.");
        }
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    // ---------------------------------------------------------------- doimiylar va ichki turlar

    private const int MaxPageSize = 100;
    private const int MinSearchLength = 2;

    /// <summary>EF konfiguratsiyasidagi chegaralar bilan bir xil bo'lishi shart.</summary>
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 2000;

    /// <summary>10 soat — bu chegara xatoni ushlash uchun, real dars uchun emas.</summary>
    private const int MaxDurationMin = 600;

    private const string OrderedIdsField = "orderedIds";

    private sealed record CourseHead(
        long Id,
        string Name,
        string? Description,
        bool IsActive,
        int Position,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record ModuleRow(
        long Id,
        long CourseId,
        string Name,
        int Position,
        List<LessonRow> Lessons);

    private sealed record LessonRow(
        long Id,
        long ModuleId,
        string Name,
        string? Description,
        int Position,
        int? DurationMin,
        bool HasAssignment,
        bool HasTest);
}
