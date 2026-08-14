using System.Linq.Expressions;
using Zinnur.Domain.Entities;

namespace Zinnur.Domain.Staffing;

/// <summary>
/// ========================================================================
/// GURUHDAGI IKKI O'RINDIQ: USTOZ va KURATOR (R33 + R40 ning YAGONA ta'rifi)
/// ========================================================================
///
/// ★ BU IKKI TALAB — BITTA SAVOL, IKKI MARTA BERILGAN:
///
///   R33 — *"vazifalarni tekshirishni dynamic qilish kerak, o'quv bo'limi
///          tanlaydi kurator yoki teacher tekshirishi kerakligini"*;
///   R40 — *"...javob berish dostupi dynamic bo'lsin, o'quv bo'limi
///          tarafidan tayinlanishi kerak"*.
///
/// Ikkalasi ham AYNI uchlikdan kelib chiqadi (<c>Group.TeacherId</c>,
/// <c>Group.AssistantId</c>, <c>Group.CuratorGroupId</c>), faqat bugun ular
/// IKKI XIL kod yo'lidan o'tadi: baholash <c>AssignmentService</c> dagi
/// qo'lda yozilgan OR-ifodadan, yozishma esa <c>CuratorDirectory</c> dan.
/// Shu sababli bugun "bu vazifani KURATOR tekshirsin" deyishning imkoni
/// YO'Q — OR ikkalasini bir xil ko'radi.
///
/// 🔴 IKKI MUSTAQIL MEXANIZM YOZISH — ANIQ XATO YO'LI. Ular albatta
/// ajralib ketardi: bugun "kurator tekshiradi" deb qo'yilgan guruhda
/// savollarga ustoz javob berib turardi va hech kim buni sezmasdi.
/// Shuning uchun QOIDA SHU YERDA, BIR MARTA. Ikkala servis ham ayni shu
/// ifodani ishlatadi.
///
/// ── ATAMALAR ────────────────────────────────────────────────────────────
///
/// O'RINDIQ (seat) — guruhdagi mas'uliyat o'rni, aniq odam emas. Har
/// guruhda IKKI o'rindiq bor va har biriga IKKI yo'l bilan odam
/// o'tirishi mumkin (<c>CuratorDirectory</c> dagi qoidaning aynan o'zi):
///
///   USTOZ o'rindig'i    = <c>Group.TeacherId</c>
///                         yoki <c>Group.CuratorGroup.TeacherId</c>
///   KURATOR o'rindig'i  = <c>Group.AssistantId</c>
///                         yoki <c>Group.CuratorGroup.AssistantId</c>
///
/// VAZIFA TURI (<see cref="StaffDuty"/>) — qaysi ish uchun so'ralyapti.
/// Har ish uchun guruhda ALOHIDA ustun bor, chunki markaz amalda ularni
/// ALOHIDA taqsimlaydi: kuchli kuratorga savollar, ustozga baholash.
/// </summary>
public static class StaffResponsibility
{
    // ====================================================================
    // 1) SO'ROV IFODASI — EF ga beriladi (`WHERE ... IN (SELECT ...)`)
    // ====================================================================

    /// <summary>
    /// «<paramref name="staffId"/> shu guruhda <paramref name="duty"/> uchun
    /// mas'ulmi» — <b>EF Core tarjima qiladigan</b> ifoda.
    ///
    /// ★ NIMA UCHUN <c>Expression</c>, oddiy <c>bool</c> metod EMAS:
    /// chaqiruvchilar buni ICHMA-ICH so'rov sifatida ishlatadi
    /// (<c>WHERE "StudentId" IN (SELECT ...)</c>). Metod bo'lsa EF uni
    /// tarjima qila olmasdi va butun jadval ilovaga tortilardi — 200
    /// o'quvchili kuratorda bu o'n minglab qator degani.
    ///
    /// ★ IFODA ICHIDA YORDAMCHI METOD CHAQIRILMAYDI (o'rindiqlar to'liq
    /// yozib chiqilgan): EF `g => Seat(g, id)` ni tarjima qila olmaydi.
    /// Takrorlanish ONGLI narx — muqobili ilovada filtrlash edi.
    /// </summary>
    /// <param name="assignmentOverride">
    /// Vazifa darajasidagi ISTISNO (R33). <c>null</c> — guruh ustuni
    /// o'qiladi. Faqat <see cref="StaffDuty.Grading"/> uchun ma'noli.
    /// </param>
    public static Expression<Func<Group, bool>> Predicate(
        long staffId, StaffDuty duty, GroupStaffRole? assignmentOverride = null)
    {
        // ── ACCESS: O'RINDIQNI UMUMAN AJRATMAYDI ────────────────────────
        //
        // 🔴 BU TARMOQ BUGUNGI IFODANING AYNAN O'ZI va u ATAYLAB
        // DINAMIK EMAS. "O'quvchining ishini KO'RISH" — baholash EMAS:
        // ustoz o'z guruhidagi javobning rasmini ocholmay qolsa, u
        // darsda nima bo'layotganini umuman bilmasdi. R33 esa faqat
        // "kim TEKSHIRADI" ni so'radi. Ikkalasini bitta ustunga
        // bog'lash talab qilinmagan cheklovni olib kirardi.
        if (duty == StaffDuty.Access)
        {
            return group =>
                group.TeacherId == staffId
                || group.AssistantId == staffId
                || (group.CuratorGroup != null
                    && (group.CuratorGroup.TeacherId == staffId
                        || group.CuratorGroup.AssistantId == staffId));
        }

        // Vazifa darajasidagi istisno guruh ustunini butunlay YENGADI.
        if (assignmentOverride is { } forced)
            return ForFixedRole(staffId, forced, WithFallback(duty));

        return duty == StaffDuty.Grading
            ? GradingFromGroupColumn(staffId)
            : QuestionsFromGroupColumn(staffId);
    }

    /// <summary>
    /// «Bu guruhda <paramref name="role"/> ko'rsatgan o'rindiqda ODAM
    /// BORMI» — sozlashda tekshiriladi (o'quv bo'limi bo'sh o'rindiqni
    /// tanlab qo'ymasin).
    ///
    /// <see cref="GroupStaffRole.Both"/> uchun: kamida bittasi bo'lsa yetadi.
    /// </summary>
    public static Expression<Func<Group, bool>> HasSeat(GroupStaffRole role) => role switch
    {
        GroupStaffRole.Teacher => group =>
            group.TeacherId != null
            || (group.CuratorGroup != null && group.CuratorGroup.TeacherId != null),

        GroupStaffRole.Assistant => group =>
            group.AssistantId != null
            || (group.CuratorGroup != null && group.CuratorGroup.AssistantId != null),

        _ => group =>
            group.TeacherId != null
            || group.AssistantId != null
            || (group.CuratorGroup != null
                && (group.CuratorGroup.TeacherId != null
                    || group.CuratorGroup.AssistantId != null)),
    };

    // ---------------------------------------------------------------- tarmoqlar

    /// <summary>
    /// BAHOLASH — rol GURUH ustunidan (<c>Group.AssignmentGraderRole</c>).
    ///
    /// ★ <see cref="GroupStaffRole.Both"/> tarmog'i BUGUNGI ifodaning
    /// AYNAN o'zi. Ustunning standart qiymati ham <c>Both</c>, ya'ni
    /// migratsiyadan keyin baholash xatti-harakati BIT-TO-BIT o'zgarmaydi.
    ///
    /// 🔴 ZAXIRA YO'L (fallback) FAQAT BU YERDA: <c>AssistantId</c>
    /// NULL bo'lishi mumkin, ya'ni "kurator tekshirsin" deb qo'yilgan
    /// guruhda kurator bo'lmasa TOPSHIRILGAN ISH BAHOLANMAY QOLARDI —
    /// o'quvchi javobini yuborgan, lekin hech kim unga yeta olmaydi.
    /// Shu sababli bo'sh o'rindiq ikkinchisiga o'tadi. Sozlashda esa
    /// bu holat 400 bilan oldindan to'siladi (<c>GroupService</c>) —
    /// zaxira yo'l faqat KEYIN buzilgan sozlama uchun (kurator guruhdan
    /// olib tashlandi).
    /// </summary>
    private static Expression<Func<Group, bool>> GradingFromGroupColumn(long staffId) =>
        group =>
            // ── BOTH: ikkala o'rindiq ham (bugungi xatti-harakat)
            (group.AssignmentGraderRole == GroupStaffRole.Both
                && (group.TeacherId == staffId
                    || group.AssistantId == staffId
                    || (group.CuratorGroup != null
                        && (group.CuratorGroup.TeacherId == staffId
                            || group.CuratorGroup.AssistantId == staffId))))

            // ── TEACHER: ustoz; ustoz o'rindig'i BO'SH bo'lsa — kurator
            || (group.AssignmentGraderRole == GroupStaffRole.Teacher
                && (group.TeacherId == staffId
                    || (group.CuratorGroup != null && group.CuratorGroup.TeacherId == staffId)
                    || (group.TeacherId == null
                        && (group.CuratorGroup == null || group.CuratorGroup.TeacherId == null)
                        && (group.AssistantId == staffId
                            || (group.CuratorGroup != null
                                && group.CuratorGroup.AssistantId == staffId)))))

            // ── ASSISTANT: kurator; kurator o'rindig'i BO'SH bo'lsa — ustoz
            || (group.AssignmentGraderRole == GroupStaffRole.Assistant
                && (group.AssistantId == staffId
                    || (group.CuratorGroup != null && group.CuratorGroup.AssistantId == staffId)
                    || (group.AssistantId == null
                        && (group.CuratorGroup == null || group.CuratorGroup.AssistantId == null)
                        && (group.TeacherId == staffId
                            || (group.CuratorGroup != null
                                && group.CuratorGroup.TeacherId == staffId)))));

    /// <summary>
    /// SAVOLGA JAVOB — rol GURUH ustunidan (<c>Group.QuestionResponderRole</c>).
    ///
    /// ★ STANDART QIYMAT <see cref="GroupStaffRole.Assistant"/>, ya'ni
    /// bugungi holat: o'quvchining suhbatdoshi — KURATOR
    /// (<c>CuratorDirectory.ResolveCuratorAsync</c>). Ustoz
    /// <c>/ustoz/savollar</c> da bo'sh ro'yxat ko'radi va migratsiyadan
    /// keyin ham shunday qoladi — sozlama o'zgartirilmaguncha.
    ///
    /// 🔴 ZAXIRA YO'L ATAYLAB YO'Q (baholashdan FARQI). Sabab: baholashda
    /// TOPSHIRILGAN ISH bor va u egasiz qolmasligi kerak; savolda esa
    /// hali hech narsa yozilmagan. Kurator biriktirilmagan guruhda
    /// o'quvchi bugun ham suhbatdosh ko'rmaydi va ekran buni ochiq
    /// aytadi ("Sizga hali kurator biriktirilmagan"). Zaxira yo'l
    /// qo'shilsa esa MIGRATSIYA KUNIYOQ kuratorsiz guruhlarning barcha
    /// savollari ustozlarga oqib ketardi — hech kim so'ramagan holda.
    /// </summary>
    private static Expression<Func<Group, bool>> QuestionsFromGroupColumn(long staffId) =>
        group =>
            (group.QuestionResponderRole == GroupStaffRole.Both
                && (group.TeacherId == staffId
                    || group.AssistantId == staffId
                    || (group.CuratorGroup != null
                        && (group.CuratorGroup.TeacherId == staffId
                            || group.CuratorGroup.AssistantId == staffId))))

            || (group.QuestionResponderRole == GroupStaffRole.Teacher
                && (group.TeacherId == staffId
                    || (group.CuratorGroup != null && group.CuratorGroup.TeacherId == staffId)))

            || (group.QuestionResponderRole == GroupStaffRole.Assistant
                && (group.AssistantId == staffId
                    || (group.CuratorGroup != null && group.CuratorGroup.AssistantId == staffId)));

    /// <summary>Vazifa darajasidagi istisno — rol guruh ustunidan O'QILMAYDI.</summary>
    private static Expression<Func<Group, bool>> ForFixedRole(
        long staffId, GroupStaffRole role, bool fallback)
    {
        if (role == GroupStaffRole.Both)
        {
            return group =>
                group.TeacherId == staffId
                || group.AssistantId == staffId
                || (group.CuratorGroup != null
                    && (group.CuratorGroup.TeacherId == staffId
                        || group.CuratorGroup.AssistantId == staffId));
        }

        if (role == GroupStaffRole.Teacher)
        {
            return fallback
                ? group =>
                    group.TeacherId == staffId
                    || (group.CuratorGroup != null && group.CuratorGroup.TeacherId == staffId)
                    || (group.TeacherId == null
                        && (group.CuratorGroup == null || group.CuratorGroup.TeacherId == null)
                        && (group.AssistantId == staffId
                            || (group.CuratorGroup != null
                                && group.CuratorGroup.AssistantId == staffId)))
                : group =>
                    group.TeacherId == staffId
                    || (group.CuratorGroup != null && group.CuratorGroup.TeacherId == staffId);
        }

        return fallback
            ? group =>
                group.AssistantId == staffId
                || (group.CuratorGroup != null && group.CuratorGroup.AssistantId == staffId)
                || (group.AssistantId == null
                    && (group.CuratorGroup == null || group.CuratorGroup.AssistantId == null)
                    && (group.TeacherId == staffId
                        || (group.CuratorGroup != null
                            && group.CuratorGroup.TeacherId == staffId)))
            : group =>
                group.AssistantId == staffId
                || (group.CuratorGroup != null && group.CuratorGroup.AssistantId == staffId);
    }

    // ====================================================================
    // 2) TESKARI YO'NALISH — "shu O'QUVCHIGA kim mas'ul", TARTIBI bilan
    // ====================================================================

    /// <summary>
    /// Bitta guruhning to'rt o'rindiq nomzodi (bazadan proyeksiya bilan
    /// olinadi — butun <see cref="Group"/> tortilmaydi).
    /// </summary>
    public readonly record struct StaffSeats(
        long? Teacher,
        long? Assistant,
        long? CuratorGroupTeacher,
        long? CuratorGroupAssistant);

    /// <summary>
    /// Guruh nomzodlarini MAS'ULIYAT TARTIBIDA qaytaradi (birinchisi —
    /// asosiy suhbatdosh).
    ///
    /// ★ BU <see cref="Predicate"/> BILAN BITTA QOIDANING IKKINCHI
    /// KO'RINISHI va ular ajralib ketmasligi SHART. Shuning uchun
    /// <c>StaffResponsibilityTests</c> ikkalasini BIR-BIRIGA QARSHI
    /// tekshiradi: har sozlama uchun
    /// <c>Predicate(x).Compile()(group) == Responsible(seats).Contains(x)</c>.
    /// Bittasi o'zgarib ikkinchisi qolsa test darhol yiqiladi.
    ///
    /// TARTIB: avval TO'G'RIDAN-TO'G'RI biriktirilgan odam, keyin kurator
    /// guruhi orqali kelgani — <c>CuratorDirectory</c> dagi bugungi
    /// tartibning aynan o'zi. <see cref="GroupStaffRole.Both"/> da esa
    /// KURATOR birinchi: bugun o'quvchining yagona suhbatdoshi kurator va
    /// u ro'yxatning boshida turishi kerak.
    /// </summary>
    public static IEnumerable<long> Responsible(
        StaffSeats seats, GroupStaffRole role, StaffDuty duty)
    {
        if (duty == StaffDuty.Access)
        {
            foreach (var id in Assistants(seats)) yield return id;
            foreach (var id in Teachers(seats)) yield return id;
            yield break;
        }

        if (role == GroupStaffRole.Both)
        {
            foreach (var id in Assistants(seats)) yield return id;
            foreach (var id in Teachers(seats)) yield return id;
            yield break;
        }

        var primary = role == GroupStaffRole.Teacher ? Teachers(seats) : Assistants(seats);
        var any = false;

        foreach (var id in primary)
        {
            any = true;
            yield return id;
        }

        // Zaxira yo'l — faqat baholashda va faqat o'rindiq BUTUNLAY bo'sh bo'lsa.
        if (any || !WithFallback(duty)) yield break;

        var backup = role == GroupStaffRole.Teacher ? Assistants(seats) : Teachers(seats);

        foreach (var id in backup) yield return id;
    }

    private static IEnumerable<long> Teachers(StaffSeats seats)
    {
        if (seats.Teacher is { } direct) yield return direct;
        if (seats.CuratorGroupTeacher is { } linked) yield return linked;
    }

    private static IEnumerable<long> Assistants(StaffSeats seats)
    {
        if (seats.Assistant is { } direct) yield return direct;
        if (seats.CuratorGroupAssistant is { } linked) yield return linked;
    }

    /// <summary>Zaxira yo'l qaysi ishda ishlaydi — sabab tarmoq izohlarida.</summary>
    private static bool WithFallback(StaffDuty duty) => duty == StaffDuty.Grading;
}
