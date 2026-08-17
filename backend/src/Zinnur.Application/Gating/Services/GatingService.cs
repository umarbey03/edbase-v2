using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Gating.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Gating.Services;

/// <summary>
/// ========================================================================
/// GATING SERVISI — UNUMDORLIK ASOSIY TALAB
/// ========================================================================
///
/// ESKI TIZIM MUAMMOSI (`course_svc.py`): `lesson_unlocked_for()` bitta
/// darsni tekshirish uchun `course_tree()` ni chaqirardi, u esa modullar,
/// darslar, videolar, video manbalari, testlar, vazifalar, topshirilgan
/// javoblar, fayllar, urinishlar, progress va davomatni ALOHIDA-ALOHIDA
/// so'rovlar bilan tortardi — bitta test topshirish ~30 ta so'rovga tushardi.
/// Va bu HAR SO'ROVDA takrorlanardi.
///
/// SHU YERDAGI YECHIM UCH QATLAM:
///
///  1) BITTA DARS uchun ARZON yo'l (<see cref="GetLessonGateAsync"/>):
///     daraxt QURILMAYDI. Qoida faqat BEVOSITA oldingi darsga qaraydi,
///     shuning uchun 2 ta dars faktlari yetarli — jami ≤4 so'rov.
///
///  2) BUTUN DARAXT (<see cref="GetCourseGateAsync"/>) — BITTA so'rov:
///     har darsning vazifa/test/progress holati bitta `SELECT` ichidagi
///     ichki (correlated) so'rovlar bilan olinadi. N+1 yo'q.
///
///  3) KESH: natija Redis'da (<see cref="CacheTtl"/>) va bir so'rov ichida
///     xotirada (<c>_snapshot</c>) — ya'ni bitta HTTP so'rovida daraxt
///     ko'pi bilan BIR MARTA hisoblanadi.
///
/// KESHNI BEKOR QILISH — to'rt hodisa:
///   • video ko'rildi        -> <see cref="MarkVideoWatchedAsync"/>
///   • vazifa topshirildi    -> `AssignmentService` -> <see cref="InvalidateAsync"/>
///   • test topshirildi      -> `TestService`       -> <see cref="InvalidateAsync"/>
///   • ustoz sur'ati o'zgardi -> keshda SAQLANGAN sur'at har o'qishda
///     hozirgisi bilan taqqoslanadi (<see cref="ResolvePaceAsync"/>); farq
///     bo'lsa snapshot yaroqsiz deb tashlanadi.
///
/// NIMA UCHUN SUR'AT KALITGA EMAS, QIYMATGA QO'YILGAN: kalitga qo'yilsa
/// (`gating:7:pace3`) `RemoveAsync` uchun sur'atni QAYTA hisoblash kerak
/// bo'lardi va "eski" kalitlar Redis'da TTL tugaguncha yotardi. Barqaror
/// kalit + qiymat ichidagi tekshiruv: bitta `DEL` yetarli va sur'at
/// o'zgarishi DARHOL sezildi. Sur'at o'zi 2 ta indeksli arzon so'rov —
/// daraxtdan ~10 barobar arzon.
/// </summary>
public sealed class GatingService(
    IApplicationDbContext db,
    ICacheService cache,
    TimeProvider clock) : IGatingService
{
    /// <summary>
    /// So'rov ichidagi memo. Servis SCOPED, ya'ni bu maydon BITTA HTTP
    /// so'rovi umriga tegishli — instance boshqa so'rov bilan bo'lishilmaydi.
    /// </summary>
    private CourseGateDto? _snapshot;

    // ================================================================= o'qish

    public async Task<CourseGateDto> GetCourseGateAsync(
        long studentId, CancellationToken ct = default)
    {
        if (_snapshot is { } memo) return memo;

        var pace = await ResolvePaceAsync(studentId, ct);
        var key = CacheKey(studentId);

        var cached = await cache.GetAsync<CourseGateDto>(key, ct);

        // Keshdagi snapshot faqat SUR'AT va GURUH BOSHLANISH NUQTASI
        // o'zgarmagan bo'lsa haqiqiy.
        if (cached is not null && IsFresh(cached, pace))
        {
            _snapshot = cached;
            return cached;
        }

        var computed = await ComputeCourseGateAsync(studentId, pace, ct);

        await cache.SetAsync(key, computed, CacheTtl, ct);

        _snapshot = computed;
        return computed;
    }

    public async Task<LessonGateDto> GetLessonGateAsync(
        long studentId, long moduleLessonId, CancellationToken ct = default)
    {
        // 1) Snapshot allaqachon bor (shu so'rovda hisoblangan) — bazaga
        //    UMUMAN tegilmaydi: bu shunchaki ro'yxatdan qidirish.
        if (_snapshot is { } memo)
            return Find(memo, moduleLessonId);

        var pace = await ResolvePaceAsync(studentId, ct);

        // Guruhga kurs biriktirilmagan bo'lsa kurs kontenti KO'RINMAYDI.
        // (Eski tizim ham shunday: "birinchi kursga tushib qolish" YO'Q,
        // aks holda begona guruhning darslari ochilib qolardi.)
        if (pace.CourseId is not { } courseId)
            return Locked(moduleLessonId, index: 0, LessonLockReason.NotInCourse);

        var cached = await cache.GetAsync<CourseGateDto>(CacheKey(studentId), ct);

        if (cached is not null && IsFresh(cached, pace))
        {
            _snapshot = cached;
            return Find(cached, moduleLessonId);
        }

        // 2) ARZON YO'L — daraxt QURILMAYDI.
        //
        //    Faqat dars ID'lari (indeks-only so'rov, yuzlarcha `bigint`)
        //    tortiladi: shundan darsning tartib raqami va OLDINGI darsning
        //    ID'si topiladi. Keyin ikki dars uchun bitta faktlar so'rovi.
        //
        //    DIQQAT: natija KESHGA YOZILMAYDI — u to'liq daraxt emas.
        //    Yarim ma'lumotni keshga qo'yish keyingi to'liq so'rovni
        //    aldardi.
        var lessonIds = await OrderedLessonIds(courseId).ToListAsync(ct);
        var index = lessonIds.IndexOf(moduleLessonId);

        if (index < 0)
            return Locked(moduleLessonId, index: 0, LessonLockReason.NotInCourse);

        // ★ GURUH BOSHLANISH NUQTASI — ayni shu ro'yxatdan topiladi, ya'ni
        //   qo'shimcha so'rov YO'Q (ro'yxat baribir tortilgan).
        //
        //   ⚠️ BU YERDA "index < startIndex bo'lsa darhol qaytish" QILINMAYDI,
        //   garchi u bitta so'rovni tejaydigan ko'rinsa ham: o'sha holatda
        //   `UnlockedOverride` (o'quv bo'limi qo'lda ochgan dars) tekshirilmay
        //   qolardi va ARZON yo'l bilan DARAXT yo'li boshqa-boshqa javob
        //   berardi. Qoida BITTA joyda — `LessonGate.Evaluate` da.
        var startIndex = StartIndexOf(lessonIds, pace.VideoStartLessonId);

        var previousId = index > 0 ? lessonIds[index - 1] : (long?)null;

        var wanted = previousId is { } prev
            ? new[] { moduleLessonId, prev }
            : [moduleLessonId];

        var facts = await LessonFactsQuery(
                studentId, db.ModuleLessons.AsNoTracking().Where(l => wanted.Contains(l.Id)))
            .ToListAsync(ct);

        var current = facts.Find(f => f.LessonId == moduleLessonId)
            ?? throw new NotFoundException(nameof(ModuleLesson), moduleLessonId);

        var previous = previousId is { } id ? facts.Find(f => f.LessonId == id) : null;

        var (unlocked, reason) = LessonGate.Evaluate(
            index, current, previous, pace.TaughtLessonCount, startIndex);

        return LessonGate.Describe(index, current, unlocked, reason);
    }

    public async Task EnsureLessonUnlockedAsync(
        long studentId, long moduleLessonId, CancellationToken ct = default)
    {
        var gate = await GetLessonGateAsync(studentId, moduleLessonId, ct);

        if (gate.Unlocked) return;

        throw new ForbiddenException(gate.LockReason switch
        {
            LessonLockReason.TeacherPace =>
                "Bu dars hali ochilmagan — ustozingiz bu darsga yetib kelmagan.",

            LessonLockReason.PreviousIncomplete =>
                "Avval oldingi darsni tugating (video, vazifa va test).",

            // ★ Sabab ALOHIDA: bu darsni o'quvchi hech qachon o'tmaydi, ya'ni
            //   "oldingi darsni tugat" degan maslahat uni faqat chalg'itardi.
            LessonLockReason.BeforeGroupStart =>
                "Guruhingiz kursni bu qismdan boshlamagan — dars sizning o'quv "
                + "rejangizga kirmaydi. Kerak bo'lsa o'quv bo'limi uni alohida "
                + "ochib berishi mumkin.",

            _ => "Bu dars sizning kursingizga tegishli emas.",
        });
    }

    // ================================================================= yozish

    public async Task<LessonGateDto> MarkVideoWatchedAsync(
        long studentId, long moduleLessonId, CancellationToken ct = default)
    {
        // ════════════════════════════════════════════════════════════════
        // 🔴 DARS OCHIQ BO'LISHI SHART — AMALDAN OLDIN.
        // ════════════════════════════════════════════════════════════════
        //
        // Bu metod endi TASHQARIDAN (o'quvchining brauzeridan) chaqiriladi
        // (`ProgressController.MarkVideoWatched`). Tekshiruvsiz o'quvchi
        // hali OCHILMAGAN darslarning Id'sini ketma-ket yuborib, butun
        // kursning video shartini "bajarilgan" qilib qo'yardi — ya'ni
        // gating'ni O'ZI ochib olardi.
        //
        // ★ Bitta ARZON tekshiruv IKKALA savolga javob beradi: "bu dars
        //   mening kursimdami" va "dars ochiqmi" — begona kursning darsi
        //   `NotInCourse` bo'lib rad etiladi (`LessonAssetService` va
        //   `AssignmentService.SubmitAsync` dagi AYNI mulohaza).
        //
        // ⚠️ `SetOverrideAsync` da bunday tekshiruv YO'Q va bo'lmasligi
        //    ham kerak: uni O'QUV BO'LIMI chaqiradi va uning butun
        //    ma'nosi — aynan YOPIQ darsni ochish.
        await EnsureLessonUnlockedAsync(studentId, moduleLessonId, ct);

        var progress = await LoadOrCreateProgressAsync(studentId, moduleLessonId, ct);

        // Idempotent: birinchi ko'rilgan payt SAQLANADI (Domain qoidasi) —
        // o'quvchi videoni qayta ko'rsa progress orqaga ketmaydi.
        progress.MarkVideoWatched(clock.GetUtcNow());

        await db.SaveChangesAsync(ct);
        await InvalidateAsync(studentId, ct);

        return await GetLessonGateAsync(studentId, moduleLessonId, ct);
    }

    public async Task<LessonGateDto> SetOverrideAsync(
        long studentId,
        long moduleLessonId,
        bool unlocked,
        string? reason,
        long actorId,
        CancellationToken ct = default)
    {
        var progress = await LoadOrCreateProgressAsync(studentId, moduleLessonId, ct);

        progress.SetOverride(unlocked, reason, actorId, clock.GetUtcNow());

        await db.SaveChangesAsync(ct);
        await InvalidateAsync(studentId, ct);

        return await GetLessonGateAsync(studentId, moduleLessonId, ct);
    }

    public async Task InvalidateAsync(long studentId, CancellationToken ct = default)
    {
        // Xotiradagi memo ham tozalanadi — aks holda AYNI so'rov ichida
        // (masalan "topshir -> holatni qaytar") eski javob qaytardi.
        _snapshot = null;

        await cache.RemoveAsync(CacheKey(studentId), ct);
    }

    // ================================================================= hisoblash

    private async Task<CourseGateDto> ComputeCourseGateAsync(
        long studentId, PaceSnapshot pace, CancellationToken ct)
    {
        if (pace.CourseId is not { } courseId)
            return new CourseGateDto(null, 0, null, 0, []);

        // BITTA so'rov: barcha darslar + har birining faktlari.
        var facts = await LessonFactsQuery(studentId, OrderedLessons(courseId)).ToListAsync(ct);

        // ★ Boshlanish nuqtasi ALLAQACHON tortilgan ro'yxatdan topiladi —
        //   qo'shimcha so'rov yo'q (O(n) bitta o'tish).
        var startIndex = StartIndexOf(facts, pace.VideoStartLessonId);

        return new CourseGateDto(
            courseId,
            pace.TaughtLessonCount,
            pace.VideoStartLessonId,
            startIndex,
            LessonGate.EvaluateAll(facts, pace.TaughtLessonCount, startIndex));
    }

    /// <summary>
    /// Darsning gating faktlari — BITTA <c>SELECT</c> ichidagi ichki so'rovlar.
    ///
    /// Bu ifoda "bitta dars" va "butun daraxt" yo'llarida AYNI — shu tufayli
    /// ikki yo'l hech qachon boshqa-boshqa javob bermaydi (DRY).
    /// </summary>
    private IQueryable<LessonFacts> LessonFactsQuery(long studentId, IQueryable<ModuleLesson> lessons) =>
        lessons.Select(l => new LessonFacts(
            l.Id,

            // ════════════════════════════════════════════════════════════
            // VIDEO SHARTI — 2026-08-14 dan YOQIQ.
            // ════════════════════════════════════════════════════════════
            //
            // Ilgari bu yerda `VideoContentModelled = false` degan QOTIB
            // qolgan doimiy turardi: video kontenti hali modellashtirilmagan
            // edi, ya'ni "videosi bor" fakti HAMMA dars uchun `false` bo'lib,
            // gating'ning video oyog'i (`LessonGate.IsComplete` dagi
            // `!HasVideo || VideoWatched`) HECH QACHON ishlamasdi —
            // o'quvchi hech narsa ko'rmasdan darsni "tugatgan" bo'lardi.
            //
            // Endi uchala bo'lak ham joyida:
            //   • kontent  — `LessonAsset` (`Kind = Video`, `Position` bilan
            //                qismlarga bo'linadi);
            //   • ijro     — HMAC chiptali pleyer (`LessonAssetsController`);
            //   • yozuv    — `POST /progress/lessons/{id}/video-watched` ->
            //                `MarkVideoWatchedAsync` -> `VideoWatchedAt`.
            //
            // ★ NIMA UCHUN AYNAN `Kind == Video`: imtihon darsining rasmlari
            //   ham AYNI jadvalda yotadi (`LessonAsset` izohi). Tur bo'yicha
            //   filtrsiz imtihon darsi "videosi bor, ko'rilmagan" bo'lib
            //   ABADIY tugallanmagan qolardi va butun zanjirni qulflardi.
            //
            // 🔴 NIMA UCHUN HOZIR XAVFSIZ: yangi serverga, BO'SH baza bilan
            //   chiqilmoqda (loyiha egasi: "noldan ishlatiladi"). Ishlab
            //   turgan bazada bu o'zgarish videosi bor har bir darsni bir
            //   zumda "tugallanmagan" qilib, kursning o'rtasidagi
            //   o'quvchilarni QULFLAB qo'yardi (reja hujjatidagi 1-savol
            //   aynan shu edi).
            //
            // ★ KESHGA TA'SIRI: bu QO'SHIMCHA korrelyatsion `EXISTS`, ya'ni
            //   so'rovlar soni O'ZGARMAYDI (N+1 yo'q) va to'rt bekor qilish
            //   hodisasi ham tegilmadi. Faqat bitta yangi yo'l bor: ustoz
            //   darsga video QO'SHSA (yoki o'chirsa) fakt o'zgaradi, lekin
            //   `InvalidateAsync` chaqirilmaydi — kesh o'z TTL'i (60 s)
            //   bilan eskiradi. ATAYLAB: bu ustoz amali, o'quvchiniki emas,
            //   ya'ni "kimning keshini tozalash kerak" degan savol butun
            //   guruhga (yoki kursga) fan-out berardi; bir daqiqalik
            //   kechikish esa hech kimni qulflab qo'ymaydi.
            db.LessonAssets.Any(a => a.LessonId == l.Id
                                  && a.Kind == LessonAssetKind.Video),

            db.LessonProgress.Any(p => p.StudentId == studentId
                                    && p.ModuleLessonId == l.Id
                                    && p.VideoWatchedAt != null),

            // KURS vazifasi (guruh vazifasi gating'ga kirmaydi — u dars
            // kontentiga bog'lanmagan).
            db.Assignments.Any(a => a.ModuleLessonId == l.Id),

            db.Submissions.Any(s => s.StudentId == studentId
                                 && s.Assignment!.ModuleLessonId == l.Id),

            // Faqat E'LON QILINGAN test shart bo'ladi: qoralama test hech
            // kimga ko'rinmaydi, shuning uchun uni "yechish shart" deb
            // sanash darsni abadiy yopib qo'yardi.
            db.Tests.Any(t => t.ModuleLessonId == l.Id && t.IsPublished),

            db.TestAttempts.Any(x => x.StudentId == studentId
                                  && x.Status == AttemptStatus.Submitted
                                  && x.Test!.ModuleLessonId == l.Id),

            db.LessonProgress.Any(p => p.StudentId == studentId
                                    && p.ModuleLessonId == l.Id
                                    && p.UnlockedOverride)));

    // ================================================================= guruh sur'ati (hisobot)

    /// <inheritdoc />
    public async Task<GroupPaceDto?> GetGroupPaceAsync(long groupId, CancellationToken ct = default)
    {
        var group = await db.Groups
            .AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new { g.CourseId, g.VideoStartLessonId })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        // Kursi biriktirilmagan guruhda "qaysi darsga kelgani" MA'NOSIZ —
        // chaqiruvchi buni `null` bo'yicha ajratadi (port izohi).
        if (group?.CourseId is not { } courseId) return null;

        // ★ AYNI SO'ROV `ResolvePaceAsync` DAGI BILAN: yakunlangan USTOZ
        //   darslari soni. Kurator darslari sanalmaydi — ular kurs mavzusini
        //   oldinga surmaydi.
        var taught = await db.LiveSessions
            .AsNoTracking()
            .CountAsync(
                s => s.GroupId == groupId
                    && s.Type == SessionType.Teacher
                    && s.Status == SessionStatus.Ended,
                ct)
            .ConfigureAwait(false);

        // Faqat NOM va ID kerak — butun entity emas.
        var ordered = await OrderedLessons(courseId)
            .Select(l => new { l.Id, LessonName = l.Name, ModuleName = l.Module!.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var startIndex = StartIndexOf([.. ordered.Select(l => l.Id)], group.VideoStartLessonId);

        // Qoida `LessonGate` bilan AYNI: guruh `startIndex .. startIndex+N-1`
        // darslarini o'tgan, ya'ni oxirgi o'tilgani `startIndex+N-1`,
        // navbatdagisi esa `startIndex+N`.
        var lastIndex = startIndex + taught - 1;
        var nextIndex = startIndex + taught;

        // ⚠️ KURS OXIRIDAN OSHIB KETISHI MUMKIN: sur'at ORDINAL sanoq, ya'ni
        // guruh kursdagi darslardan KO'P dars o'tgan bo'lishi mumkin
        // (takrorlash darslari, qo'shimcha mashg'ulotlar). O'shanda joriy
        // pozitsiya OXIRGI darsga qisqartiriladi — aks holda u `null` bo'lib,
        // "kurs tugagan" holati "hali boshlanmagan" bilan bir xil ko'rinardi.
        if (ordered.Count > 0 && lastIndex >= ordered.Count)
            lastIndex = ordered.Count - 1;

        var last = lastIndex >= 0 && lastIndex < ordered.Count ? ordered[lastIndex] : null;
        var next = nextIndex >= 0 && nextIndex < ordered.Count ? ordered[nextIndex] : null;

        return new GroupPaceDto(
            TaughtLessonCount: taught,
            StartIndex: startIndex,
            TotalLessons: ordered.Count,
            CoveredLessons: Math.Min(startIndex + taught, ordered.Count),
            CurrentModuleName: last?.ModuleName,
            CurrentLessonName: last?.LessonName,
            NextModuleName: next?.ModuleName,
            NextLessonName: next?.LessonName);
    }

    /// <summary>
    /// Kurs darslari GLOBAL tartibda: modul tartibi, so'ng dars tartibi.
    /// Tartib BARQAROR bo'lishi shart (`Id` bilan yakunlanadi) — aks holda
    /// bir xil `Position` da darslar joyini almashtirib, gating natijasi
    /// so'rovdan so'rovga o'zgarardi.
    /// </summary>
    private IQueryable<ModuleLesson> OrderedLessons(long courseId) =>
        db.ModuleLessons
            .AsNoTracking()
            .Where(l => l.Module!.CourseId == courseId)
            .OrderBy(l => l.Module!.Position)
            .ThenBy(l => l.ModuleId)
            .ThenBy(l => l.Position)
            .ThenBy(l => l.Id);

    private IQueryable<long> OrderedLessonIds(long courseId) =>
        OrderedLessons(courseId).Select(l => l.Id);

    /// <summary>
    /// USTOZ SUR'ATI + o'quvchining kursi.
    ///
    /// Sur'at YAKUNLANGAN ustoz darslari soni bilan o'lchanadi. Eski tizimda
    /// bu `groups.taught_upto_lesson_id` ustuni edi — ustoz uni QO'LDA
    /// belgilardi va belgilashni unutsa butun guruh qulflanib qolardi.
    /// Yakunlangan darslar soni esa jadvaldan O'ZI kelib chiqadi: qo'lda
    /// yuritiladigan hisob yo'q, ya'ni unutish ham mumkin emas.
    ///
    /// O'quvchi ikki guruhda bo'lsa ENG ILDAM sur'at olinadi (uni qulflab
    /// qo'ymaslik uchun).
    /// </summary>
    private async Task<PaceSnapshot> ResolvePaceAsync(long studentId, CancellationToken ct)
    {
        var memberships = await db.GroupMembers
            .AsNoTracking()
            .Where(m => m.StudentId == studentId
                     && m.Status == MemberStatus.Active
                     && m.Group!.IsActive
                     && m.Group.CourseId != null)
            .Select(m => new
            {
                m.GroupId,
                CourseId = m.Group!.CourseId!.Value,
                m.Group.VideoStartLessonId,
            })
            .OrderBy(x => x.GroupId)
            .ToListAsync(ct);

        if (memberships.Count == 0)
            return new PaceSnapshot(null, 0, null);

        var courseId = memberships[0].CourseId;

        var sameCourse = memberships.FindAll(m => m.CourseId == courseId);

        var groupIds = sameCourse.ConvertAll(m => m.GroupId);

        // ★ VIDEO BOSHLANISH NUQTASI — ENG KENG (eng kam cheklovchi) qiymat.
        //
        // O'quvchi bitta kursning ikki guruhida bo'lishi mumkin. Agar
        // ULARDAN BIRORTASIDA cheklov yo'q bo'lsa (`null` = kurs boshidan),
        // umumiy natija ham `null` — ya'ni cheklov qo'yilmaydi. Aks holda
        // birlamchi guruhning (`GroupId` bo'yicha eng kichigi — kurs ham AYNI
        // shu qoida bilan tanlangan) qiymati olinadi.
        //
        // Bu SUR'AT bilan bir falsafada: sur'at MAKSIMUM olinadi, boshlanish
        // nuqtasi esa eng ERKIN olinadi — o'quvchini qulflab qo'ymaslik
        // muhimroq. Kurs va boshlanish nuqtasi bitta guruhdan kelishi ham
        // shart: aks holda "A guruhining kursi, B guruhining boshlanishi"
        // degan mavjud bo'lmagan holat hosil bo'lardi.
        var videoStartLessonId = sameCourse.Exists(m => m.VideoStartLessonId is null)
            ? null
            : sameCourse[0].VideoStartLessonId;

        // Guruh bo'yicha ALOHIDA sanaladi va MAKSIMUMI olinadi. Bitta
        // `COUNT(*)` bo'lsa ikki guruhdagi o'quvchi uchun sur'at ikki
        // barobar ko'rinardi va gating umuman ishlamasdi.
        var perGroup = await db.LiveSessions
            .AsNoTracking()
            .Where(s => groupIds.Contains(s.GroupId)
                     && s.Type == SessionType.Teacher
                     && s.Status == SessionStatus.Ended)
            .GroupBy(s => s.GroupId)
            .Select(g => g.Count())
            .ToListAsync(ct);

        return new PaceSnapshot(
            courseId,
            perGroup.Count == 0 ? 0 : perGroup.Max(),
            videoStartLessonId);
    }

    /// <summary>
    /// ★ BOSHLANISH DARSINING GLOBAL TARTIB RAQAMI.
    ///
    /// Cheklov yo'q bo'lsa (yoki dars kursda topilmasa) 0 qaytadi, ya'ni
    /// "kurs boshidan" — bugungi xatti-harakat.
    ///
    /// NIMA UCHUN TOPILMASA 0, XATO EMAS: FK <c>ON DELETE SET NULL</c> bo'lgani
    /// uchun bu holat amalda yuz bermaydi, lekin agar yuz bersa (masalan
    /// dars boshqa kursga ko'chirilsa) o'quvchi uchun BUTUN kurs qulflanib
    /// qolmasligi kerak. Xato tomonini tanlaganda "cheklovsiz" tanlanadi —
    /// qulflab qo'yish ancha qimmat nosozlik.
    /// </summary>
    private static int StartIndexOf(List<long> orderedLessonIds, long? videoStartLessonId) =>
        videoStartLessonId is { } startId
            ? NormalizeStartIndex(orderedLessonIds.IndexOf(startId))
            : 0;

    /// <inheritdoc cref="StartIndexOf(List{long}, long?)"/>
    private static int StartIndexOf(List<LessonFacts> orderedLessons, long? videoStartLessonId) =>
        videoStartLessonId is { } startId
            ? NormalizeStartIndex(orderedLessons.FindIndex(f => f.LessonId == startId))
            : 0;

    /// <summary>Topilmagan dars (−1) "cheklov yo'q" ga aylanadi — sabab yuqorida.</summary>
    private static int NormalizeStartIndex(int index) => index < 0 ? 0 : index;

    /// <summary>
    /// Keshdagi snapshot HALI HAQIQIYMI.
    ///
    /// Uch fakt taqqoslanadi: kurs, ustoz sur'ati va guruh boshlanish
    /// nuqtasi. Uchalasi ham <see cref="ResolvePaceAsync"/> dagi ARZON
    /// indeksli so'rovdan keladi, ya'ni tekshiruv daraxtni qayta qurishdan
    /// ~10 barobar arzon. Boshlanish nuqtasi shu ro'yxatga QO'SHILDI: aks
    /// holda o'quv bo'limi uni o'zgartirganda o'quvchi TTL tugaguncha
    /// (60 s) eski qulflar bilan qolardi.
    /// </summary>
    private static bool IsFresh(CourseGateDto cached, PaceSnapshot pace) =>
        cached.CourseId == pace.CourseId
        && cached.TaughtLessonCount == pace.TaughtLessonCount
        && cached.VideoStartLessonId == pace.VideoStartLessonId;

    // ================================================================= ichki yordamchi

    private async Task<LessonProgress> LoadOrCreateProgressAsync(
        long studentId, long moduleLessonId, CancellationToken ct)
    {
        if (!await db.ModuleLessons.AsNoTracking().AnyAsync(l => l.Id == moduleLessonId, ct))
            throw new NotFoundException(nameof(ModuleLesson), moduleLessonId);

        var progress = await db.LessonProgress
            .AsTracking()
            .FirstOrDefaultAsync(p => p.StudentId == studentId && p.ModuleLessonId == moduleLessonId, ct);

        if (progress is not null) return progress;

        progress = new LessonProgress
        {
            StudentId = studentId,
            ModuleLessonId = moduleLessonId,
        };

        db.LessonProgress.Add(progress);
        return progress;
    }

    private static LessonGateDto Find(CourseGateDto snapshot, long moduleLessonId)
    {
        foreach (var lesson in snapshot.Lessons)
        {
            if (lesson.LessonId == moduleLessonId) return lesson;
        }

        // Daraxtda yo'q = o'quvchining kursiga tegishli emas.
        return Locked(moduleLessonId, index: 0, LessonLockReason.NotInCourse);
    }

    private static LessonGateDto Locked(long lessonId, int index, LessonLockReason reason) =>
        new(lessonId, index, Unlocked: false, reason,
            Completed: false,
            HasVideo: false, VideoWatched: false,
            HasAssignment: false, AssignmentSubmitted: false,
            HasTest: false, TestTaken: false,
            UnlockedOverride: false);

    private static string CacheKey(long studentId) =>
        string.Create(CultureInfo.InvariantCulture, $"gating:course:{studentId}");

    /// <summary>
    /// Kesh muddati. 60 sekund ataylab QISQA: gating o'quvchi ko'rayotgan
    /// ro'yxatga ta'sir qiladi, shuning uchun eski ma'lumot uzoq yashamasin.
    /// Progress o'zgarganda kesh baribir DARHOL bekor qilinadi — TTL faqat
    /// zaxira (masalan boshqa instance ma'lumotni o'zgartirgan holat).
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <param name="TaughtLessonCount">Yakunlangan ustoz darslari soni (sur'at).</param>
    /// <param name="VideoStartLessonId">
    /// Guruh video darslarni qaysi darsdan boshlaydi. <c>null</c> — kurs
    /// boshidan (batafsil: <see cref="ResolvePaceAsync"/>).
    /// </param>
    private sealed record PaceSnapshot(
        long? CourseId,
        int TaughtLessonCount,
        long? VideoStartLessonId);
}
