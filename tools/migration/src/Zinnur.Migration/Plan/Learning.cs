using Zinnur.Migration.Mapping;
using Zinnur.Migration.Pipeline;
using static Zinnur.Migration.Plan.MigrationPlan;

namespace Zinnur.Migration.Plan;

/// <summary>O'quv jarayoni: vazifa, javob, fayl, progress, test.</summary>
internal static class Learning
{
    // ====================================================================
    // VAZIFALAR
    // ====================================================================

    /// <summary>
    /// <c>assignments</c> -> <c>Assignments</c>.
    ///
    /// ★★ v2 DA QAT'IY CHEKLOV BOR: <c>CK_Assignments_GroupXorLesson</c> —
    /// vazifa YO guruhga, YO kurs darsiga tegishli, IKKALASIGA emas va
    /// HECH BIRIGA emas ham bo'lolmaydi. Eski sxemada ikkala ustun ham
    /// ixtiyoriy edi, ya'ni prod bazasida "ikkalasi ham bo'sh" yoki
    /// "ikkalasi ham to'la" qatorlar BO'LISHI MUMKIN.
    ///
    /// Qaror:
    ///   • ikkalasi ham to'la  -> GURUH vazifasi deb olinadi
    ///     (eski <c>student_router</c> shu tartibda o'qigan: avval
    ///     <c>group_id</c>, keyin kurs darsi), <c>module_lesson_id</c>
    ///     BEKOR QILINADI va hisobotga tushadi;
    ///   • ikkalasi ham bo'sh -> qator KO'CHMAYDI (uni hech kim ko'ra
    ///     olmasdi ham — na guruh sahifasida, na dars sahifasida).
    ///
    /// ⚠️ YO'QOTISH: <c>lesson_id</c> (vazifa qaysi JONLI darsda berilgani)
    /// v2 <c>Assignment</c> da yo'q.
    /// </summary>
    public static TableSpec Assignments() => new()
    {
        Name = "assignments -> Assignments",
        SourceTable = "assignments",
        TargetTable = "Assignments",
        SourceCountSql = "SELECT COUNT(*) FROM assignments",
        SourceSql = """
            SELECT id, group_id, module_lesson_id, answer_formats, title, description,
                   max_value, due_at, image_url, created_by, created_at
            FROM assignments
            ORDER BY id
            """,
        Columns =
        [
            Id(), Ref("GroupId"), Ref("ModuleLessonId"), Str("Title"), Str("Description"),
            Dec("MaxScore"), Moment("DueAt"), Num("AllowedFormats"), Str("ImageKey"),
            Ref("CreatedById"), Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var groupId = ctx.Int64OrNull(1);
            if (groupId is not null && !ctx.State.Has("groups", groupId.Value))
            {
                ctx.Fixed("Guruh ko'chmagan — bog'lanish bo'shatildi", RowContext.Str(groupId.Value));
                groupId = null;
            }

            var moduleLessonId = ctx.Int64OrNull(2);
            if (moduleLessonId is not null && !ctx.State.Has("module_lessons", moduleLessonId.Value))
            {
                ctx.Fixed("Kurs darsi ko'chmagan — bog'lanish bo'shatildi", RowContext.Str(moduleLessonId.Value));
                moduleLessonId = null;
            }

            if (groupId is not null && moduleLessonId is not null)
            {
                ctx.Fixed(
                    "Guruh ham, kurs darsi ham to'ldirilgan (v2 da faqat bittasi) — guruh vazifasi deb olindi",
                    RowContext.Str(moduleLessonId.Value));
                moduleLessonId = null;
            }

            if (groupId is null && moduleLessonId is null)
                return ctx.Skip("Na guruhga, na kurs darsiga bog'lanmagan (v2 CHECK cheklovi)");

            var formats = LegacyMap.Formats(ctx.Text(3), out var complete);
            if (!complete)
                ctx.Fixed("Javob formatlari tanilmadi yoki bo'sh — eski standart (matn+rasm)", ctx.Text(3));

            var maxScore = ctx.Money(6);
            if (maxScore <= 0)
            {
                ctx.Fixed("Maksimal ball 0 yoki manfiy — 5 ga tuzatildi", RowContext.Str(maxScore));
                maxScore = 5m;
            }

            var createdBy = ctx.Int64OrNull(9);
            if (!ctx.State.HasOptional("users", createdBy)) createdBy = null;

            ctx.State.Add("assignments", ctx.Id);

            return
            [
                ctx.Id,
                groupId,
                moduleLessonId,
                RowContext.Clip(ctx.Text(4)?.Trim(), 200) is { Length: > 0 } t ? t : "Nomsiz vazifa",
                RowContext.Clip(ctx.Text(5), 4000),
                maxScore,
                ctx.InstantOrNull(7),
                (int)formats,
                RowContext.Clip(ctx.Text(8), 500),
                createdBy,
                ctx.Instant(10),
                null,
            ];
        },
    };

    /// <summary>
    /// <c>submissions</c> -> <c>Submissions</c>.
    ///
    /// ★ <c>IsLate</c> v2 DA YANGI USTUN va eski bazada MOSI YO'Q. U
    /// HISOBLANADI: <c>submitted_at &gt; assignments.due_at</c>. Shuning
    /// uchun so'rovda <c>assignments</c> bilan JOIN bor.
    ///
    /// NIMA UCHUN hisoblanadi, <c>false</c> qo'yilmaydi: aks holda
    /// ko'chirishdan keyin BARCHA eski javoblar "o'z vaqtida" bo'lib
    /// qolardi va kechikish statistikasi ustozga yolg'on ko'rsatardi.
    /// Bu TAXMIN QILINGAN qiymat va hisobotda shunday belgilanadi
    /// (eski tizim kechikishni javob YOZILGAN paytda emas, ko'rsatish
    /// paytida hisoblagan — muddat keyin o'zgargan bo'lsa natija farq qiladi).
    ///
    /// ⚠️ YO'QOTISH: <c>submissions.file_url</c> ustuni v2 da yo'q —
    /// u <see cref="SubmissionLegacyFileUrls"/> da alohida
    /// <c>SubmissionFiles</c> qatoriga aylantiriladi.
    /// </summary>
    public static TableSpec Submissions() => new()
    {
        Name = "submissions -> Submissions",
        SourceTable = "submissions",
        TargetTable = "Submissions",
        SourceCountSql = "SELECT COUNT(*) FROM submissions",
        SourceSql = """
            SELECT s.id, s.assignment_id, s.student_id, s.text, s.status, s.grade_value,
                   s.feedback, s.graded_by, s.graded_at, s.submitted_at, s.updated_at,
                   s.can_resubmit, s.resubmit_note, s.attempt_no, a.due_at
            FROM submissions s
            LEFT JOIN assignments a ON a.id = s.assignment_id
            ORDER BY s.id
            """,
        Columns =
        [
            Id(), Ref("AssignmentId"), Ref("StudentId"), Str("Text"), Num("Status"),
            Dec("Score"), Str("Feedback"), Ref("GradedById"), Moment("GradedAt"),
            Moment("SubmittedAt"), Num("AttemptNumber"), Flag("AllowResubmit"),
            Str("ResubmitNote"), Flag("IsLate"), Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var assignmentId = ctx.Int64(1);
            var studentId = ctx.Int64(2);

            if (!ctx.State.Has("assignments", assignmentId))
                return ctx.Skip("Vazifa ko'chmagan", RowContext.Str(assignmentId));

            if (!ctx.State.Has("users", studentId))
                return ctx.Skip("O'quvchi ko'chmagan", RowContext.Str(studentId));

            if (!LegacyMap.TrySubmissionStatus(ctx.Text(4), out var status))
                return ctx.Skip("Javob holati tanilmadi", ctx.Text(4));

            var gradedBy = ctx.Int64OrNull(7);
            if (!ctx.State.HasOptional("users", gradedBy))
            {
                ctx.Fixed("Baholovchi ko'chmagan — bo'shatildi", RowContext.Str(gradedBy!.Value));
                gradedBy = null;
            }

            var submittedAt = ctx.Instant(9);
            var dueAt = ctx.InstantOrNull(14);
            var isLate = dueAt is not null && submittedAt > dueAt.Value;

            var attempt = ctx.Int32OrNull(13) ?? 1;
            if (attempt < 1)
            {
                ctx.Fixed("Urinish raqami 1 dan kichik — 1 ga tuzatildi", RowContext.Str(attempt));
                attempt = 1;
            }

            ctx.State.Add("submissions", ctx.Id);

            return
            [
                ctx.Id,
                assignmentId,
                studentId,
                RowContext.Clip(ctx.Text(3), 10_000),
                (int)status,
                ctx.MoneyOrNull(5),
                RowContext.Clip(ctx.Text(6), 2000),
                gradedBy,
                ctx.InstantOrNull(8),
                submittedAt,
                attempt,
                ctx.Bool(11),
                RowContext.Clip(ctx.Text(12), 500),
                isLate,
                submittedAt,
                ctx.InstantOrNull(10),
            ];
        },
    };

    /// <summary>
    /// <c>submission_files</c> -> <c>SubmissionFiles</c>.
    ///
    /// ⚠️ TAXMIN: v2 da <c>SizeBytes</c> MAJBURIY, eski jadvalda esa fayl
    /// hajmi UMUMAN saqlanmagan. <c>0</c> yoziladi — bu "hajmi noma'lum"
    /// degani. Ombor hisobotlari (qancha joy band) ko'chirilgan fayllar
    /// bo'yicha NOTO'G'RI chiqadi; buni tuzatish uchun ko'chirishdan keyin
    /// ombordan hajmlarni o'qib chiqadigan alohida ish kerak.
    /// <c>ContentType</c> ham eski bazada yo'q — <c>NULL</c> qoladi.
    /// </summary>
    public static TableSpec SubmissionFiles() => new()
    {
        Name = "submission_files -> SubmissionFiles",
        SourceTable = "submission_files",
        TargetTable = "SubmissionFiles",
        SourceCountSql = "SELECT COUNT(*) FROM submission_files",
        SourceSql = "SELECT id, submission_id, url, kind, created_at FROM submission_files ORDER BY id",
        Columns =
        [
            Id(), Ref("SubmissionId"), Str("ObjectKey"), Num("Kind"), Big("SizeBytes"),
            Str("ContentType"), Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var submissionId = ctx.Int64(1);
            if (!ctx.State.Has("submissions", submissionId))
                return ctx.Skip("Javob ko'chmagan", RowContext.Str(submissionId));

            var key = RowContext.Clip(ctx.Text(2)?.Trim(), 500);
            if (string.IsNullOrEmpty(key))
                return ctx.Skip("Fayl manzili bo'sh");

            return
            [
                ctx.Id,
                submissionId,
                key,
                (int)LegacyMap.AttachmentKind(ctx.Text(3)),
                0L,                                  // ⚠️ hajm noma'lum
                null,                                // ⚠️ MIME turi noma'lum
                ctx.Instant(4),
                null,
            ];
        },
    };

    /// <summary>
    /// <c>submissions.file_url</c> -> <c>SubmissionFiles</c> (qo'shimcha qator).
    ///
    /// ★ NIMA UCHUN ALOHIDA QADAM: eski tizimning ESKIROQ versiyasida
    /// javobga BITTA fayl biriktirilardi va u <c>submissions.file_url</c>
    /// ustunida turardi; <c>submission_files</c> jadvali keyinroq
    /// qo'shilgan. Ya'ni prod bazasida IKKALA ko'rinish ham bor. Faqat
    /// yangi jadval ko'chirilsa eski javoblarning fayli JIMGINA yo'qolardi.
    ///
    /// ID: <c>10^12 + submission_id</c> (<see cref="MigrationPlan.FileUrlIdOffset"/>) —
    /// determinstik, ya'ni qayta yurgizishda AYNI ID hosil bo'ladi va
    /// <c>ON CONFLICT</c> ishlaydi.
    ///
    /// Fayl turi kengaytmadan taxmin qilinadi — hisobotda shunday belgilanadi.
    /// </summary>
    public static TableSpec SubmissionLegacyFileUrls() => new()
    {
        Name = "submissions.file_url -> SubmissionFiles",
        SourceTable = "submissions(file_url)",
        TargetTable = "SubmissionFiles",
        SourceCountSql = "SELECT COUNT(*) FROM submissions WHERE COALESCE(btrim(file_url), '') <> ''",
        SourceSql = """
            SELECT id, file_url, submitted_at
            FROM submissions
            WHERE COALESCE(btrim(file_url), '') <> ''
            ORDER BY id
            """,
        Columns =
        [
            Id(), Ref("SubmissionId"), Str("ObjectKey"), Num("Kind"), Big("SizeBytes"),
            Str("ContentType"), Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var submissionId = ctx.Id;
            if (!ctx.State.Has("submissions", submissionId))
                return ctx.Skip("Javob ko'chmagan", RowContext.Str(submissionId));

            var key = RowContext.Clip(ctx.Text(1)?.Trim(), 500);
            if (string.IsNullOrEmpty(key))
                return ctx.Skip("Fayl manzili bo'sh");

            ctx.Fixed("Eski `file_url` ustunidan fayl qatori yaratildi (turi kengaytmadan taxmin)", key);

            return
            [
                FileUrlIdOffset + submissionId,
                submissionId,
                key,
                (int)LegacyMap.KindFromExtension(key),
                0L,
                null,
                ctx.Instant(2),
                null,
            ];
        },
    };

    /// <summary>
    /// <c>lesson_progress</c> -> <c>LessonProgress</c>.
    ///
    /// ⚠️ TAXMIN: v2 da <c>OverrideReason</c> va <c>OverrideById</c> bor —
    /// "kim va nima uchun qo'lda ochib berdi". Eski jadvalda faqat
    /// <c>unlocked_override</c> bayrog'i saqlangan, sabab ham, kim
    /// ochgani ham YO'Q. Ikkalasi <c>NULL</c> qoladi va qo'lda ochilgan
    /// qatorlar hisobotda sanaladi.
    /// </summary>
    public static TableSpec LessonProgress() => new()
    {
        Name = "lesson_progress -> LessonProgress",
        SourceTable = "lesson_progress",
        TargetTable = "LessonProgress",
        SourceCountSql = "SELECT COUNT(*) FROM lesson_progress",
        SourceSql = """
            SELECT id, student_id, module_lesson_id, video_watched_at,
                   unlocked_override, created_at
            FROM lesson_progress
            ORDER BY id
            """,
        Columns =
        [
            Id(), Ref("StudentId"), Ref("ModuleLessonId"), Moment("VideoWatchedAt"),
            Flag("UnlockedOverride"), Str("OverrideReason"), Ref("OverrideById"),
            Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var studentId = ctx.Int64(1);
            var moduleLessonId = ctx.Int64(2);

            if (!ctx.State.Has("users", studentId))
                return ctx.Skip("O'quvchi ko'chmagan", RowContext.Str(studentId));

            if (!ctx.State.Has("module_lessons", moduleLessonId))
                return ctx.Skip("Kurs darsi ko'chmagan", RowContext.Str(moduleLessonId));

            var overridden = ctx.Bool(4);
            if (overridden)
                ctx.Fixed("Qo'lda ochilgan dars: sabab va ochgan xodim eski bazada saqlanmagan");

            return
            [
                ctx.Id,
                studentId,
                moduleLessonId,
                ctx.InstantOrNull(3),
                overridden,
                null,                        // ⚠️ sabab noma'lum
                null,                        // ⚠️ kim ochgani noma'lum
                ctx.Instant(5),
                null,
            ];
        },
    };

    // ====================================================================
    // TESTLAR
    // ====================================================================

    public static TableSpec Tests() => new()
    {
        Name = "tests -> Tests",
        SourceTable = "tests",
        TargetTable = "Tests",
        SourceCountSql = "SELECT COUNT(*) FROM tests",
        SourceSql = """
            SELECT id, title, description, kind, module_lesson_id, time_limit_min,
                   due_at, is_published, created_by, created_at
            FROM tests
            ORDER BY id
            """,
        Columns =
        [
            Id(), Str("Title"), Str("Description"), Num("Kind"), Ref("ModuleLessonId"),
            Num("TimeLimitMinutes"), Moment("DueAt"), Flag("IsPublished"),
            Ref("CreatedById"), Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            if (!LegacyMap.TryTestKind(ctx.Text(3), out var kind))
                return ctx.Skip("Test turi tanilmadi", ctx.Text(3));

            var moduleLessonId = ctx.Int64OrNull(4);
            if (moduleLessonId is not null && !ctx.State.Has("module_lessons", moduleLessonId.Value))
            {
                ctx.Fixed("Kurs darsi ko'chmagan — bog'lanish bo'shatildi", RowContext.Str(moduleLessonId.Value));
                moduleLessonId = null;
            }

            // Eski tizimda dars testi kurs darsisiz qolishi mumkin edi
            // (dars o'chirilganda `ON DELETE SET NULL`). v2 da bunday test
            // sur'at nazoratiga tusha olmaydi — musobaqa testiga aylantiramiz.
            if (kind == Domain.Enums.TestKind.Lesson && moduleLessonId is null)
            {
                ctx.Fixed("Dars testi kurs darsiga bog'lanmagan — musobaqa testi deb belgilandi");
                kind = Domain.Enums.TestKind.Competition;
            }

            var createdBy = ctx.Int64OrNull(8);
            if (!ctx.State.HasOptional("users", createdBy)) createdBy = null;

            ctx.State.Add("tests", ctx.Id);

            return
            [
                ctx.Id,
                RowContext.Clip(ctx.Text(1)?.Trim(), 200) is { Length: > 0 } t ? t : "Nomsiz test",
                RowContext.Clip(ctx.Text(2), 2000),
                (int)kind,
                moduleLessonId,
                ctx.Int32OrNull(5),
                ctx.InstantOrNull(6),
                ctx.Bool(7),
                createdBy,
                ctx.Instant(9),
                null,
            ];
        },
    };

    public static TableSpec TestQuestions() => new()
    {
        Name = "test_questions -> TestQuestions",
        SourceTable = "test_questions",
        TargetTable = "TestQuestions",
        SourceCountSql = "SELECT COUNT(*) FROM test_questions",
        SourceSql = "SELECT id, test_id, body, image_url, position, points FROM test_questions ORDER BY id",
        Columns =
        [
            Id(), Ref("TestId"), Str("Body"), Str("ImageKey"), Num("Position"),
            Dec("Points"), Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var testId = ctx.Int64(1);
            if (!ctx.State.Has("tests", testId))
                return ctx.Skip("Test ko'chmagan", RowContext.Str(testId));

            var body = RowContext.Clip(ctx.Text(2)?.Trim(), 2000);
            if (string.IsNullOrEmpty(body))
                return ctx.Skip("Savol matni bo'sh (v2 da majburiy)");

            var points = ctx.Money(5, 1m);
            if (points <= 0)
            {
                ctx.Fixed("Savol bali 0 yoki manfiy — 1 ga tuzatildi", RowContext.Str(points));
                points = 1m;
            }

            ctx.State.Add("test_questions", ctx.Id);

            return
            [
                ctx.Id, testId, body, RowContext.Clip(ctx.Text(3), 500),
                ctx.Int32OrNull(4) ?? 0, points, Fallback, null,
            ];
        },
    };

    public static TableSpec TestOptions() => new()
    {
        Name = "test_options -> TestOptions",
        SourceTable = "test_options",
        TargetTable = "TestOptions",
        SourceCountSql = "SELECT COUNT(*) FROM test_options",
        SourceSql = "SELECT id, question_id, body, is_correct, position FROM test_options ORDER BY id",
        Columns =
        [
            Id(), Ref("QuestionId"), Str("Body"), Flag("IsCorrect"), Num("Position"),
            Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var questionId = ctx.Int64(1);
            if (!ctx.State.Has("test_questions", questionId))
                return ctx.Skip("Savol ko'chmagan", RowContext.Str(questionId));

            var body = RowContext.Clip(ctx.Text(2)?.Trim(), 1000);
            if (string.IsNullOrEmpty(body))
                return ctx.Skip("Variant matni bo'sh (v2 da majburiy)");

            ctx.State.Add("test_options", ctx.Id);

            return [ctx.Id, questionId, body, ctx.Bool(3), ctx.Int32OrNull(4) ?? 0, Fallback, null];
        },
    };

    /// <summary>
    /// <c>test_attempts</c> -> <c>TestAttempts</c>.
    ///
    /// ⚠️ TAXMIN: v2 da <c>ClosedByTimeout</c> ("vaqt tugagani uchun
    /// yopilgan") bor, eski bazada esa bunday belgi yo'q — <c>false</c>
    /// yoziladi. Ya'ni ko'chirilgan urinishlarning HECH BIRI "vaqt
    /// tugadi" deb ko'rinmaydi; bu statistikani biroz optimistik qiladi.
    /// </summary>
    public static TableSpec TestAttempts() => new()
    {
        Name = "test_attempts -> TestAttempts",
        SourceTable = "test_attempts",
        TargetTable = "TestAttempts",
        SourceCountSql = "SELECT COUNT(*) FROM test_attempts",
        SourceSql = """
            SELECT id, test_id, student_id, status, score, max_score, started_at, submitted_at
            FROM test_attempts
            ORDER BY id
            """,
        Columns =
        [
            Id(), Ref("TestId"), Ref("StudentId"), Num("Status"), Dec("Score"),
            Dec("MaxScore"), Moment("StartedAt"), Moment("SubmittedAt"),
            Flag("ClosedByTimeout"), Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var testId = ctx.Int64(1);
            var studentId = ctx.Int64(2);

            if (!ctx.State.Has("tests", testId))
                return ctx.Skip("Test ko'chmagan", RowContext.Str(testId));

            if (!ctx.State.Has("users", studentId))
                return ctx.Skip("O'quvchi ko'chmagan", RowContext.Str(studentId));

            if (!LegacyMap.TryAttemptStatus(ctx.Text(3), out var status))
                return ctx.Skip("Urinish holati tanilmadi", ctx.Text(3));

            var startedAt = ctx.Instant(6);
            ctx.State.Add("test_attempts", ctx.Id);

            return
            [
                ctx.Id, testId, studentId, (int)status, ctx.MoneyOrNull(4), ctx.MoneyOrNull(5),
                startedAt, ctx.InstantOrNull(7), false, startedAt, null,
            ];
        },
    };

    /// <summary>
    /// <c>test_answers</c> -> <c>TestAnswers</c>.
    ///
    /// ★★ ANIQ MA'LUMOT YO'QOTISHI: eski <c>option_id</c> USTUNI
    /// <c>NULL</c> BO'LISHI MUMKIN — bu "o'quvchi savolni ko'rdi, lekin
    /// variant tanlamadi" degani (yoki variant keyin o'chirilgan:
    /// <c>ON DELETE SET NULL</c>). v2 da <c>OptionId</c> MAJBURIY, chunki
    /// javobsiz javob qatori ma'nosiz — javobsizlik qator YO'QLIGI bilan
    /// ifodalanadi.
    ///
    /// Bunday qatorlar KO'CHMAYDI va HAR BIRI hisobotda sanaladi.
    /// Amaliy ta'siri: urinishning `score` qiymati o'zgarmaydi (u
    /// <c>test_attempts</c> da tayyor turadi), lekin "qaysi savolga javob
    /// bermagan" tafsiloti yo'qoladi.
    /// </summary>
    public static TableSpec TestAnswers() => new()
    {
        Name = "test_answers -> TestAnswers",
        SourceTable = "test_answers",
        TargetTable = "TestAnswers",
        SourceCountSql = "SELECT COUNT(*) FROM test_answers",
        SourceSql = "SELECT id, attempt_id, question_id, option_id FROM test_answers ORDER BY id",
        Columns =
        [
            Id(), Ref("AttemptId"), Ref("QuestionId"), Ref("OptionId"),
            Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var attemptId = ctx.Int64(1);
            var questionId = ctx.Int64(2);
            var optionId = ctx.Int64OrNull(3);

            if (!ctx.State.Has("test_attempts", attemptId))
                return ctx.Skip("Urinish ko'chmagan", RowContext.Str(attemptId));

            if (!ctx.State.Has("test_questions", questionId))
                return ctx.Skip("Savol ko'chmagan", RowContext.Str(questionId));

            if (optionId is null)
                return ctx.Skip("Variant tanlanmagan (v2 da OptionId MAJBURIY) — javobsiz savol ko'chmaydi");

            if (!ctx.State.Has("test_options", optionId.Value))
                return ctx.Skip("Variant ko'chmagan", RowContext.Str(optionId.Value));

            return [ctx.Id, attemptId, questionId, optionId, Fallback, null];
        },
    };
}
