using Zinnur.Domain.Entities;
using Zinnur.Migration.Mapping;
using Zinnur.Migration.Pipeline;
using static Zinnur.Migration.Plan.MigrationPlan;

namespace Zinnur.Migration.Plan;

/// <summary>Asosiy ma'lumot: foydalanuvchi, kurs, guruh, dars, davomat, chat.</summary>
internal static class Core
{
    // ====================================================================
    // FOYDALANUVCHILAR
    // ====================================================================

    /// <summary>
    /// <c>users</c> -> <c>Users</c>.
    ///
    /// ★ TELEFON: v2 da <c>PhoneNormalized</c> ustuni va FILTRLANGAN
    /// UNIKAL indeks bor — <c>+998 90 123 45 67</c> bilan
    /// <c>998901234567</c> BIR XIL hisoblanadi. Eski bazada bunday
    /// cheklov yo'q edi, ya'ni dublikatlar bo'lishi TABIIY (masalan
    /// aka-uka bir raqam bergan).
    ///
    /// Dublikat topilganda qator YO'QOLMAYDI: eng kichik <c>id</c> li
    /// foydalanuvchi normallashtirilgan raqamni oladi, qolganlarida
    /// <c>Phone</c> KO'RINISHDA QOLADI (xodim uni panelda ko'radi), lekin
    /// <c>PhoneNormalized</c> <c>NULL</c> yoziladi. Har bunday holat
    /// hisobotda ALOHIDA ko'rsatiladi — "jimgina yutilish" bo'lmasin.
    ///
    /// Normalizatsiya <see cref="User.NormalizePhone"/> orqali —
    /// v2 ning O'Z kodi. Yangisi yozilsa ikki manba bir-biridan ajralib
    /// ketardi va ko'chirilgan raqamlar ilova qidiruviga tushmasdi.
    /// </summary>
    public static TableSpec Users() => new()
    {
        Name = "users -> Users",
        SourceTable = "users",
        TargetTable = "Users",
        SourceCountSql = "SELECT COUNT(*) FROM users",
        SourceSql = """
            SELECT id, full_name, email, phone, telegram_id, password_hash,
                   role, is_active, payment_exempt, created_at
            FROM users
            ORDER BY id
            """,
        Columns =
        [
            Id(), Str("FullName"), Str("Email"), Str("PasswordHash"), Str("Phone"),
            Ref("TelegramId"), Num("Role"), Flag("IsActive"), Num("TokenVersion"),
            Moment("CreatedAt"), Moment("UpdatedAt"), Str("PhoneNormalized"), Flag("PaymentExempt"),
        ],
        Map = ctx =>
        {
            var id = ctx.Id;

            if (ctx.State.EmailDuplicateLosers.Contains(id))
            {
                return ctx.Skip(
                    "Elektron pochta kichik harfda dublikat bo'lib qoldi (v2 da UNIKAL)",
                    ctx.Text(2));
            }

            var email = (ctx.Text(2) ?? string.Empty).Trim().ToLowerInvariant();
            if (email.Length == 0)
                return ctx.Skip("Elektron pochta bo'sh — v2 da majburiy va unikal");

            if (!LegacyMap.TryRole(ctx.Text(6), out var role))
                return ctx.Skip("Rol tanilmadi", ctx.Text(6));

            var fullName = RowContext.Clip(ctx.Text(1)?.Trim(), 200);
            if (string.IsNullOrEmpty(fullName))
            {
                fullName = "Noma'lum";
                ctx.Fixed("Ism bo'sh edi — \"Noma'lum\" qo'yildi");
            }

            var phone = RowContext.Clip(ctx.Text(3), 32);
            string? normalized = null;

            if (ctx.State.PhoneDuplicateLosers.Contains(id))
            {
                ctx.Fixed(
                    "Telefon dublikati: PhoneNormalized NULL qoldirildi (Phone saqlandi)",
                    phone);
            }
            else
            {
                normalized = User.NormalizePhone(phone);
            }

            ctx.State.Add("users", id);

            return
            [
                id,
                fullName,
                RowContext.Clip(email, 256),
                RowContext.Clip(ctx.Text(5), 120) ?? string.Empty,
                phone,
                ctx.Int64OrNull(4),
                (int)role,
                ctx.Bool(7, true),
                0,                                  // TokenVersion — yangi hisoblagich
                ctx.Instant(9),
                null,                               // UpdatedAt
                normalized,
                ctx.Bool(8),
            ];
        },
    };

    /// <summary>
    /// <c>users.balance</c> -> <c>StudentAccounts</c>.
    ///
    /// v2 da balans FOYDALANUVCHI qatorida emas, alohida moliya
    /// entity'sida turadi (sabab <c>StudentAccount</c> izohida).
    /// Noldan farqli balansi bor foydalanuvchilar uchungina qator
    /// yaratiladi — nol balansli hisob ma'lumot emas, shovqin.
    /// </summary>
    public static TableSpec StudentAccounts() => new()
    {
        Name = "users.balance -> StudentAccounts",
        SourceTable = "users(balance)",
        TargetTable = "StudentAccounts",
        ConflictTarget = "\"StudentId\"",
        SourceCountSql = "SELECT COUNT(*) FROM users WHERE COALESCE(balance, 0) <> 0",
        SourceSql = """
            SELECT id, balance
            FROM users
            WHERE COALESCE(balance, 0) <> 0
            ORDER BY id
            """,
        Columns = [Ref("StudentId"), Money("Balance"), Moment("CreatedAt"), Moment("UpdatedAt")],
        Map = ctx =>
        {
            var id = ctx.Id;
            if (!ctx.State.Has("users", id))
                return ctx.Skip("Foydalanuvchi ko'chmagan");

            var balance = ctx.Money(1);
            if (balance < 0)
            {
                // v2 da `CK_StudentAccounts_Balance_NonNegative` bor: manfiy
                // balans "yashirin qarz" bo'lib, qarz hisobotida ko'rinmasdi.
                return ctx.Skip(
                    "Balans MANFIY — v2 cheklovi ruxsat bermaydi (qarz sifatida qo'lda kiritilsin)",
                    RowContext.Str(balance));
            }

            ctx.Report.AddMoney("StudentAccounts.Balance", balance);

            return [id, balance, Fallback, null];
        },
    };

    // ====================================================================
    // KURS DARAXTI
    // ====================================================================

    public static TableSpec Courses() => new()
    {
        Name = "courses -> Courses",
        SourceTable = "courses",
        TargetTable = "Courses",
        SourceCountSql = "SELECT COUNT(*) FROM courses",
        SourceSql = "SELECT id, name, description, is_active, position, created_at FROM courses ORDER BY id",
        Columns =
        [
            Id(), Str("Name"), Str("Description"), Flag("IsActive"), Num("Position"),
            Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            ctx.State.Add("courses", ctx.Id);

            return
            [
                ctx.Id,
                RowContext.Clip(ctx.Text(1), 200) ?? "Nomsiz kurs",
                RowContext.Clip(ctx.Text(2), 2000),
                ctx.Bool(3, true),
                ctx.Int32OrNull(4) ?? 0,
                ctx.Instant(5),
                null,
            ];
        },
    };

    /// <summary>
    /// <c>modules</c> -> <c>Modules</c>.
    ///
    /// ★ ESKI SXEMADA <c>course_id</c> IXTIYORIY, v2 DA MAJBURIY.
    /// Kursi yo'q modul ko'cha OLMAYDI — va u bilan birga butun daraxt
    /// (kurs darslari, ular ustidagi vazifalar, testlar, progress)
    /// tushib qoladi. Aynan shuning uchun bu holat <c>Preflight</c> da
    /// KO'CHIRISHDAN OLDIN topiladi va vosita to'xtaydi: to'g'ri yechim —
    /// eski bazada modullarga kurs biriktirib, keyin ko'chirish.
    /// </summary>
    public static TableSpec Modules() => new()
    {
        Name = "modules -> Modules",
        SourceTable = "modules",
        TargetTable = "Modules",
        SourceCountSql = "SELECT COUNT(*) FROM modules",
        SourceSql = "SELECT id, course_id, name, position FROM modules ORDER BY id",
        Columns = [Id(), Ref("CourseId"), Str("Name"), Num("Position"), Moment("CreatedAt"), Moment("UpdatedAt")],
        Map = ctx =>
        {
            var courseId = ctx.Int64OrNull(1);
            if (courseId is null)
                return ctx.Skip("Kursga bog'lanmagan (v2 da Modules.CourseId MAJBURIY)");

            if (!ctx.State.Has("courses", courseId.Value))
                return ctx.Skip("Kurs ko'chmagan", RowContext.Str(courseId.Value));

            ctx.State.Add("modules", ctx.Id);

            return
            [
                ctx.Id,
                courseId,
                RowContext.Clip(ctx.Text(2), 200) ?? "Nomsiz modul",
                ctx.Int32OrNull(3) ?? 0,
                Fallback,                    // eski jadvalda created_at YO'Q — taxmin
                null,
            ];
        },
    };

    /// <summary>
    /// <c>module_lessons</c> -> <c>ModuleLessons</c>.
    ///
    /// ⚠️ YO'QOTISH: eski ustunlar <c>video_url</c>, <c>is_exam</c>,
    /// <c>exam_image</c> ning v2 da MOS USTUNI YO'Q. Ya'ni kurs
    /// darslarining VIDEO HAVOLASI ko'chmaydi. Bu vosita xatosi emas —
    /// v2 sxemasida hali bunday maydon yo'q; hisobotda alohida
    /// ko'rsatiladi va qaror loyiha egasiga qoladi.
    /// </summary>
    public static TableSpec ModuleLessons() => new()
    {
        Name = "module_lessons -> ModuleLessons",
        SourceTable = "module_lessons",
        TargetTable = "ModuleLessons",
        SourceCountSql = "SELECT COUNT(*) FROM module_lessons",
        SourceSql = """
            SELECT id, module_id, name, position, description, duration_min
            FROM module_lessons
            ORDER BY id
            """,
        Columns =
        [
            Id(), Ref("ModuleId"), Str("Name"), Str("Description"), Num("Position"),
            Num("DurationMin"), Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var moduleId = ctx.Int64(1);
            if (!ctx.State.Has("modules", moduleId))
                return ctx.Skip("Modul ko'chmagan", RowContext.Str(moduleId));

            ctx.State.Add("module_lessons", ctx.Id);

            return
            [
                ctx.Id,
                moduleId,
                RowContext.Clip(ctx.Text(2), 200) ?? "Nomsiz dars",
                RowContext.Clip(ctx.Text(4), 2000),
                ctx.Int32OrNull(3) ?? 0,
                ctx.Int32OrNull(5),
                Fallback,                    // eski jadvalda created_at YO'Q — taxmin
                null,
            ];
        },
    };

    // ====================================================================
    // GURUHLAR
    // ====================================================================

    /// <summary>
    /// <c>groups</c> -> <c>Groups</c>.
    ///
    /// ★★ ENG XAVFLI KONVERTATSIYA SHU YERDA: <c>teacher_weekdays</c>.
    /// Eski Python konvensiyasi Dushanba = 0, .NET da esa Yakshanba = 0.
    /// Konvertatsiyasiz BARCHA guruhlarning dars kunlari bir kun oldinga
    /// siljirdi va jadval "to'g'ri" ko'rinib turaverardi. Formula:
    /// <c>dotnet = (python + 1) % 7</c> (<see cref="LegacyMap.TryWeekday"/>).
    /// Natija <c>Reconciler</c> da HAQIQIY dars sanalari bilan tekshiriladi.
    ///
    /// ★ <c>CuratorGroupId</c> BU BOSQICHDA <c>NULL</c> yoziladi: u
    /// <c>Groups</c> ning O'ZIGA havola qiladi va kerakli guruh hali
    /// yozilmagan bo'lishi mumkin. Havola ko'chirishdan keyin alohida
    /// <c>UPDATE</c> qadamida qo'yiladi (<c>Migrator.LinkCuratorGroups</c>).
    /// </summary>
    public static TableSpec Groups() => new()
    {
        Name = "groups -> Groups",
        SourceTable = "groups",
        TargetTable = "Groups",
        SourceCountSql = "SELECT COUNT(*) FROM groups",
        SourceSql = """
            SELECT id, name, teacher_id, assistant_id, course_id, course_start_date,
                   course_months, teacher_weekdays, teacher_start_time,
                   teacher_duration_min, group_type, status, record_enabled, created_at
            FROM groups
            ORDER BY id
            """,
        Columns =
        [
            Id(), Str("Name"), Ref("CourseId"), Ref("TeacherId"), Ref("AssistantId"),
            Day("StartDate"), Flag("IsActive"), Flag("RecordEnabled"),
            Moment("CreatedAt"), Moment("UpdatedAt"), Num("CourseMonths"),
            Ref("CuratorGroupId"), Num("DurationMinutes"), Clock("StartTime"),
            Num("Type"), IntArray("Weekdays"),
        ],
        Map = ctx =>
        {
            var id = ctx.Id;

            if (!LegacyMap.TryGroupType(ctx.Text(10), out var type))
                return ctx.Skip("Guruh turi tanilmadi", ctx.Text(10));

            if (!LegacyMap.TryGroupActive(ctx.Text(11), out var isActive))
            {
                isActive = false;
                ctx.Fixed("Guruh holati tanilmadi — nofaol deb belgilandi", ctx.Text(11));
            }

            // --- HAFTA KUNLARI ---
            var weekdays = new List<int>();
            foreach (var day in ctx.Array<short>(7))
            {
                if (LegacyMap.TryWeekday(day, out var dotnet))
                {
                    if (!weekdays.Contains(dotnet)) weekdays.Add(dotnet);
                }
                else
                {
                    ctx.Fixed("Hafta kuni qiymati 0..6 dan tashqarida — tashlandi", RowContext.Str(day));
                }
            }

            weekdays.Sort();

            if (weekdays.Count == 0)
                ctx.Fixed("Dars kunlari bo'sh — guruh jadvalsiz ko'chdi");

            // --- xodimlar va kurs havolalari ---
            var teacherId = ctx.Int64OrNull(2);
            if (teacherId is not null && !ctx.State.Has("users", teacherId.Value))
            {
                ctx.Fixed("Ustoz ko'chmagan — biriktiruv bo'shatildi", RowContext.Str(teacherId.Value));
                teacherId = null;
            }

            var assistantId = ctx.Int64OrNull(3);
            if (assistantId is not null && !ctx.State.Has("users", assistantId.Value))
            {
                ctx.Fixed("Kurator ko'chmagan — biriktiruv bo'shatildi", RowContext.Str(assistantId.Value));
                assistantId = null;
            }

            var courseId = ctx.Int64OrNull(4);
            if (courseId is not null && !ctx.State.Has("courses", courseId.Value))
            {
                ctx.Fixed("Kurs ko'chmagan — biriktiruv bo'shatildi", RowContext.Str(courseId.Value));
                courseId = null;
            }

            var months = ctx.Int32OrNull(6) ?? 8;
            if (months is < 1 or > 24)
                ctx.Fixed("Kurs oylari 1..24 dan tashqarida (v2 domen qoidasi)", RowContext.Str(months));

            var duration = ctx.Int32OrNull(9) ?? 80;
            if (duration is < 20 or > 240)
                ctx.Fixed("Dars davomiyligi 20..240 dan tashqarida (v2 domen qoidasi)", RowContext.Str(duration));

            ctx.State.Add("groups", id);

            return
            [
                id,
                RowContext.Clip(ctx.Text(1), 150) ?? "Nomsiz guruh",
                courseId,
                teacherId,
                assistantId,
                ctx.Date(5, DateOnly.FromDateTime(Fallback.UtcDateTime)),
                isActive,
                ctx.Bool(12),
                ctx.Instant(13),
                null,
                months,
                null,                                    // CuratorGroupId — keyingi qadamda
                duration,
                ctx.Time(8, new TimeOnly(19, 0)),
                (int)type,
                weekdays.ToArray(),
            ];
        },
    };

    /// <summary>
    /// <c>group_members</c> -> <c>GroupMembers</c>.
    ///
    /// ★ ESKI JADVALDA <c>id</c> USTUNI YO'Q (kalit
    /// <c>(group_id, student_id)</c>), shuning uchun idempotentlik
    /// TABIIY kalit orqali ta'minlanadi.
    ///
    /// ⚠️ YO'QOTISH: <c>archive_reason</c>, <c>archived_at</c>,
    /// <c>moved_to_group_id</c> ustunlarining v2 da mos maydoni yo'q.
    /// </summary>
    public static TableSpec GroupMembers() => new()
    {
        Name = "group_members -> GroupMembers",
        SourceTable = "group_members",
        TargetTable = "GroupMembers",
        ConflictTarget = "\"GroupId\", \"StudentId\"",
        SourceCountSql = "SELECT COUNT(*) FROM group_members",
        SourceSql = """
            SELECT group_id, student_id, joined_at, status, paused_until
            FROM group_members
            ORDER BY group_id, student_id
            """,
        Columns =
        [
            Ref("GroupId"), Ref("StudentId"), Num("Status"), Moment("JoinedAt"),
            Moment("CreatedAt"), Moment("UpdatedAt"), Day("PausedUntil"),
        ],
        Map = ctx =>
        {
            var groupId = ctx.Int64(0);
            var studentId = ctx.Int64(1);

            if (!ctx.State.Has("groups", groupId))
                return ctx.Skip("Guruh ko'chmagan", RowContext.Str(studentId));

            if (!ctx.State.Has("users", studentId))
                return ctx.Skip("O'quvchi ko'chmagan", RowContext.Str(studentId));

            if (!LegacyMap.TryMemberStatus(ctx.Text(3), out var status))
                return ctx.Skip("A'zolik holati tanilmadi", ctx.Text(3));

            var joined = ctx.InstantOrNull(2) ?? Fallback;

            return [groupId, studentId, (int)status, joined, joined, null, ctx.DateOrNull(4)];
        },
    };

    // ====================================================================
    // JONLI DARS VA DAVOMAT
    // ====================================================================

    /// <summary>
    /// <c>lessons</c> -> <c>LiveSessions</c>.
    ///
    /// ★ XONA NOMI v2 DA UNIKAL (<c>UX_LiveSessions_RoomName</c>). Eski
    /// tizimda nom <c>g{guruh}-l{tartib}</c> edi va jadval qayta
    /// tuzilganda tartib noldan sanalardi — ya'ni TAKRORLANGAN nomlar
    /// PROD BAZASIDA BO'LISHI MUMKIN (bu aynan v2 tuzatgan xato).
    /// Takror topilsa nom <c>mig-l{id}</c> ga almashtiriladi: bu
    /// determinstik (qayta yurgizishda o'zgarmaydi) va hisobotga tushadi.
    ///
    /// ★ VAQT: <c>scheduled_start</c> eski tizimda ANIQ UTC instant
    /// sifatida yozilgan (<c>scheduler._local_to_utc</c>) va ustun turi
    /// <c>TIMESTAMPTZ</c>. Shuning uchun HECH QANDAY siljitish
    /// qilinmaydi — qo'shilsa barcha dars vaqtlari 5 soatga surilardi.
    ///
    /// ⚠️ YO'QOTISH: <c>recording_status</c>, <c>recording_note</c>,
    /// <c>recording_error</c>, <c>egress_id</c>, <c>analysis_*</c>,
    /// <c>is_free</c>, <c>bo_timer_*</c> ustunlarining v2 da mosi yo'q.
    /// </summary>
    public static TableSpec LiveSessions() => new()
    {
        Name = "lessons -> LiveSessions",
        SourceTable = "lessons",
        TargetTable = "LiveSessions",
        SourceCountSql = "SELECT COUNT(*) FROM lessons",
        SourceSql = """
            SELECT id, group_id, host_id, title, type, status,
                   scheduled_start, scheduled_end, actual_start, actual_end,
                   livekit_room, recording_url, extended_min, created_at
            FROM lessons
            ORDER BY id
            """,
        Columns =
        [
            Id(), Ref("GroupId"), Ref("HostId"), Str("Title"), Num("Type"), Num("Status"),
            Moment("ScheduledStart"), Moment("ScheduledEnd"), Moment("ActualStart"),
            Moment("ActualEnd"), Str("RoomName"), Str("RecordingUrl"), Num("ExtendedMin"),
            Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var id = ctx.Id;
            var groupId = ctx.Int64(1);

            if (!ctx.State.Has("groups", groupId))
                return ctx.Skip("Guruh ko'chmagan", RowContext.Str(groupId));

            if (!LegacyMap.TrySessionType(ctx.Text(4), out var type))
                return ctx.Skip("Dars turi tanilmadi", ctx.Text(4));

            if (!LegacyMap.TrySessionStatus(ctx.Text(5), out var status))
                return ctx.Skip("Dars holati tanilmadi", ctx.Text(5));

            var hostId = ctx.Int64OrNull(2);
            if (hostId is not null && !ctx.State.Has("users", hostId.Value))
            {
                ctx.Fixed("Dars egasi ko'chmagan — bo'shatildi", RowContext.Str(hostId.Value));
                hostId = null;
            }

            var room = RowContext.Clip(ctx.Text(10)?.Trim(), 64);
            if (string.IsNullOrEmpty(room) || !ctx.State.UsedRoomNames.Add(room))
            {
                var replacement = "mig-l" + RowContext.Str(id);
                ctx.Fixed(
                    "LiveKit xona nomi bo'sh yoki TAKRORLANGAN (v2 da unikal) — yangi nom berildi",
                    room + " -> " + replacement);
                room = replacement;
                ctx.State.UsedRoomNames.Add(room);
            }

            var extended = ctx.Int32OrNull(12) ?? 0;
            if (extended is < 0 or > 10)
                ctx.Fixed("Uzaytirish 0..10 dan tashqarida (v2 domen qoidasi)", RowContext.Str(extended));

            ctx.State.Add("lessons", id);

            return
            [
                id,
                groupId,
                hostId,
                RowContext.Clip(ctx.Text(3), 200),
                (int)type,
                (int)status,
                ctx.Instant(6),
                ctx.Instant(7),
                ctx.InstantOrNull(8),
                ctx.InstantOrNull(9),
                room,
                RowContext.Clip(ctx.Text(11), 500),
                extended,
                ctx.Instant(13),
                null,
            ];
        },
    };

    /// <summary>
    /// <c>attendance</c> -> <c>Attendances</c>.
    ///
    /// ★★ BAYROQ TESKARI: eski ustun <c>auto_marked</c> ("avtomatik
    /// belgilangan"), v2 da esa <c>IsManual</c> ("qo'lda tuzatilgan").
    /// Ya'ni <c>IsManual = NOT auto_marked</c>. To'g'ridan-to'g'ri
    /// ko'chirilsa ma'no TESKARISIGA aylanardi va <c>Finalize()</c>
    /// qo'lda qo'yilgan baholarni qayta hisoblab yuborardi.
    ///
    /// ★ <c>joined_at</c> -> <c>FirstJoinAt</c>, <c>last_join_at</c> ->
    /// <c>LastJoinAt</c>: nomlar boshqa, ma'no bir xil (eski tizim ham
    /// aynan shu ikkilikni 0018-tuzatishda kiritgan).
    /// </summary>
    public static TableSpec Attendances() => new()
    {
        Name = "attendance -> Attendances",
        SourceTable = "attendance",
        TargetTable = "Attendances",
        SourceCountSql = "SELECT COUNT(*) FROM attendance",
        SourceSql = """
            SELECT id, lesson_id, student_id, status, joined_at, last_join_at,
                   left_at, duration_seconds, auto_marked
            FROM attendance
            ORDER BY id
            """,
        Columns =
        [
            Id(), Ref("SessionId"), Ref("StudentId"), Num("Status"), Moment("FirstJoinAt"),
            Moment("LastJoinAt"), Moment("LeftAt"), Num("DurationSeconds"), Flag("IsManual"),
            Moment("CreatedAt"), Moment("UpdatedAt"), Str("Reason"),
        ],
        Map = ctx =>
        {
            var lessonId = ctx.Int64(1);
            var studentId = ctx.Int64(2);

            if (!ctx.State.Has("lessons", lessonId))
                return ctx.Skip("Dars ko'chmagan", RowContext.Str(lessonId));

            if (!ctx.State.Has("users", studentId))
                return ctx.Skip("O'quvchi ko'chmagan", RowContext.Str(studentId));

            if (!LegacyMap.TryAttendanceStatus(ctx.Text(3), out var status))
                return ctx.Skip("Davomat holati tanilmadi", ctx.Text(3));

            var firstJoin = ctx.InstantOrNull(4);

            return
            [
                ctx.Id,
                lessonId,
                studentId,
                (int)status,
                firstJoin,
                ctx.InstantOrNull(5),
                ctx.InstantOrNull(6),
                ctx.Int32OrNull(7) ?? 0,
                !ctx.Bool(8, true),               // ⚠️ TESKARI: auto_marked -> IsManual
                firstJoin ?? Fallback,            // eski jadvalda created_at YO'Q — taxmin
                null,
                null,                             // Reason — eskisida bunday maydon yo'q
            ];
        },
    };

    // ====================================================================
    // YOZISHMALAR
    // ====================================================================

    /// <summary>
    /// <c>chat_messages</c> -> <c>GroupChatMessages</c>.
    ///
    /// ★★ IKKI OQIM AJRALGANICHA QOLADI. Eski <c>channel</c> ustuni
    /// <c>"teacher"</c> / <c>"assistant"</c> qiymatlarini oladi va
    /// o'quvchi ustozga hamda kuratorga ALOHIDA yozadi. Kanal tashlab
    /// yuborilsa ikki oqim qo'shilib ketardi va ustoz o'quvchining
    /// kuratorga atalgan savollarini o'qib qolardi. Xaritalash:
    /// <c>"assistant" -> Curator</c>.
    ///
    /// ★ <c>SenderName</c> va <c>SenderRole</c> XABAR BILAN BIRGA
    /// saqlanadi (v2 denormalizatsiyasi). Eski bazada bunday nusxa yo'q,
    /// shuning uchun ular <c>users</c> dan JOIN bilan olinadi.
    /// ⚠️ <c>SenderRole</c> — foydalanuvchining KO'CHIRISH PAYTIDAGI
    /// roli, xabar YOZILGAN PAYTDAGI roli emas: eski tizim rol tarixini
    /// umuman saqlamagan. Bu TAXMIN va hisobotda shunday belgilanadi.
    ///
    /// ⚠️ YO'QOTISH: eski <c>lesson_id</c> (xabar qaysi jonli darsda
    /// yozilgani) v2 <c>GroupChatMessage</c> da yo'q.
    /// </summary>
    public static TableSpec GroupChatMessages() => new()
    {
        Name = "chat_messages -> GroupChatMessages",
        SourceTable = "chat_messages",
        TargetTable = "GroupChatMessages",
        SourceCountSql = "SELECT COUNT(*) FROM chat_messages cm JOIN users u ON u.id = cm.sender_id",
        SourceSql = """
            SELECT cm.id, cm.group_id, cm.channel, cm.sender_id,
                   u.full_name, u.role, cm.body, cm.created_at
            FROM chat_messages cm
            JOIN users u ON u.id = cm.sender_id
            ORDER BY cm.id
            """,
        Columns =
        [
            Id(), Ref("GroupId"), Num("Channel"), Ref("SenderId"), Str("SenderName"),
            Num("SenderRole"), Str("Body"), Moment("SentAt"), Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var groupId = ctx.Int64(1);
            var senderId = ctx.Int64(3);

            if (!ctx.State.Has("groups", groupId))
                return ctx.Skip("Guruh ko'chmagan", RowContext.Str(groupId));

            if (!ctx.State.Has("users", senderId))
                return ctx.Skip("Yuboruvchi ko'chmagan", RowContext.Str(senderId));

            var channel = LegacyMap.Channel(ctx.Text(2), out var known);
            if (!known)
                ctx.Fixed("Chat kanali tanilmadi — ustoz oqimiga qo'yildi", ctx.Text(2));

            if (!LegacyMap.TryRole(ctx.Text(5), out var role))
                return ctx.Skip("Yuboruvchi roli tanilmadi", ctx.Text(5));

            var body = RowContext.Clip(ctx.Text(6)?.Trim(), 2000);
            if (string.IsNullOrEmpty(body))
                return ctx.Skip("Xabar matni bo'sh");

            var sentAt = ctx.Instant(7);

            return
            [
                ctx.Id,
                groupId,
                (int)channel,
                senderId,
                RowContext.Clip(ctx.Text(4)?.Trim(), 200) is { Length: > 0 } n ? n : "Noma'lum",
                (int)role,
                body,
                sentAt,
                sentAt,
                null,
            ];
        },
    };

    /// <summary><c>dm_messages</c> -> <c>DirectMessages</c> (kurator ↔ o'quvchi).</summary>
    public static TableSpec DirectMessages() => new()
    {
        Name = "dm_messages -> DirectMessages",
        SourceTable = "dm_messages",
        TargetTable = "DirectMessages",
        SourceCountSql = "SELECT COUNT(*) FROM dm_messages",
        SourceSql = """
            SELECT id, student_id, staff_id, sender_id, module_lesson_id,
                   body, read_by_student, read_by_staff, created_at
            FROM dm_messages
            ORDER BY id
            """,
        Columns =
        [
            Id(), Ref("StudentId"), Ref("StaffId"), Ref("SenderId"), Ref("ModuleLessonId"),
            Str("Body"), Flag("ReadByStudent"), Flag("ReadByStaff"), Moment("SentAt"),
            Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var studentId = ctx.Int64(1);
            var staffId = ctx.Int64(2);
            var senderId = ctx.Int64(3);

            if (!ctx.State.Has("users", studentId) || !ctx.State.Has("users", staffId))
                return ctx.Skip("Suhbat ishtirokchisi ko'chmagan");

            if (studentId == staffId)
                return ctx.Skip("O'quvchi va xodim bir xil (v2 domen qoidasi buni rad etadi)");

            if (senderId != studentId && senderId != staffId)
                return ctx.Skip("Yuboruvchi suhbat ishtirokchisi emas", RowContext.Str(senderId));

            var moduleLessonId = ctx.Int64OrNull(4);
            if (moduleLessonId is not null && !ctx.State.Has("module_lessons", moduleLessonId.Value))
            {
                ctx.Fixed("Kurs darsi ko'chmagan — kontekst bo'shatildi", RowContext.Str(moduleLessonId.Value));
                moduleLessonId = null;
            }

            var body = RowContext.Clip(ctx.Text(5)?.Trim(), 2000);
            if (string.IsNullOrEmpty(body))
                return ctx.Skip("Xabar matni bo'sh");

            var sentAt = ctx.Instant(8);

            return
            [
                ctx.Id, studentId, staffId, senderId, moduleLessonId, body,
                ctx.Bool(6), ctx.Bool(7), sentAt, sentAt, null,
            ];
        },
    };
}
