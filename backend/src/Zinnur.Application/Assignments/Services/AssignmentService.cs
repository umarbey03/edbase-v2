using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Assignments.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.Gating.Dtos;
using Zinnur.Application.Gating.Services;
using Zinnur.Application.Notifications;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Application.Notifications.Services;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Staffing;

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
    TimeProvider clock,
    // ===== R35/R36 · BAHOLASH -> BILDIRISHNOMA =====
    //
    // ★ IKKI ALOHIDA YO'L, ATAYLAB: `outbox` — TELEGRAM (biznes
    //   tranzaksiyasi ichida navbatga yoziladi), `notifier` — brauzerdagi
    //   ochiq sahifa (kommitdan KEYIN). Ular bir-biriga bog'liq emas:
    //   Telegram sozlanmagan bo'lsa ham qo'ng'iroqcha ishlaydi, hub
    //   yiqilsa ham Telegram xabari ketadi.
    INotificationOutbox outbox,
    INotificationNotifier notifier) : IAssignmentService
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
            // ⚠️ `Access` — RO'YXAT ko'rish "kim tekshiradi" ga bog'liq emas
            // (baholash tugmasi javob ekranida, va u o'z darvozasidan o'tadi).
            var staffGroups = StaffGroupIds(actor.Id, StaffDuty.Access);

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

            // WAVE 1: SHART biriktirmalari — o'quvchi shartni to'liq ko'rishi
            // kerak (audio namuna, varaq rasmi). `Include` bitta so'rovda
            // keladi, ya'ni vazifa boshiga alohida so'rov YO'Q.
            .Include(a => a.Attachments)
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

                // 🔴 QULFLANGAN DARSNING vazifasida shart biriktirmalari
                //    BERILMAYDI: `Description` ochiq qolayotgani mavjud
                //    xatti-harakat (uni o'zgartirish qamrovdan tashqarida),
                //    lekin YANGI maydonni ochib qo'yish gating teshigini
                //    KENGAYTIRISH bo'lardi. Fayl oqimi baribir to'silgan
                //    (`AssignmentAttachmentService`), bu — ikkinchi qatlam.
                unlocked ? MapAttachments(a.Attachments) : [],
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

        // R32: RUXSAT NISHONGA BOG'LIQ EMAS — kurs vazifasi ham, guruh
        // vazifasi ham faqat o'quv bo'limi/admin tomonidan yaratiladi.
        // (Ilgari guruh vazifasini ustoz o'z guruhiga bera olardi; sabab va
        // bekor qilinishi — `CanManageEverything` ustidagi izohda.)
        EnsureCanCreate(actor);
        await EnsureTargetExistsAsync(request.GroupId, request.ModuleLessonId, ct);

        RequireAnswerFormats(request.AllowedFormats);

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
            GraderRole = request.GraderRole,
            CreatedById = actor.Id,
        };

        // Domain qoidasi: sarlavha, ball, formatlar va "YOKI guruh, YOKI dars".
        // (R33: kurs vazifasiga tekshiruvchi tayinlanmasligi ham shu yerda.)
        // Buzilsa DomainException -> HTTP 409. Bazada ham `CHECK` bor.
        assignment.Validate();

        await EnsureGraderSeatFilledAsync(assignment, ct);

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

        EnsureCanWrite(actor);

        RequireAnswerFormats(request.AllowedFormats);

        // NISHON (guruh / dars) O'ZGARTIRILMAYDI: topshirilgan javoblar
        // begona vazifaga tegib qolardi va baholar aralashardi. Boshqa nishon
        // kerak bo'lsa — yangi vazifa.
        assignment.Title = request.Title?.Trim() ?? string.Empty;
        assignment.Description = Normalize(request.Description);
        assignment.MaxScore = request.MaxScore;
        assignment.DueAt = request.DueAt;
        assignment.AllowedFormats = request.AllowedFormats;
        assignment.ImageKey = Normalize(request.ImageKey);
        assignment.GraderRole = request.GraderRole;

        assignment.Validate();

        await EnsureGraderSeatFilledAsync(assignment, ct);

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

    // ================================================================= fayl o'qish

    /// <summary>
    /// Javobga ilova qilingan faylni O'QISHGA ochadi.
    ///
    /// ★ SO'ROV OBYEKT KALITI bilan EMAS, FAYL ID'si bilan keladi. Bu ataylab:
    /// kalit chaqiruvchidan qabul qilinsa, u istalgan yo'lni
    /// (<c>../boshqa-bucket/...</c>, begona o'quvchining kaliti) yozib
    /// yuborardi va ruxsat tekshiruvi ma'nosini yo'qotardi. ID esa bazadagi
    /// yozuvga olib boradi va kalit FAQAT bazadan olinadi.
    /// </summary>
    public async Task<SubmissionFileDownload> OpenFileAsync(
        long fileId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);

        // Faylning O'ZI emas, uning EGASI kerak — shuning uchun javob orqali
        // o'quvchiga chiqiladi (bitta so'rov, JOIN bazada).
        var file = await db.SubmissionFiles
            .AsNoTracking()
            .Where(f => f.Id == fileId)
            .Select(f => new
            {
                f.Id,
                f.SubmissionId,
                f.ObjectKey,
                f.Kind,
                f.ContentType,
                OwnerId = f.Submission!.StudentId,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(SubmissionFile), fileId);

        // RUXSAT — OMBORGA MUROJAATDAN OLDIN. Aks holda begona odam
        // "javob qancha kutdi" yoki "503 keldi" kabi belgilardan faylning
        // bor-yo'qligini payqardi.
        await EnsureCanReadStudentWorkAsync(actor, file.OwnerId, ct);

        if (!storage.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori (R2/S3) sozlanmagan — faylni ochib bo'lmaydi. "
                + "Administrator uchun: `Storage:ServiceUrl`, `Storage:Bucket`, "
                + "`Storage:AccessKey`, `Storage:SecretKey` to'ldirilishi kerak.");
        }

        var stored = await storage.OpenReadAsync(file.ObjectKey, ct)
            ?? throw new NotFoundException(nameof(SubmissionFile), fileId);

        // TUR BAZADAN ustun: yuklashda u MAZMUNDAN aniqlangan edi, ombor esa
        // faqat biz yozgan sarlavhani qaytaradi (va u yo'qolgan bo'lishi ham
        // mumkin). Ikkalasi ham bo'lmasa — "noma'lum ikkilik".
        var contentType = Normalize(file.ContentType) ?? stored.ContentType;

        return new SubmissionFileDownload(
            stored,
            contentType,
            SuggestFileName(file.SubmissionId, file.Id, file.ObjectKey, file.Kind));
    }

    /// <summary>
    /// Yuklab olinadigan fayl nomi.
    ///
    /// OBYEKT KALITI NOM SIFATIDA BERILMAYDI: unda ichki tuzilma va
    /// o'quvchi ID'si bor, bu esa foydalanuvchiga keraksiz va omborimiz
    /// tuzilishini oshkor qiladi. Kengaytma esa kalitdan olinadi — u
    /// yuklashda mazmundan aniqlangan.
    /// </summary>
    private static string SuggestFileName(
        long submissionId, long fileId, string objectKey, AttachmentKind kind)
    {
        var extension = Path.GetExtension(objectKey.AsSpan());
        var prefix = kind == AttachmentKind.Audio ? "ovoz" : "rasm";

        return string.Create(
            CultureInfo.InvariantCulture, $"{prefix}-{submissionId}-{fileId}{extension}");
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
        //
        // 🔴 R33: FILTR AYNI `Grading` QOIDASIDAN, `Access` DAN EMAS.
        // Aks holda tekshiruvchi qilib tayinlanmagan xodim javoblarni
        // ro'yxatda KO'RARDI, lekin har bosganda 403 olardi — ya'ni
        // bajarib bo'lmaydigan navbat. Bu eng yomon ko'rinishdagi
        // nomuvofiqlik: ekran ish bor deydi, server esa yo'q deydi.
        if (!CanManageEverything(actor))
        {
            var myStudents = StudentIdsOfStaff(actor.Id, StaffDuty.Grading, assignment.GraderRole);
            rows = rows.Where(s => myStudents.Contains(s.StudentId));
        }

        var list = await ProjectSubmissions(rows
                .OrderBy(s => s.Status)
                .ThenBy(s => s.SubmittedAt)
                .ThenBy(s => s.Id))
            .ToListAsync(ct);

        return list.ConvertAll(Map);
    }

    // ================================================================= o'quv bo'limi umumiy ko'rinishi
    //
    // ★ Ikkalasi ham FAQAT Academic/Admin (`EnsureCanViewOverview`) — ustoz/
    // kurator o'z "Tekshirish" sahifasida yuqoridagi `ListSubmissionsAsync`
    // dan foydalanadi, ya'ni bu yerda staff-scoping (`StaffGroupIds`) YO'Q.

    public async Task<IReadOnlyList<AssignmentGroupOverviewDto>> GetGroupsOverviewAsync(
        AssignmentOverviewFilter filter, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanViewOverview(actor);

        var rows = ApplyOverviewFilter(db.Assignments.AsNoTracking(), filter);

        /*
          ★ AVVAL YASSI QATORLAR, KEYIN GURUHLASH XOTIRADA (LINQ-to-Objects).
          Har vazifaning `SubmissionCount`/`GradedCount` KORRELYATSIYALANGAN
          quyi so'rov bilan bitta SELECT'da keladi — `Project()` dagi AYNI
          naqsh, N+1 EMAS. Postgres tomonida `GROUP BY` yozish esa
          `TeacherName` kabi qo'shimcha quyi so'rovlar bilan chalkash SQL
          berardi; filtrlangan vazifalar soni cheklangan, ya'ni oxirgi
          guruhlash xotirada arzon.
        */
        var flat = await rows
            .Select(a => new
            {
                a.Id,
                a.GroupId,
                GroupName = a.Group == null ? null : a.Group.Name,
                GroupType = a.Group == null ? (GroupType?)null : a.Group.Type,
                TeacherId = a.Group == null ? null : a.Group.TeacherId,
                TeacherName = a.Group == null || a.Group.TeacherId == null
                    ? null
                    : db.Users.Where(u => u.Id == a.Group.TeacherId).Select(u => u.FullName).FirstOrDefault(),
                SubmissionCount = db.Submissions.Count(s => s.AssignmentId == a.Id),
                GradedCount = db.Submissions.Count(
                    s => s.AssignmentId == a.Id && s.Status == SubmissionStatus.Graded),
                LastSubmittedAt = db.Submissions
                    .Where(s => s.AssignmentId == a.Id)
                    .Max(s => (DateTimeOffset?)s.SubmittedAt),
            })
            .ToListAsync(ct);

        return flat
            // `GroupId == null` (KURS vazifalari) bitta sun'iy qatorga yig'iladi.
            .GroupBy(a => a.GroupId)
            .Select(g =>
            {
                var first = g.First();
                return new AssignmentGroupOverviewDto(
                    g.Key,
                    g.Key == null ? "Kurs vazifalari" : first.GroupName ?? "—",
                    g.Key == null ? null : first.GroupType,
                    g.Key == null ? null : first.TeacherId,
                    g.Key == null ? null : first.TeacherName,
                    g.Count(),
                    g.Sum(x => x.SubmissionCount),
                    g.Sum(x => x.GradedCount),
                    g.Sum(x => x.SubmissionCount) - g.Sum(x => x.GradedCount),
                    g.Max(x => x.LastSubmittedAt));
            })
            // ★ ENG KO'P TEKSHIRILMAGANI BIRINCHI: "bir ko'rganda qayerda ish
            // ko'p" degan savolga to'g'ridan-to'g'ri javob.
            .OrderByDescending(x => x.UngradedCount)
            .ThenBy(x => x.GroupName, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<PagedResult<SubmissionOverviewDto>> ListSubmissionsOverviewAsync(
        SubmissionOverviewQuery query, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var actor = await LoadActorAsync(actorId, ct);
        EnsureCanViewOverview(actor);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var rows = db.Submissions.AsNoTracking();

        if (query.AssignmentId is { } assignmentId)
            rows = rows.Where(s => s.AssignmentId == assignmentId);

        if (query.GroupId is { } groupId)
            rows = rows.Where(s => s.Assignment!.GroupId == groupId);

        if (query.TeacherId is { } teacherId)
            rows = rows.Where(s => s.Assignment!.Group != null && s.Assignment.Group.TeacherId == teacherId);

        if (query.GroupType is { } groupType)
            rows = rows.Where(s => s.Assignment!.Group != null && s.Assignment.Group.Type == groupType);

        if (query.Status is { } status)
            rows = rows.Where(s => s.Status == status);

        var term = NormalizeSearch(query.Search);
        if (term is not null)
        {
            // DIQQAT (`UserService.ApplySearch` dagi AYNI izoh): `.ToLower()`
            // .NET satri ustida ishlamaydi, ifoda daraxti ichida — EF uni
            // Postgres `lower()` funksiyasiga aylantiradi.
#pragma warning disable CA1304, CA1311
            rows = rows.Where(s =>
                EF.Functions.Like(s.Student!.FullName.ToLower(), term)
                || EF.Functions.Like(s.Assignment!.Title.ToLower(), term)
                || (s.Assignment.Group != null && EF.Functions.Like(s.Assignment.Group.Name.ToLower(), term)));
#pragma warning restore CA1304, CA1311
        }

        var total = await rows.CountAsync(ct);

        var pageRows = await rows
            .OrderByDescending(s => s.SubmittedAt)
            .ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SubmissionOverviewRow(
                s.Id,
                s.AssignmentId,
                s.Assignment!.Title,
                s.Assignment.GroupId,
                s.Assignment.Group == null ? null : s.Assignment.Group.Name,
                s.Assignment.Group == null ? (GroupType?)null : s.Assignment.Group.Type,
                s.Assignment.Group == null ? null : s.Assignment.Group.TeacherId,
                s.Assignment.Group == null || s.Assignment.Group.TeacherId == null
                    ? null
                    : db.Users.Where(u => u.Id == s.Assignment.Group.TeacherId)
                        .Select(u => u.FullName).FirstOrDefault(),
                s.Assignment.Group == null || s.Assignment.Group.AssistantId == null
                    ? null
                    : db.Users.Where(u => u.Id == s.Assignment.Group.AssistantId)
                        .Select(u => u.FullName).FirstOrDefault(),
                s.Assignment.GraderRole,
                s.Assignment.Group == null ? (GroupStaffRole?)null : s.Assignment.Group.AssignmentGraderRole,
                s.StudentId,
                s.Student!.FullName,
                s.Status,
                s.Score,
                s.Assignment.MaxScore,
                s.SubmittedAt,
                s.IsLate,
                s.AttemptNumber,
                s.GradedAt,
                s.GradedById,
                s.GradedById == null
                    ? null
                    : db.Users.Where(u => u.Id == s.GradedById).Select(u => u.FullName).FirstOrDefault()))
            .ToListAsync(ct);

        return new PagedResult<SubmissionOverviewDto>(pageRows.ConvertAll(MapOverview), page, pageSize, total);
    }

    /// <summary>Guruh/ustoz/tur/qidiruv filtri — ikkala overview so'rovi UMUMIY.</summary>
    private static IQueryable<Assignment> ApplyOverviewFilter(
        IQueryable<Assignment> rows, AssignmentOverviewFilter filter)
    {
        if (filter.GroupId is { } groupId)
            rows = rows.Where(a => a.GroupId == groupId);

        if (filter.TeacherId is { } teacherId)
            rows = rows.Where(a => a.Group != null && a.Group.TeacherId == teacherId);

        if (filter.GroupType is { } groupType)
            rows = rows.Where(a => a.Group != null && a.Group.Type == groupType);

        var term = NormalizeSearch(filter.Search);
        if (term is not null)
        {
#pragma warning disable CA1304, CA1311
            rows = rows.Where(a =>
                EF.Functions.Like(a.Title.ToLower(), term)
                || (a.Group != null && EF.Functions.Like(a.Group.Name.ToLower(), term)));
#pragma warning restore CA1304, CA1311
        }

        return rows;
    }

    /// <summary>`"  Ism  "` -&gt; `"%ism%"`. Bo'sh/berilmagan bo'lsa `null` (filtrlanmaydi).</summary>
    private static string? NormalizeSearch(string? search)
    {
        var trimmed = search?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return "%" + EscapeLike(trimmed.ToLowerInvariant()) + "%";
    }

    /// <summary>`UserService.Escape` bilan AYNI — `LIKE` maxsus belgilarini zararsizlantiradi.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);

    private static void EnsureCanViewOverview(User actor)
    {
        if (CanManageEverything(actor)) return;

        throw new ForbiddenException(
            "Bu umumiy ko'rinishga faqat o'quv bo'limi va admin kira oladi.");
    }

    /// <summary>
    /// "Kim tekshirishi kerak" — <c>Assignment.GraderRole ?? Group.AssignmentGraderRole</c>
    /// ni KO'RSATISH matniga o'giradi. Kurs vazifasida <c>null</c>: u hamma
    /// guruhga taalluqli, ya'ni bitta aniq tekshiruvchi yo'q.
    /// </summary>
    private static string? ResolveGraderLabel(SubmissionOverviewRow row)
    {
        if (row.GroupId is null) return null;

        var role = row.AssignmentGraderRole ?? row.GroupGraderRole ?? GroupStaffRole.Both;

        return role switch
        {
            GroupStaffRole.Teacher => row.TeacherName ?? "Ustoz tayinlanmagan",
            GroupStaffRole.Assistant => row.AssistantName ?? "Kurator tayinlanmagan",
            _ => JoinGraderNames(row.TeacherName, row.AssistantName),
        };
    }

    private static string JoinGraderNames(string? teacherName, string? assistantName)
    {
        if (teacherName is null && assistantName is null) return "Tayinlanmagan";
        if (teacherName is null) return assistantName!;
        if (assistantName is null) return teacherName;
        return $"{teacherName} / {assistantName}";
    }

    private static SubmissionOverviewDto MapOverview(SubmissionOverviewRow row) => new(
        row.Id,
        row.AssignmentId,
        row.AssignmentTitle,
        row.GroupId,
        row.GroupName,
        row.GroupType,
        row.TeacherId,
        row.TeacherName,
        row.StudentId,
        row.StudentName,
        row.Status,
        row.Score,
        row.MaxScore,
        new Submission { Score = row.Score }.ScorePercent(row.MaxScore),
        row.SubmittedAt,
        row.IsLate,
        row.AttemptNumber,
        row.GradedAt,
        row.GradedById,
        row.GradedByName,
        ResolveGraderLabel(row));

    public async Task<SubmissionDto> GradeAsync(
        long submissionId, GradeSubmissionRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (submission, assignment) = await LoadSubmissionForStaffAsync(submissionId, actorId, ct);

        // Domain: 0..MaxScore oralig'i va izoh uzunligi shu yerda tekshiriladi.
        submission.Grade(
            request.Score, assignment.MaxScore, request.Feedback, actorId, clock.GetUtcNow());

        // ═════════════════════════════════════════════════════════════════
        // R35/R36 — BILDIRISHNOMA TAYYORLANADI, LEKIN HALI YUBORILMAYDI.
        //
        // Bu chaqiruv `SaveChanges` DAN OLDIN turishi SHART: u qo'ng'iroqcha
        // qatorini va Telegram navbatidagi qatorni AYNI kuzatuvchiga
        // qo'shadi, ya'ni ular BAHO bilan BITTA tranzaksiyada saqlanadi.
        // Keyin qo'yilsa "xabar ketdi, baho saqlanmadi" holati mumkin
        // bo'lardi (`INotificationOutbox` izohidagi eski tizim xatosi).
        // ═════════════════════════════════════════════════════════════════
        var pending = await PrepareGradedNotificationAsync(submission, assignment, ct);

        await SaveWithConcurrencyGuardAsync(ct);

        // 🔴 REALTIME FAQAT SHU YERDA — KOMMITDAN KEYIN.
        //
        // Yuqoriga ko'chirilsa, tranzaksiya orqaga qaytganda o'quvchining
        // ekranida BAZADA YO'Q baho paydo bo'lardi va u sahifani
        // yangilamaguncha shunday turardi. Istisno esa `notifier` ICHIDA
        // yutiladi — sabab `INotificationNotifier` izohida.
        if (pending is not null)
        {
            await notifier.NotificationCreatedAsync(
                pending.UserId, NotificationFeed.ToDto(pending), ct);
        }

        return Map(await LoadSubmissionRowAsync(submissionId, ct));
    }

    /// <summary>
    /// «Vazifa tekshirildi» bildirishnomasini IKKI yo'lga tayyorlaydi:
    /// qo'ng'iroqcha qatori (baza) va Telegram navbati.
    ///
    /// ★ <c>SaveChanges</c> CHAQIRILMAYDI — chaqiruvchi uni o'z
    /// tranzaksiyasida qiladi (commit-then-send).
    /// </summary>
    /// <returns>
    /// Kuzatuvchiga qo'shilgan qator — kommitdan keyin realtime uchun
    /// kerak (o'shanda uning <c>Id</c> si to'ldirilgan bo'ladi).
    /// </returns>
    private async Task<Notification?> PrepareGradedNotificationAsync(
        Submission submission, Assignment assignment, CancellationToken ct)
    {
        // Baho `Grade()` ichida qo'yilgan, ya'ni bu yerda `null` bo'lishi
        // mumkin emas. Shunday bo'lsa ham — jimgina chiqamiz: bildirishnoma
        // yo'qligi baholashni yiqitadigan sabab emas.
        if (submission.Score is not { } score) return null;

        var now = clock.GetUtcNow();

        // Faqat KERAKLI ikki maydon (butun `User` emas): bu metod
        // baholashning ISSIQ yo'lida turibdi va ustoz 50 ta ishni ketma-ket
        // baholaganda har ortiqcha ustun 50 marta o'qiladi.
        var student = await db.Users.AsNoTracking()
            .Where(u => u.Id == submission.StudentId)
            .Select(u => new { u.TelegramId })
            .FirstOrDefaultAsync(ct);

        // ---------------------------------------------------------------- 1) qo'ng'iroqcha
        //
        // ★ AYNI JAVOB UCHUN O'QILMAGAN ESKI QATOR O'CHIRILADI.
        //
        // Ustoz bahoni tuzatishi odatiy hol (izohdagi xato, noto'g'ri ball).
        // Har tuzatish yangi qator yozsa, o'quvchining qo'ng'iroqchasida BIR
        // vazifa uchun uch-to'rt bir xil yozuv turardi va ularning qaysi
        // biri OXIRGI ekani ko'rinmasdi. O'qilganlari esa TEGILMAYDI — ular
        // tarix.
        //
        // ⚠️ Bu QO'SHIMCHA so'rov, ya'ni baholashning issiq yo'lida narx bor.
        // Narx ongli: so'rov `(UserId, ReadAt, CreatedAt)` indeksidan
        // o'qiladi va o'qilmaganlar soni doim kichik.
        var stale = await db.Notifications
            .Where(n => n.UserId == submission.StudentId
                     && n.Kind == NotificationKind.SubmissionGraded
                     && n.EntityId == submission.Id
                     && n.ReadAt == null)
            .ToListAsync(ct);

        if (stale.Count > 0) db.Notifications.RemoveRange(stale);

        var row = Notification.Create(
            submission.StudentId,
            NotificationKind.SubmissionGraded,
            NotificationTemplates.SubmissionGradedTitle(),
            NotificationTemplates.SubmissionGradedBody(
                assignment.Title, score, assignment.MaxScore, submission.Feedback),
            submission.Id,
            now);

        db.Notifications.Add(row);

        // ---------------------------------------------------------------- 2) Telegram
        //
        // Bog'lanmagan o'quvchiga navbatga yozish MA'NOSIZ: `chat_id` yo'q,
        // ya'ni qator faqat urinishlar chegarasini yeb, `Failed` bo'lardi.
        if (student?.TelegramId is { } chatId)
        {
            await outbox.EnqueueAsync(
                new NotificationRequest
                {
                    Channel = NotificationChannel.Telegram,
                    RecipientUserId = submission.StudentId,
                    RecipientAddress = chatId.ToString(CultureInfo.InvariantCulture),
                    TemplateKey = TelegramTemplates.SubmissionGraded,
                    Body = TelegramTemplates.SubmissionGradedText(
                        assignment.Title, score, assignment.MaxScore, submission.Feedback),

                    // ═══════════════════════════════════════════════════════
                    // 🔴 KALITDA URINISH RAQAMI BO'LISHI SHART.
                    //
                    // `submission_graded:{id}` bo'lsa QAYTA OCHILGAN va qayta
                    // baholangan javob haqida o'quvchi HECH QACHON ikkinchi
                    // xabar olmasdi: birinchi kalit bazada qolgan, ya'ni
                    // ikkinchi navbat yozuvi jimgina rad etilardi. Bu
                    // "himoya ishladi" emas — MA'LUMOT YO'QOLISHI bo'lardi,
                    // chunki qayta baholash HAQIQATAN yangi hodisa.
                    //
                    // ★ AYNI URINISH ichidagi tuzatish esa TAKROR xabar
                    //   yubormaydi va bu to'g'ri: Telegram — turtki
                    //   (intrusive) kanal, bir vazifa uchun uch marta
                    //   "tekshirildi" deyish spam bo'lardi. Qo'ng'iroqchada
                    //   esa yuqoridagi o'chirish tufayli DOIM oxirgi holat
                    //   ko'rinadi — ikki kanalning xatti-harakati ataylab
                    //   turlicha.
                    // ═══════════════════════════════════════════════════════
                    IdempotencyKey = string.Create(
                        CultureInfo.InvariantCulture,
                        $"submission_graded:{submission.Id}:{submission.AttemptNumber}"),
                },
                ct);
        }

        return row;
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
    /// | Kurs vazifasi yaratish   | ✔              | ✘                        |
    /// | Guruh vazifasi yaratish  | ✔              | ✘ (R32 — quyida)         |
    /// | Tahrirlash               | ✔              | ✘ (R32)                  |
    /// | Shart biriktirmasi       | ✔              | ✘ (R32)                  |
    /// | O'chirish                | ✔              | ✘                        |
    /// | Vazifani KO'RISH         | ✔ hammasi      | ✔ o'z guruhi + kurs vaz. |
    /// | Javoblarni ko'rish       | ✔ hammasi      | ✔ faqat O'Z o'quvchilari |
    /// | Baholash / qayta ochish  | ✔              | ✔ faqat O'Z o'quvchisini |
    ///
    /// Admin/Academic baholashdan CHETLATILMAGAN (ro'yxatdagi "Teacher/Assistant"
    /// dan ko'proq): o'quv bo'limi ustozning xatosini tuzatishi kerak, aks
    /// holda noto'g'ri baho butun tizimda tuzatilmas bo'lib qolardi.
    ///
    /// ═══════════════════════════════════════════════════════════════════
    /// R32 (2026-08-13) — BEKOR QILINGAN QATOR: "ustoz O'Z guruhiga beradi"
    /// ═══════════════════════════════════════════════════════════════════
    /// Ilgari ustoz/kurator O'Z guruhiga vazifa YARATA VA TAHRIRLAY olardi
    /// (kurs vazifasi allaqachon faqat o'quv bo'limida edi). Loyiha egasi
    /// buni ham yopdi: *"teacher vazifa yaratishi kerakmas, o'quv bo'limi
    /// yaratadi vazifalarni"* — Q10 QAT'IY o'qilishda.
    ///
    /// ★ NIMA UCHUN QOIDA O'CHIRILDI, SHUNCHAKI ROL RO'YXATI QISQARTIRILMADI:
    /// "ustoz o'z guruhiga" tarmog'i qolib, unga hech qachon kirilmasa,
    /// keyingi o'quvchi kod uni TIRIK deb o'qirdi va yangi endpointda
    /// takrorlardi. Endi <see cref="EnsureCanCreateAsync"/> va
    /// <see cref="EnsureCanWriteAsync"/> bitta gapni aytadi.
    ///
    /// ⚠️ O'QISH va BAHOLASH tarmoqlari TEGILMADI: ustoz vazifani ko'rishi
    /// va javoblarni baholashi kerak, aks holda talab baholashni ham
    /// o'chirib yuborardi — egasi bunday demagan.
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

    /// <summary>
    /// YARATISH ruxsati.
    ///
    /// ★ IMZO ATAYLAB QISQARDI: ilgari u <c>groupId</c>, <c>moduleLessonId</c>
    /// va <c>CancellationToken</c> olardi va bazaga borardi ("bu ustozning
    /// guruhimi?"). R32 dan keyin qaror NISHONGA ham, bazaga ham qaramaydi —
    /// ya'ni endi u sinxron va argumentsiz. Eski imzoni saqlab, ichini
    /// bo'shatish "bu yerda hali ham nishon tekshirilyapti" degan yolg'on
    /// taassurot qoldirardi.
    /// </summary>
    private static void EnsureCanCreate(User actor)
    {
        if (CanManageEverything(actor)) return;

        throw new ForbiddenException(
            "Vazifani faqat o'quv bo'limi yaratadi. Ustoz va kurator "
            + "topshirilgan ishlarni ko'radi va baholaydi.");
    }

    /// <summary>
    /// WAVE 1: O'QISH darvozasi — OSHKOR yo'l (shart biriktirmalari uchun).
    ///
    /// Ichida `GetAsync` bilan AYNI ikki tarmoq: o'quvchi -> "menga
    /// tegishlimi", xodim -> rol va nishon qoidasi. Qoida TAKRORLANMAYDI.
    /// </summary>
    public async Task EnsureCanReadAssignmentAsync(
        long assignmentId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);

        var assignment = await db.Assignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assignmentId, ct)
            ?? throw new NotFoundException(nameof(Assignment), assignmentId);

        if (actor.Role == UserRole.Student)
            await EnsureVisibleToStudentAsync(assignment, actor.Id, ct);
        else
            await EnsureCanReadAsync(actor, assignment, ct);
    }

    /// <summary>
    /// WAVE 1: YOZISH darvozasi — OSHKOR yo'l (shart biriktirmalari uchun).
    ///
    /// ★ Vazifa RUXSATDAN OLDIN yuklanadi, garchi R32 dan keyin qoida uni
    /// o'qimasa ham: mavjud bo'lmagan vazifa 404 olishi kerak, 403 emas.
    /// Aks holda o'quv bo'limi xodimi noto'g'ri Id kiritganda "ruxsatingiz
    /// yo'q" degan chalg'ituvchi xabar ko'rardi.
    /// </summary>
    public async Task EnsureCanWriteAssignmentAsync(
        long assignmentId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);

        var exists = await db.Assignments.AsNoTracking()
            .AnyAsync(a => a.Id == assignmentId, ct);

        if (!exists)
            throw new NotFoundException(nameof(Assignment), assignmentId);

        EnsureCanWrite(actor);
    }

    /// <summary>
    /// R37: JAVOBNI KO'RISH darvozasi — OSHKOR yo'l (tekshiruv fayllari uchun).
    ///
    /// ★ Ichida <c>OpenFileAsync</c> bilan AYNI metod
    /// (<see cref="EnsureCanReadStudentWorkAsync"/>) chaqiriladi — ya'ni
    /// o'quvchining ISHIGA kirish qoidasi butun loyihada BITTA joyda
    /// qoladi.
    /// </summary>
    public async Task EnsureCanReadSubmissionAsync(
        long submissionId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct);

        var ownerId = await db.Submissions.AsNoTracking()
            .Where(s => s.Id == submissionId)
            .Select(s => (long?)s.StudentId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Submission), submissionId);

        await EnsureCanReadStudentWorkAsync(actor, ownerId, ct);
    }

    /// <summary>
    /// R37: JAVOBNI BAHOLASH darvozasi — OSHKOR yo'l.
    ///
    /// ★ <c>GradeAsync</c> ISHLATADIGAN AYNI yuklovchi
    /// (<see cref="LoadSubmissionForStaffAsync"/>): "baho qo'yadigan" va
    /// "tekshiruv fayli qo'yadigan" odam ta'rifi bir-biridan ajralib
    /// ketmasin. Yuklangan obyekt bu yerda KERAK EMAS — faqat u
    /// istisno ko'tarmasligi muhim.
    /// </summary>
    public async Task EnsureCanGradeSubmissionAsync(
        long submissionId, long actorId, CancellationToken ct = default)
    {
        _ = await LoadSubmissionForStaffAsync(submissionId, actorId, ct);
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

    /// <summary>
    /// YOZISH ruxsati (tahrirlash va shart biriktirmalari) — o'qishdan
    /// qat'iyroq.
    ///
    /// ★ R32 dan keyin YARATISH bilan AYNI qoida
    /// (<see cref="EnsureCanCreate"/>), shuning uchun u ham nishonni
    /// so'ramaydi va bazaga bormaydi. Ikkitasi ataylab ALOHIDA metod bo'lib
    /// qoldi: xato XABARI boshqa ("yaratadi" / "tahrirlaydi") va kelajakda
    /// biri yumshasa (masalan ustoz o'z vazifasining muddatini surishi),
    /// ikkinchisi bexosdan yumshab qolmasin.
    /// </summary>
    private static void EnsureCanWrite(User actor)
    {
        if (CanManageEverything(actor)) return;

        throw new ForbiddenException(
            "Vazifani faqat o'quv bo'limi tahrirlaydi.");
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

        // ═══════════════════════════════════════════════════════════════
        // R33 — BAHOLASH DARVOZASI. Bu metod baholash va qayta ochishning
        // YAGONA yo'li, shu sababli tanlov shu yerda BIR MARTA qo'llanadi.
        //
        // 🔴 `Academic`/`Admin` yuqorida allaqachon chiqib ketgan
        // (`CanManageEverything`) — ular tanlovga BO'YSUNMAYDI. Sabab
        // ruxsat jadvalidagi izohda: o'quv bo'limi ustozning xatosini
        // tuzatishi kerak, aks holda noto'g'ri baho tuzatilmas bo'lardi.
        // ═══════════════════════════════════════════════════════════════
        var allowed = await IsMyStudentAsync(
            actor.Id, submission.StudentId, StaffDuty.Grading, assignment.GraderRole, ct);

        if (allowed) return (submission, assignment);

        // XATO MATNI TANLOVGA QARAB: "guruhingizda emas" deyish endi
        // chalg'itardi — o'quvchi guruhda BOR, faqat tekshiruvchi boshqa.
        // Xodim sababni bilmasa o'quv bo'limiga "tizim ishlamayapti" deb
        // murojaat qilardi.
        var inScope = await IsMyStudentAsync(
            actor.Id, submission.StudentId, StaffDuty.Access, null, ct);

        throw new ForbiddenException(
            inScope
                ? "Bu vazifani boshqa xodim tekshiradi — tekshiruvchini o'quv bo'limi tayinlaydi."
                : "Bu o'quvchi sizning guruhingizda emas.");
    }

    /// <summary>
    /// O'QUVCHINING ISHIGA (javob va uning fayllariga) kirish huquqi.
    ///
    /// | Kim              | Nimani ko'radi                    |
    /// |------------------|-----------------------------------|
    /// | O'quvchi         | FAQAT o'zinikini                  |
    /// | Ustoz/kurator    | O'z guruhidagi o'quvchinikini     |
    /// | Academic/Admin   | Hammasini                         |
    ///
    /// "Ustoz o'z guruhida" sharti <see cref="StudentIdsOfStaff"/> dan —
    /// baholash tekshiruvi bilan AYNI IFODA. Bu ataylab: qoida nusxalansa,
    /// masalan kurator havolasi baholashda hisobga olinib, faylda unutilsa,
    /// kurator o'z o'quvchisining rasmini ocholmay qolardi (yoki aksincha —
    /// jimgina teshik ochilardi).
    /// </summary>
    private async Task EnsureCanReadStudentWorkAsync(
        User actor, long ownerStudentId, CancellationToken ct)
    {
        if (actor.Role == UserRole.Student)
        {
            // BOSHQA o'quvchining ishi — hech qanday shartsiz TAQIQ.
            // Eski tizimda fayl `/media` da autentifikatsiyasiz turardi va
            // havolani bilgan har kim ochardi (audit X-6).
            if (ownerStudentId != actor.Id)
                throw new ForbiddenException("Bu fayl sizga tegishli emas.");

            return;
        }

        if (CanManageEverything(actor)) return;

        if (!IsStaff(actor))
            throw new ForbiddenException("Bu faylga ruxsatingiz yo'q.");

        // ⚠️ R33: bu yerda ATAYLAB `Access` — "kim BAHOLAYDI" emas, "kim
        // KO'RADI". Baholash `Grading` ga o'tkazilgan bo'lsa ham, ustoz o'z
        // guruhidagi javobning rasmini va ovozini ocholishi kerak: aks holda
        // u darsda nima bo'layotganini umuman bilmasdi, va talab bunday
        // cheklovni SO'RAMAGAN.
        if (!await IsMyStudentAsync(actor.Id, ownerStudentId, StaffDuty.Access, null, ct))
            throw new ForbiddenException("Bu o'quvchi sizning guruhingizda emas.");
    }

    /// <summary>
    /// Xodim SHU o'quvchiga mas'ulmi — <see cref="StudentIdsOfStaff"/> ustidan
    /// YAGONA tekshiruv. Baholash ham, fayl o'qish ham shu yerdan o'tadi.
    /// </summary>
    private async Task<bool> IsMyStudentAsync(
        long staffId,
        long studentId,
        StaffDuty duty,
        GroupStaffRole? assignmentOverride,
        CancellationToken ct) =>
        await StudentIdsOfStaff(staffId, duty, assignmentOverride).ContainsAsync(studentId, ct);

    /// <summary>
    /// Xodim (ustoz/kurator) mas'ul bo'lgan o'quvchilar — BITTA ifoda,
    /// bir necha joyda ishlatiladi (javoblar filtri, baholash tekshiruvi,
    /// fayl o'qish), shuning uchun ular hech qachon ajralib ketmaydi.
    ///
    /// KURATOR ham hisobga olinadi: kurator darsida BOG'LANGAN ustoz
    /// guruhlarining o'quvchilari qatnashadi. Eski tizimda bu havola
    /// hisobga olinmagani uchun (B-8a) kurator o'z o'quvchisining javobini
    /// ko'ra ham, baholay ham olmasdi.
    ///
    /// ═══════════════════════════════════════════════════════════════════
    /// R33 (2026-08-14) — QOIDA ENDI <paramref name="duty"/> GA BOG'LIQ
    /// ═══════════════════════════════════════════════════════════════════
    ///
    /// Ilgari bu yerda ustoz va kurator BITTA OR ga qo'shilardi, ya'ni
    /// "bu vazifani KURATOR tekshirsin" deyishning imkoni yo'q edi.
    /// Ifodaning O'ZI endi <c>StaffResponsibility</c> da — AYNI ifodani
    /// yozishma servisi ham o'qiydi (R40), shu sababli ikki ruxsat
    /// hech qachon ajralib ketmaydi.
    ///
    /// ⚠️ <see cref="StaffDuty.Access"/> tarmog'i — bugungi ifodaning
    /// AYNAN o'zi (o'rindiqni ajratmaydi). Fayl o'qish va ro'yxatlar
    /// ATAYLAB shunda qoladi: R33 "kim TEKSHIRADI" ni so'radi, "kim
    /// KO'RADI" ni emas.
    ///
    /// `IQueryable` qaytaradi — chaqiruvchi uni ichma-ich so'rov sifatida
    /// ishlatadi (`WHERE ... IN (SELECT ...)`), ya'ni ID'lar ilovaga
    /// tortilmaydi.
    /// </summary>
    /// <param name="assignmentOverride">
    /// Vazifa darajasidagi istisno (<c>Assignment.GraderRole</c>).
    /// <c>null</c> — guruh sozlamasi o'qiladi.
    /// </param>
    private IQueryable<long> StudentIdsOfStaff(
        long staffId, StaffDuty duty, GroupStaffRole? assignmentOverride = null)
    {
        var groupIds = StaffGroupIds(staffId, duty, assignmentOverride);

        return db.GroupMembers
            .AsNoTracking()
            .Where(m => m.Status == MemberStatus.Active && groupIds.Contains(m.GroupId))
            .Select(m => m.StudentId);
    }

    /// <summary>Xodim mas'ul bo'lgan guruhlar (ichma-ich so'rov sifatida).</summary>
    private IQueryable<long> StaffGroupIds(
        long staffId, StaffDuty duty, GroupStaffRole? assignmentOverride = null) =>
        db.Groups
            .AsNoTracking()
            .Where(StaffResponsibility.Predicate(staffId, duty, assignmentOverride))
            .Select(g => g.Id);

    private async Task<bool> IsStaffOfGroupAsync(long staffId, long groupId, CancellationToken ct) =>
        await StaffGroupIds(staffId, StaffDuty.Access).ContainsAsync(groupId, ct);

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

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// R33 — «TANLANGAN O'RINDIQ BO'SHMI» TEKSHIRUVI (yaratish/tahrirlash)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// 🔴 NIMA UCHUN KERAK: <c>Group.AssistantId</c> NULL bo'lishi mumkin.
    /// "Kurator tekshirsin" deb qo'yilgan, lekin kuratori yo'q guruhda
    /// topshirilgan ish EGASIZ qolardi — o'quvchi javobini yuborgan,
    /// ustoz esa ro'yxatda ko'rmaydi va hech kim baholay olmaydi. Xato
    /// FAQAT o'quvchi shikoyat qilganda bilinardi.
    ///
    /// ★ IKKI QATLAM, IKKI XIL VAZIFA — va ular BIR-BIRINI ALMASHTIRMAYDI:
    ///
    ///   • SHU YERDA — 400. O'quv bo'limi bo'sh o'rindiqni TANLAY olmaydi,
    ///     xato yaratish paytida, tushunarli matn bilan chiqadi.
    ///
    ///   • <c>StaffResponsibility</c> ichidagi ZAXIRA YO'L — keyin buzilgan
    ///     sozlama uchun. Vazifa to'g'ri yaratilgan, keyin kurator guruhdan
    ///     olib tashlangan bo'lsa hech qanday validatsiya yordam bermaydi:
    ///     o'sha payt ish allaqachon topshirilgan. Shunda baholash
    ///     ikkinchi o'rindiqqa o'tadi.
    ///
    /// Bittasi yetmaydi: faqat validatsiya bo'lsa keyingi o'zgarish ishni
    /// egasiz qoldirardi, faqat zaxira yo'l bo'lsa o'quv bo'limi
    /// "kuratorga berdim" deb o'ylab, aslida ustozga bergan bo'lardi.
    /// </summary>
    private async Task EnsureGraderSeatFilledAsync(Assignment assignment, CancellationToken ct)
    {
        if (assignment.GraderRole is not { } role) return;

        // `Validate()` allaqachon kafolatladi: istisno FAQAT guruh vazifasida.
        if (assignment.GroupId is not { } groupId) return;

        var filled = await db.Groups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .AnyAsync(StaffResponsibility.HasSeat(role), ct);

        if (filled) return;

        throw new ValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [nameof(CreateAssignmentRequest.GraderRole)] =
            [
                role == GroupStaffRole.Teacher
                    ? "Bu guruhga ustoz biriktirilmagan — tekshiruvchi qilib tanlab bo'lmaydi."
                    : "Bu guruhga kurator biriktirilmagan — tekshiruvchi qilib tanlab bo'lmaydi.",
            ],
        });
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

    /// <summary>
    /// ★★ KAMIDA BITTA JAVOB FORMATI TANLANISHI SHART — 400.
    ///
    /// 🔴 NIMA UCHUN ALOHIDA TEKSHIRUV, `Assignment.Validate()` YETARLI
    /// EMAS: Domain bu holatni allaqachon ushlaydi, lekin
    /// `DomainException` -> HTTP **409**. 409 esa "holat ziddiyati"
    /// degani va frontend uni `problem.detail` orqali umumiy xato deb
    /// ko'rsatardi — ya'ni foydalanuvchi QAYSI MAYDON xato ekanini
    /// bilmasdi. Bu esa AYNAN maydon validatsiyasi: shu yerda 400 va
    /// `problem.errors["allowedFormats"]` beriladi, ya'ni forma xatoni
    /// to'g'ri katakcha ostida ko'rsatadi.
    ///
    /// ★★ NIMA UCHUN BU UMUMAN JIMGINA TUZOQ: `AllowedFormats = None`
    /// bo'lgan vazifa MUVAFFAQIYATLI yaratilardi va o'quvchi uni ko'rardi,
    /// lekin HAR QANDAY javob `EnsureFormatAllowed` da rad etilardi. Ya'ni
    /// vazifa mavjud, muddati ketmoqda, topshirish esa TEXNIK JIHATDAN
    /// imkonsiz — va sabab faqat bazadagi bitta nolda ko'rinardi.
    /// </summary>
    private static void RequireAnswerFormats(AnswerFormats formats)
    {
        if (formats == AnswerFormats.None)
        {
            throw Invalid(
                "allowedFormats",
                "Kamida bitta javob formati tanlanishi shart (matn, rasm yoki audio). "
                + "Aks holda o'quvchi bu vazifaga javob berolmaydi.");
        }

        // Enumda mavjud bo'lmagan bayroq (masalan 64) — klient xatosi.
        // `[Flags]` uchun `Enum.IsDefined` ISHLAMAYDI (u faqat aniq
        // qiymatlarni biladi), shuning uchun MA'LUM bayroqlar maskasi
        // bilan solishtiriladi.
        const AnswerFormats Known = AnswerFormats.Text | AnswerFormats.Image | AnswerFormats.Audio;

        if ((formats & ~Known) != AnswerFormats.None)
        {
            throw Invalid(
                "allowedFormats",
                "Javob formati noma'lum. Ruxsat etilganlar: Text, Image, Audio.");
        }
    }

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

            // WAVE 1: shart biriktirmalari AYNI so'rovda (correlated
            // projection) — vazifa boshiga alohida so'rov YO'Q.
            // 🔴 `ObjectKey` bu proyeksiyaga UMUMAN kirmaydi (16-tuzoq).
            a.Attachments
                .OrderBy(x => x.Position)
                .ThenBy(x => x.Id)
                .Select(x => new AssignmentAttachmentDto(
                    x.Id,
                    x.AssignmentId,
                    x.Kind,
                    x.Position,
                    x.ContentType,
                    x.SizeBytes,
                    x.DurationSec,
                    x.CreatedAt))
                .ToList(),
            a.CreatedById,
            db.Submissions.Count(s => s.AssignmentId == a.Id),
            db.Submissions.Count(s => s.AssignmentId == a.Id && s.Status == SubmissionStatus.Graded),
            a.CreatedAt,
            a.UpdatedAt,
            a.GraderRole));

    /// <summary>
    /// Entity kolleksiyasidan DTO ro'yxati (o'quvchi yo'li `Include` bilan
    /// ishlaydi, ya'ni bu yerda IFODA DARAXTI emas, oddiy o'girish).
    /// </summary>
    private static List<AssignmentAttachmentDto> MapAttachments(
        IEnumerable<AssignmentAttachment> attachments) =>
        attachments
            .OrderBy(x => x.Position)
            .ThenBy(x => x.Id)
            .Select(x => new AssignmentAttachmentDto(
                x.Id,
                x.AssignmentId,
                x.Kind,
                x.Position,
                x.ContentType,
                x.SizeBytes,
                x.DurationSec,
                x.CreatedAt))
            .ToList();

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
                .ToList(),

            // R37: USTOZ biriktirgan fayllar — ALOHIDA jadvaldan.
            //
            // ★ SHU YERDA proyeksiyaga qo'shildi, alohida so'rov bilan
            // EMAS: baholash ro'yxati bir necha o'nlab javobni qaytaradi va
            // har biri uchun alohida so'rov klassik N+1 bo'lardi. EF buni
            // bitta `LEFT JOIN` ga aylantiradi.
            s.FeedbackFiles
                .OrderBy(f => f.Id)
                .Select(f => new SubmissionFeedbackFileDto(
                    f.Id, f.SubmissionId, f.Kind, f.ContentType, f.FileName,
                    f.SizeBytes, f.CreatedById, f.CreatedAt))
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
        row.AttemptNumber, row.AllowResubmit, row.ResubmitNote, row.IsLate, row.Files,
        row.FeedbackFiles);

    private static StudentSubmissionDto MapStudent(SubmissionRow row) => new(
        row.Id, row.Status, row.Text, row.Score, Percent(row), row.Feedback, row.SubmittedAt,
        row.AttemptNumber, row.AllowResubmit, row.ResubmitNote, row.IsLate, row.Files,
        row.FeedbackFiles);

    // ---------------------------------------------------------------- doimiylar va ichki turlar

    private const int MaxPageSize = 100;

    /// <summary>
    /// O'quv bo'limi umumiy ko'rinishi uchun YASSI qator — guruh, ustoz va
    /// tekshiruvchi konteksti bilan (<see cref="SubmissionRow"/> dan farqli:
    /// bu yerda R37 fayllari YO'Q, chunki ro'yxat kartochkasi ularni
    /// ko'rsatmaydi — kerak bo'lsa xodim javobni ochib ko'radi).
    /// </summary>
    private sealed record SubmissionOverviewRow(
        long Id,
        long AssignmentId,
        string AssignmentTitle,
        long? GroupId,
        string? GroupName,
        GroupType? GroupType,
        long? TeacherId,
        string? TeacherName,
        string? AssistantName,
        GroupStaffRole? AssignmentGraderRole,
        GroupStaffRole? GroupGraderRole,
        long StudentId,
        string StudentName,
        SubmissionStatus Status,
        decimal? Score,
        decimal MaxScore,
        DateTimeOffset SubmittedAt,
        bool IsLate,
        int AttemptNumber,
        DateTimeOffset? GradedAt,
        long? GradedById,
        string? GradedByName);

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
        List<SubmissionFileDto> Files,
        List<SubmissionFeedbackFileDto> FeedbackFiles);
}
