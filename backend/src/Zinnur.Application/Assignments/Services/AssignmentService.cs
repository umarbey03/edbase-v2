using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Assignments.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Gating.Dtos;
using Zinnur.Application.Gating.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Assignments.Services;

/// <summary>
/// Uy vazifalari use-case'lari. HTTP haqida HECH NARSA bilmaydi.
///
/// Biznes qoidalari Domain'da va bu yerda TAKRORLANMAYDI:
///   • <c>Assignment.Validate()</c>            — nishon, sarlavha, ball;
///   • <c>Assignment.EnsureFormatAllowed()</c> — javob shakli ruxsat etilganmi;
///   • <c>Assignment.IsOverdue()</c>           — muddat o'tganmi;
///   • <c>Submission.Create()/Resubmit()</c>   — bir marta topshirish qoidasi;
///   • <c>Submission.Grade()/ReopenForResubmit()</c> — baholash.
///
/// Servis faqat FAKT topadi (bazadan), RUXSAT tekshiradi, natijani yozadi va
/// gating keshini bekor qiladi.
/// </summary>
public sealed class AssignmentService(
    IApplicationDbContext db,
    IGatingService gating,
    ISubmissionStorage storage,
    TimeProvider clock) : IAssignmentService
{
    // ================================================================= o'qish (xodim)

    public async Task<PagedResult<AssignmentDto>> ListAsync(
        AssignmentListQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureNotStudent(actor);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = db.Assignments.AsNoTracking();

        // USTOZ/KURATOR: o'z guruhlarining vazifalari + BARCHA kurs vazifalari
        // (kurs vazifasi hamma guruhga taalluqli va uni ustoz ham baholaydi).
        if (!CanManageEverything(actor))
        {
            var staffGroups = StaffGroupIds(actor.Id);

            rows = rows.Where(a => a.ModuleLessonId != null
                                || (a.GroupId != null && staffGroups.Contains(a.GroupId.Value)));
        }

        if (query.GroupId is { } groupId)
            rows = rows.Where(a => a.GroupId == groupId);

        if (query.ModuleLessonId is { } lessonId)
            rows = rows.Where(a => a.ModuleLessonId == lessonId);

        var total = await rows.CountAsync(ct);

        var items = await Project(rows
                .OrderByDescending(a => a.CreatedAt)
                .ThenBy(a => a.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(ct);

        return new PagedResult<AssignmentDto>(items, page, pageSize, total);
    }

    public async Task<AssignmentDto> GetAsync(long id, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);

        var assignment = await db.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Assignment), id);

        // O'QUVCHI ham ko'radi, LEKIN faqat o'ziga tegishlisini — aks holda
        // begona guruhning vazifa matni oshkor bo'lardi.
        if (actor.Role == UserRole.Student)
            await EnsureVisibleToStudentAsync(assignment, actor.Id, ct);
        else
            await EnsureCanReadAsync(actor, assignment, ct);

        return await Project(db.Assignments.AsNoTracking().Where(a => a.Id == id)).FirstAsync(ct);
    }

    // ================================================================= o'qish (o'quvchi)

    public async Task<IReadOnlyList<StudentAssignmentDto>> ListMineAsync(
        long studentId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(studentId, ct);

        if (actor.Role != UserRole.Student)
            throw new ForbiddenException("Bu ro'yxat faqat o'quvchi uchun.");

        var groupIds = await StudentGroupIdsAsync(studentId, ct);

        // GATING BIR MARTA (keshdan yoki bitta so'rovda) — har vazifa uchun
        // alohida EMAS. Eski tizimda har vazifa uchun butun daraxt qayta
        // qurilardi.
        var gate = await gating.GetCourseGateAsync(studentId, ct);

        var unlockedLessons = gate.Lessons
            .Where(l => l.Unlocked)
            .Select(l => l.LessonId)
            .ToHashSet();

        var courseLessonIds = gate.Lessons.Select(l => l.LessonId).ToList();

        // Entity'lar (proyeksiya emas) — shu tufayli `IsOverdue()` kabi Domain
        // metodlari BEVOSITA ishlatiladi va qoida takrorlanmaydi.
        var assignments = await db.Assignments
            .AsNoTracking()
            .Include(a => a.Group)
            .Include(a => a.ModuleLesson)
            .Where(a => (a.GroupId != null && groupIds.Contains(a.GroupId.Value))
                     || (a.ModuleLessonId != null && courseLessonIds.Contains(a.ModuleLessonId.Value)))
            .OrderBy(a => a.DueAt == null)
            .ThenBy(a => a.DueAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct);

        var assignmentIds = assignments.ConvertAll(a => a.Id);

        // Javoblar ALOHIDA so'rovda: fayllar kolleksiyasi ichma-ich
        // proyeksiyada emas, ya'ni SQL sodda va bir marta bajariladi.
        var submissions = await ProjectSubmissions(db.Submissions
                .AsNoTracking()
                .Where(s => s.StudentId == studentId && assignmentIds.Contains(s.AssignmentId)))
            .ToListAsync(ct);

        var byAssignment = submissions.ToDictionary(s => s.AssignmentId);
        var now = clock.GetUtcNow();

        return assignments.ConvertAll(a =>
        {
            // GURUH vazifasi kurs sur'atiga bog'lanmagan — u DOIM ochiq.
            var unlocked = a.ModuleLessonId is not { } lessonId || unlockedLessons.Contains(lessonId);

            var submission = byAssignment.GetValueOrDefault(a.Id);

            // "Topshirsa bo'ladimi" — INTERFEYS uchun maslahat, himoya EMAS:
            // haqiqiy qaror `SubmitAsync` ichida qayta tekshiriladi.
            var canSubmit = unlocked && (submission is null || submission.AllowResubmit);

            return new StudentAssignmentDto(
                a.Id,
                a.GroupId,
                a.Group?.Name,
                a.ModuleLessonId,
                a.ModuleLesson?.Name,
                a.Title,
                a.Description,
                a.MaxScore,
                a.DueAt,
                a.AllowedFormats,
                a.ImageKey,
                a.IsOverdue(now),
                unlocked,
                canSubmit,
                submission is null ? null : MapStudent(submission));
        });
    }

    // ================================================================= yaratish / tahrirlash

    public async Task<AssignmentDto> CreateAsync(
        CreateAssignmentRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);

        // RUXSAT NISHONGA BOG'LIQ:
        //   • KURS vazifasi (dars) — faqat o'quv bo'limi/admin: u BARCHA
        //     guruhlarga taalluqli, ya'ni bitta ustoz butun platformaga
        //     vazifa berib qo'ymasligi kerak;
        //   • GURUH vazifasi — o'z guruhiga ustoz/kurator ham beradi.
        await EnsureCanCreateAsync(actor, request.GroupId, request.ModuleLessonId, ct);
        await EnsureTargetExistsAsync(request.GroupId, request.ModuleLessonId, ct);

        var assignment = new Assignment
        {
            GroupId = request.GroupId,
            ModuleLessonId = request.ModuleLessonId,
            Title = request.Title?.Trim() ?? string.Empty,
            Description = Normalize(request.Description),
            MaxScore = request.MaxScore,
            DueAt = request.DueAt,
            AllowedFormats = request.AllowedFormats,
            ImageKey = Normalize(request.ImageKey),
            CreatedById = actor.Id,
        };

        // Domain qoidasi: sarlavha, ball, formatlar va "YOKI guruh, YOKI dars".
        // Buzilsa DomainException -> HTTP 409. Bazada ham `CHECK` bor.
        assignment.Validate();

        db.Assignments.Add(assignment);
        await db.SaveChangesAsync(ct);

        return await Project(db.Assignments.AsNoTracking().Where(a => a.Id == assignment.Id))
            .FirstAsync(ct);
    }

    public async Task<AssignmentDto> UpdateAsync(
        long id, UpdateAssignmentRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct);

        var assignment = await db.Assignments.AsTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Assignment), id);

        await EnsureCanWriteAsync(actor, assignment, ct);

        // NISHON (guruh / dars) O'ZGARTIRILMAYDI: topshirilgan javoblar
        // begona vazifaga tegib qolardi va baholar aralashardi. Boshqa nishon
        // kerak bo'lsa — yangi vazifa.
        assignment.Title = request.Title?.Trim() ?? string.Empty;
        assignment.Description = Normalize(request.Description);
        assignment.MaxScore = request.MaxScore;
        assignment.DueAt = request.DueAt;
        assignment.AllowedFormats = request.AllowedFormats;
        assignment.ImageKey = Normalize(request.ImageKey);

        assignment.Validate();

        await db.SaveChangesAsync(ct);

        return await Project(db.Assignments.AsNoTracking().Where(a => a.Id == id)).FirstAsync(ct);
    }

    public async Task DeleteAsync(long id, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);

        // O'CHIRISH faqat o'quv bo'limi/admin — ustoz emas.
        if (!CanManageEverything(actor))
        {
            throw new ForbiddenException(
                "Vazifani faqat o'quv bo'limi xodimi yoki administrator o'chira oladi.");
        }

        var assignment = await db.Assignments.AsTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Assignment), id);

        // TOPSHIRILGAN JAVOB BO'LSA O'CHIRILMAYDI.
        //
        // FK `Cascade` bo'lgani uchun bazada o'chirish TEXNIK jihatdan mumkin,
        // lekin u bilan birga baholar, izohlar va yuklangan fayl havolalari
        // ham ketardi — qaytarib bo'lmaydigan yo'qotish.
        if (await db.Submissions.AsNoTracking().AnyAsync(s => s.AssignmentId == id, ct))
        {
            throw new ConflictException(
                "Bu vazifaga javoblar topshirilgan — o'chirib bo'lmaydi. "
                + "Baholar va yuklangan fayllar bilan birga yo'qolardi. "
                + "Muddatini o'zgartiring yoki yangi vazifa yarating.");
        }

        db.Assignments.Remove(assignment);
        await db.SaveChangesAsync(ct);
    }

    // ================================================================= topshirish (o'quvchi)

    public async Task<StudentSubmissionDto> SubmitAsync(
        long assignmentId,
        string? text,
        IReadOnlyList<IncomingFile> files,
        long studentId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        var actor = await LoadActorAsync(studentId, ct);

        if (actor.Role != UserRole.Student)
            throw new ForbiddenException("Vazifani faqat o'quvchi topshiradi.");

        var assignment = await db.Assignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assignmentId, ct)
            ?? throw new NotFoundException(nameof(Assignment), assignmentId);

        if (assignment.ModuleLessonId is { } lessonId)
        {
            // KURS vazifasi. BITTA ARZON tekshiruv ikkala savolga ham javob
            // beradi: "bu dars mening kursimdami" va "dars ochiqmi" — chunki
            // begona kursning darsi gating uchun ham `NotInCourse` bo'ladi.
            //
            // Ataylab shunday: alohida "ko'rinadimi" tekshiruvi butun kurs
            // daraxtini qurishga majbur qilardi va bitta javob topshirish
            // yana o'nlab so'rovga aylanardi (eski tizimning muammosi).
            await gating.EnsureLessonUnlockedAsync(studentId, lessonId, ct);
        }
        else
        {
            await EnsureVisibleToStudentAsync(assignment, studentId, ct);
        }

        // ---- FAYLLAR: hajm OQIM DAVOMIDA, tur MAZMUNDAN ----
        var attachments = await SubmissionAttachmentReader.ReadAllAsync(
            files, Submission.MaxAttachments, ct);

        var hasText = !string.IsNullOrWhiteSpace(text);

        if (!hasText && attachments.Count == 0)
            throw Invalid("answer", "Javob bo'sh: matn yozing yoki fayl yuklang.");

        // Domain: bu vazifa aynan shu SHAKLDAGI javobni qabul qiladimi.
        assignment.EnsureFormatAllowed(
            SubmissionAttachmentReader.DescribeFormats(hasText, attachments));

        // OMBOR SOZLANMAGAN BO'LSA -> 503. LOKAL DISKKA YOZILMAYDI: eski
        // tizim aynan shunday qilgani uchun fayllar bitta konteynerga
        // bog'lanib qolgan edi va deploy'da yo'qolardi.
        if (attachments.Count > 0 && !storage.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori (R2/S3) sozlanmagan — hozir fayl qabul qilinmaydi. "
                + "Matnli javob yuborishingiz mumkin. Administrator uchun: "
                + "`Storage:ServiceUrl`, `Storage:Bucket`, `Storage:AccessKey`, "
                + "`Storage:SecretKey` to'ldirilishi kerak.");
        }

        var now = clock.GetUtcNow();

        // MUDDAT: javob RAD ETILMAYDI, lekin KECH deb belgilanadi
        // (`Submission.IsLate` — "baholashda hisobga olinadi"). Domain shu
        // maydonni ataylab beradi: o'quvchi kech bo'lsa ham ishini
        // topshirishi kerak, ustoz esa kechikkanini KO'RIB turadi.
        // TESTDA esa aksincha — muddat QAT'IY to'sadi
        // (`Test.EnsureOpenForSubmission`), chunki test — o'lchov.
        var isLate = assignment.IsOverdue(now);

        var submission = await db.Submissions
            .AsTracking()
            .Include(s => s.Files)
            .FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.StudentId == studentId, ct);

        if (submission is null)
        {
            // BIRINCHI topshirish — ALOHIDA metod (Domain izohi: "topshirilganmi"
            // biznes holati "bazada bormi" saqlash holatidan ajratilgan).
            submission = Submission.Create(assignmentId, studentId, text, isLate, now);
            db.Submissions.Add(submission);
        }
        else
        {
            // QAYTA topshirish — faqat kurator ruxsat bergan bo'lsa. Ruxsatni
            // topshirilgach Domain'ning O'ZI yopadi (cheksiz yuborish yo'q).
            submission.Resubmit(text, isLate, now);

            // Eski fayllar o'rniga yangilari keladi: javob BUTUNLAY
            // almashtiriladi, aks holda birinchi urinishning rasmi ikkinchi
            // javobga qo'shilib ketardi.
            foreach (var old in submission.Files.ToList())
                db.SubmissionFiles.Remove(old);
        }

        foreach (var attachment in attachments)
        {
            // Omborga yozilgach faqat KALIT qaytadi — to'liq URL emas.
            var objectKey = await storage.SaveAsync(
                new SubmissionUpload(
                    studentId, attachment.Kind, attachment.Extension,
                    attachment.ContentType, attachment.Content),
                ct);

            submission.Files.Add(new SubmissionFile
            {
                ObjectKey = objectKey,
                Kind = attachment.Kind,
                SizeBytes = attachment.Content.Length,
                ContentType = attachment.ContentType,
            });
        }

        await SaveWithUniqueGuardAsync(ct);

        // Vazifa topshirildi -> gating keshi YAROQSIZ (keyingi dars ochilgan
        // bo'lishi mumkin).
        await gating.InvalidateAsync(studentId, ct);

        return MapStudent(await LoadSubmissionRowAsync(submission.Id, ct));
    }

    // ================================================================= baholash (xodim)

    public async Task<IReadOnlyList<SubmissionDto>> ListSubmissionsAsync(
        long assignmentId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);

        var assignment = await db.Assignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assignmentId, ct)
            ?? throw new NotFoundException(nameof(Assignment), assignmentId);

        await EnsureCanReadAsync(actor, assignment, ct);

        var rows = db.Submissions.AsNoTracking().Where(s => s.AssignmentId == assignmentId);

        // KURS vazifasi barcha guruhlarga taalluqli — ustoz/kurator faqat
        // O'Z o'quvchilarining javoblarini ko'radi (begona guruhning javobi
        // va bahosi oshkor bo'lmasin).
        if (!CanManageEverything(actor))
        {
            var myStudents = StudentIdsOfStaff(actor.Id);
            rows = rows.Where(s => myStudents.Contains(s.StudentId));
        }

        var list = await ProjectSubmissions(rows
                .OrderBy(s => s.Status)
                .ThenBy(s => s.SubmittedAt)
                .ThenBy(s => s.Id))
            .ToListAsync(ct);

        return list.ConvertAll(Map);
    }

    public async Task<SubmissionDto> GradeAsync(
        long submissionId, GradeSubmissionRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (submission, assignment) = await LoadSubmissionForStaffAsync(submissionId, actorId, ct);

        // Domain: 0..MaxScore oralig'i va izoh uzunligi shu yerda tekshiriladi.
        submission.Grade(
            request.Score, assignment.MaxScore, request.Feedback, actorId, clock.GetUtcNow());

        await SaveWithConcurrencyGuardAsync(ct);

        return Map(await LoadSubmissionRowAsync(submissionId, ct));
    }

    public async Task<SubmissionDto> ReopenAsync(
        long submissionId, ReopenSubmissionRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (submission, _) = await LoadSubmissionForStaffAsync(submissionId, actorId, ct);

        // Baho TOZALANMAYDI — tarix qoladi. Yangi javob kelganda
        // `Resubmit()` uni o'zi bekor qiladi.
        submission.ReopenForResubmit(request.Note, clock.GetUtcNow());

        await SaveWithConcurrencyGuardAsync(ct);

        return Map(await LoadSubmissionRowAsync(submissionId, ct));
    }

    // ================================================================= RUXSAT QOIDASI

    /// <summary>
    /// ================================================================
    /// VAZIFALARNI BOSHQARISHNING YAGONA RUXSAT QOIDASI
    /// ================================================================
    /// Controller'dagi <c>[Authorize(Roles=...)]</c> faqat DARVOZA
    /// ("umuman kira oladimi"). Haqiqiy qoida shu yerda, chunki "ustoz FAQAT
    /// O'Z guruhiga" degan shartni atribut bilan ifodalash mumkin emas.
    ///
    /// | Amal                     | Admin/Academic | Teacher/Assistant        |
    /// |--------------------------|----------------|--------------------------|
    /// | Kurs vazifasi yaratish   | ✔              | ✘ (barcha guruhga tegadi)|
    /// | Guruh vazifasi yaratish  | ✔              | ✔ faqat O'Z guruhiga     |
    /// | Tahrirlash               | ✔              | ✔ faqat O'Z guruh vazifasi|
    /// | O'chirish                | ✔              | ✘                        |
    /// | Javoblarni ko'rish       | ✔ hammasi      | ✔ faqat O'Z o'quvchilari |
    /// | Baholash / qayta ochish  | ✔              | ✔ faqat O'Z o'quvchisini |
    ///
    /// Admin/Academic baholashdan CHETLATILMAGAN (ro'yxatdagi "Teacher/Assistant"
    /// dan ko'proq): o'quv bo'limi ustozning xatosini tuzatishi kerak, aks
    /// holda noto'g'ri baho butun tizimda tuzatilmas bo'lib qolardi.
    /// </summary>
    private static bool CanManageEverything(User actor) =>
        actor.Role is UserRole.Admin or UserRole.Academic;

    private static bool IsStaff(User actor) =>
        actor.Role is UserRole.Teacher or UserRole.Assistant;

    private static void EnsureNotStudent(User actor)
    {
        if (actor.Role == UserRole.Student)
            throw new ForbiddenException("Bu ro'yxatga ruxsatingiz yo'q.");
    }

    private async Task EnsureCanCreateAsync(
        User actor, long? groupId, long? moduleLessonId, CancellationToken ct)
    {
        if (CanManageEverything(actor)) return;

        if (!IsStaff(actor))
            throw new ForbiddenException("Vazifa yaratishga ruxsatingiz yo'q.");

        if (moduleLessonId is not null)
        {
            throw new ForbiddenException(
                "KURS vazifasini faqat o'quv bo'limi biriktiradi — u barcha "
                + "guruhlarga taalluqli. O'z guruhingizga vazifa berish uchun "
                + "`groupId` ni ko'rsating.");
        }

        if (groupId is not { } id || !await IsStaffOfGroupAsync(actor.Id, id, ct))
            throw new ForbiddenException("Faqat o'z guruhingizga vazifa bera olasiz.");
    }

    /// <summary>O'QISH ruxsati (vazifa kartochkasi, javoblar ro'yxati).</summary>
    private async Task EnsureCanReadAsync(User actor, Assignment assignment, CancellationToken ct)
    {
        if (CanManageEverything(actor)) return;

        if (!IsStaff(actor))
            throw new ForbiddenException("Bu vazifaga ruxsatingiz yo'q.");

        // Kurs vazifasini har bir ustoz KO'RADI (o'z o'quvchilarini baholash
        // uchun kerak), lekin TAHRIRLAY olmaydi.
        if (assignment.ModuleLessonId is not null) return;

        if (assignment.GroupId is { } groupId && await IsStaffOfGroupAsync(actor.Id, groupId, ct))
            return;

        throw new ForbiddenException("Bu vazifa sizning guruhingizga tegishli emas.");
    }

    /// <summary>YOZISH ruxsati (tahrirlash) — o'qishdan qat'iyroq.</summary>
    private async Task EnsureCanWriteAsync(User actor, Assignment assignment, CancellationToken ct)
    {
        if (CanManageEverything(actor)) return;

        if (!IsStaff(actor))
            throw new ForbiddenException("Vazifani tahrirlashga ruxsatingiz yo'q.");

        if (assignment.ModuleLessonId is not null)
            throw new ForbiddenException("Kurs vazifasini faqat o'quv bo'limi tahrirlaydi.");

        if (assignment.GroupId is { } groupId && await IsStaffOfGroupAsync(actor.Id, groupId, ct))
            return;

        throw new ForbiddenException("Bu vazifa sizning guruhingizga tegishli emas.");
    }

    /// <summary>
    /// Baholash uchun javobni yuklaydi. USTOZ/KURATOR faqat O'Z o'quvchisini
    /// baholaydi — bu KURS vazifasida ham ishlaydi (vazifa umumiy, lekin
    /// o'quvchi aniq bir guruhga tegishli).
    /// </summary>
    private async Task<(Submission Submission, Assignment Assignment)> LoadSubmissionForStaffAsync(
        long submissionId, long actorId, CancellationToken ct)
    {
        var actor = await LoadActorAsync(actorId, ct);

        var submission = await db.Submissions
            .AsTracking()
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw new NotFoundException(nameof(Submission), submissionId);

        var assignment = await db.Assignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == submission.AssignmentId, ct)
            ?? throw new NotFoundException(nameof(Assignment), submission.AssignmentId);

        if (CanManageEverything(actor)) return (submission, assignment);

        if (!IsStaff(actor))
            throw new ForbiddenException("Baholashga ruxsatingiz yo'q.");

        var mine = await StudentIdsOfStaff(actor.Id).ContainsAsync(submission.StudentId, ct);

        return mine
            ? (submission, assignment)
            : throw new ForbiddenException("Bu o'quvchi sizning guruhingizda emas.");
    }

    /// <summary>
    /// Xodim (ustoz/kurator) mas'ul bo'lgan o'quvchilar — BITTA ifoda, ikki
    /// joyda ishlatiladi (javoblar filtri va baholash tekshiruvi), shuning
    /// uchun ular hech qachon ajralib ketmaydi.
    ///
    /// KURATOR ham hisobga olinadi: kurator darsida BOG'LANGAN ustoz
    /// guruhlarining o'quvchilari qatnashadi. Eski tizimda bu havola
    /// hisobga olinmagani uchun (B-8a) kurator o'z o'quvchisining javobini
    /// ko'ra ham, baholay ham olmasdi.
    ///
    /// `IQueryable` qaytaradi — chaqiruvchi uni ichma-ich so'rov sifatida
    /// ishlatadi (`WHERE ... IN (SELECT ...)`), ya'ni ID'lar ilovaga
    /// tortilmaydi.
    /// </summary>
    private IQueryable<long> StudentIdsOfStaff(long staffId) =>
        db.GroupMembers
            .AsNoTracking()
            .Where(m => m.Status == MemberStatus.Active
                     && (m.Group!.TeacherId == staffId
                      || m.Group.AssistantId == staffId
                      || (m.Group.CuratorGroup != null
                          && (m.Group.CuratorGroup.TeacherId == staffId
                           || m.Group.CuratorGroup.AssistantId == staffId))))
            .Select(m => m.StudentId);

    /// <summary>Xodim mas'ul bo'lgan guruhlar (ichma-ich so'rov sifatida).</summary>
    private IQueryable<long> StaffGroupIds(long staffId) =>
        db.Groups
            .AsNoTracking()
            .Where(g => g.TeacherId == staffId
                     || g.AssistantId == staffId
                     || (g.CuratorGroup != null
                         && (g.CuratorGroup.TeacherId == staffId
                          || g.CuratorGroup.AssistantId == staffId)))
            .Select(g => g.Id);

    private async Task<bool> IsStaffOfGroupAsync(long staffId, long groupId, CancellationToken ct) =>
        await StaffGroupIds(staffId).ContainsAsync(groupId, ct);

    /// <summary>
    /// Vazifa o'quvchiga TEGISHLIMI: guruh vazifasi bo'lsa — o'z guruhi,
    /// kurs vazifasi bo'lsa — o'z kursining darsi.
    /// </summary>
    private async Task EnsureVisibleToStudentAsync(
        Assignment assignment, long studentId, CancellationToken ct)
    {
        if (assignment.GroupId is { } groupId)
        {
            var groupIds = await StudentGroupIdsAsync(studentId, ct);

            if (!groupIds.Contains(groupId))
                throw new ForbiddenException("Bu vazifa sizning guruhingizga berilmagan.");

            return;
        }

        if (assignment.ModuleLessonId is { } lessonId)
        {
            // ARZON yo'l: butun daraxt EMAS, faqat shu dars
            // (`NotInCourse` = dars o'quvchining kursida yo'q).
            var gate = await gating.GetLessonGateAsync(studentId, lessonId, ct);

            if (gate.LockReason == LessonLockReason.NotInCourse)
                throw new ForbiddenException("Bu vazifa sizning kursingizga tegishli emas.");

            return;
        }

        // Bu holatga tushish MUMKIN EMAS: `CK_Assignments_GroupXorLesson`
        // bazada, `Assignment.Validate()` esa kodda kafolatlaydi.
        throw new ForbiddenException("Vazifa nishoni noto'g'ri.");
    }

    /// <summary>
    /// O'quvchi KO'RA oladigan guruhlar: o'z guruhlari + ular BOG'LANGAN
    /// kurator guruhlari (kurator o'z darsiga vazifa bersa u kurator guruhiga
    /// biriktiriladi).
    /// </summary>
    private async Task<List<long>> StudentGroupIdsAsync(long studentId, CancellationToken ct)
    {
        var rows = await db.GroupMembers
            .AsNoTracking()
            .Where(m => m.StudentId == studentId && m.Status == MemberStatus.Active)
            .Select(m => new { m.GroupId, m.Group!.CuratorGroupId })
            .ToListAsync(ct);

        var ids = new List<long>(rows.Count * 2);

        foreach (var row in rows)
        {
            ids.Add(row.GroupId);

            if (row.CuratorGroupId is { } curatorGroupId)
                ids.Add(curatorGroupId);
        }

        return ids;
    }

    // ================================================================= ichki yordamchi

    private async Task<User> LoadActorAsync(long actorId, CancellationToken ct)
    {
        // Rol TOKEN'dan emas, BAZADAN: kirish tokeni 15 daqiqa yashaydi,
        // shuning uchun o'chirilgan yoki roli pasaytirilgan xodim eski token
        // bilan amal bajara olmasligi kerak.
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return actor;
    }

    private async Task EnsureTargetExistsAsync(
        long? groupId, long? moduleLessonId, CancellationToken ct)
    {
        if (groupId is { } group
            && !await db.Groups.AsNoTracking().AnyAsync(g => g.Id == group, ct))
        {
            throw new NotFoundException(nameof(Group), group);
        }

        if (moduleLessonId is { } lesson
            && !await db.ModuleLessons.AsNoTracking().AnyAsync(l => l.Id == lesson, ct))
        {
            throw new NotFoundException(nameof(ModuleLesson), lesson);
        }
    }

    private async Task<SubmissionRow> LoadSubmissionRowAsync(long submissionId, CancellationToken ct) =>
        await ProjectSubmissions(db.Submissions.AsNoTracking().Where(s => s.Id == submissionId))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(Submission), submissionId);

    /// <summary>Unikal indeks buzilishini tushunarli 409 ga aylantiradi.</summary>
    private async Task SaveWithUniqueGuardAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConcurrentEdit();
        }
        catch (DbUpdateException)
        {
            // `UX_Submissions_AssignmentId_StudentId` — ikkita parallel
            // "birinchi topshirish" so'rovidan biri shu yerga tushadi.
            throw new ConflictException(
                "Javobingiz ayni damda boshqa so'rov bilan yozildi. Sahifani yangilang.");
        }
    }

    private async Task SaveWithConcurrencyGuardAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConcurrentEdit();
        }
    }

    /// <summary>
    /// `xmin` optimistik qulfi ushlagan to'qnashuv: yozuvni boshqa so'rov
    /// allaqachon o'zgartirgan (masalan ustoz baho qo'yayotganda o'quvchi
    /// qayta topshirdi).
    /// </summary>
    private static ConflictException ConcurrentEdit() =>
        new("Bu javob ayni damda o'zgartirildi. Sahifani yangilab, qaytadan urinib ko'ring.");

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    // ---------------------------------------------------------------- proyeksiya

    /// <summary>
    /// Vazifa -> DTO. Sanoqlar BAZADA hisoblanadi — aks holda ro'yxatning har
    /// qatori uchun alohida so'rov ketardi (N+1).
    /// </summary>
    private IQueryable<AssignmentDto> Project(IQueryable<Assignment> rows) =>
        rows.Select(a => new AssignmentDto(
            a.Id,
            a.GroupId,
            a.Group == null ? null : a.Group.Name,
            a.ModuleLessonId,
            a.ModuleLesson == null ? null : a.ModuleLesson.Name,
            a.Title,
            a.Description,
            a.MaxScore,
            a.DueAt,
            a.AllowedFormats,
            a.ImageKey,
            a.CreatedById,
            db.Submissions.Count(s => s.AssignmentId == a.Id),
            db.Submissions.Count(s => s.AssignmentId == a.Id && s.Status == SubmissionStatus.Graded),
            a.CreatedAt,
            a.UpdatedAt));

    private static IQueryable<SubmissionRow> ProjectSubmissions(IQueryable<Submission> rows) =>
        rows.Select(s => new SubmissionRow(
            s.Id,
            s.AssignmentId,
            s.StudentId,
            s.Student!.FullName,
            s.Text,
            s.Status,
            s.Score,
            s.Feedback,
            s.GradedById,
            s.GradedAt,
            s.SubmittedAt,
            s.AttemptNumber,
            s.AllowResubmit,
            s.ResubmitNote,
            s.IsLate,
            s.Assignment!.MaxScore,
            s.Files
                .OrderBy(f => f.Id)
                .Select(f => new SubmissionFileDto(
                    f.Id, f.ObjectKey, f.Kind, f.SizeBytes, f.ContentType))
                .ToList()));

    /// <summary>
    /// Foizni DOMAIN hisoblaydi (<c>Submission.ScorePercent</c>) — formula bu
    /// yerda takrorlanmaydi, aks holda ikki joyda boshqacha yaxlitlanardi.
    /// </summary>
    private static decimal? Percent(SubmissionRow row) =>
        new Submission { Score = row.Score }.ScorePercent(row.MaxScore);

    private static SubmissionDto Map(SubmissionRow row) => new(
        row.Id, row.AssignmentId, row.StudentId, row.StudentName, row.Text, row.Status,
        row.Score, Percent(row), row.Feedback, row.GradedById, row.GradedAt, row.SubmittedAt,
        row.AttemptNumber, row.AllowResubmit, row.ResubmitNote, row.IsLate, row.Files);

    private static StudentSubmissionDto MapStudent(SubmissionRow row) => new(
        row.Id, row.Status, row.Text, row.Score, Percent(row), row.Feedback, row.SubmittedAt,
        row.AttemptNumber, row.AllowResubmit, row.ResubmitNote, row.IsLate, row.Files);

    // ---------------------------------------------------------------- doimiylar va ichki turlar

    private const int MaxPageSize = 100;

    /// <summary>Javob + baholash uchun kerakli ustunlar (vazifaning `MaxScore` bilan).</summary>
    private sealed record SubmissionRow(
        long Id,
        long AssignmentId,
        long StudentId,
        string StudentName,
        string? Text,
        SubmissionStatus Status,
        decimal? Score,
        string? Feedback,
        long? GradedById,
        DateTimeOffset? GradedAt,
        DateTimeOffset SubmittedAt,
        int AttemptNumber,
        bool AllowResubmit,
        string? ResubmitNote,
        bool IsLate,
        decimal MaxScore,
        List<SubmissionFileDto> Files);
}
