using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;
using Zinnur.Domain.Staffing;

namespace Zinnur.Domain.Entities;

/// <summary>
/// O'quv guruhi — o'quvchilar, ustoz va kurator biriktiriladi.
/// Jadval qoidasi ham shu yerda: undan butun kurs davomidagi dars jadvali
/// generatsiya qilinadi (<c>ScheduleGenerator</c>).
/// </summary>
public class Group : BaseEntity
{
    /// <summary>Dars davomiyligi (daqiqa) chegaralari.</summary>
    public const int MinDurationMinutes = 20;
    public const int MaxDurationMinutes = 240;

    /// <summary>Kurs davomiyligi (oy) chegaralari.</summary>
    public const int MinCourseMonths = 1;
    public const int MaxCourseMonths = 24;

    /// <summary>Oddiy guruh haftada ANIQ shuncha kun o'qiydi.</summary>
    public const int GroupWeekdayCount = 2;

    public required string Name { get; set; }

    public long? CourseId { get; set; }

    public Course? Course { get; set; }

    /* ===== R21b · GURUH KATEGORIYASI =====

       ★ NEGA ALOHIDA BLOK: bu faylga bir necha tarmoq AYNI vaqtda tegmoqda.
       Mavjud maydonlar orasiga qistirilgan qator merge paytida to'qnashuv
       beradi, uzluksiz blok esa bermaydi. */

    /// <summary>
    /// O'quv YO'NALISHI ("ATF", "Grammatika", "CEFR", "IELTS") — R21b.
    ///
    /// 🔴 <c>null</c> RUXSAT ETILADI VA BU MAJBURIY: talab kelganda bazada
    /// allaqachon 33 ta guruh bor edi va ularning birortasida kategoriya
    /// yo'q. Majburiy qilinsa migratsiya standart qiymat o'ylab topishga
    /// majbur bo'lardi — ya'ni 33 guruh YOLG'ON yorliq olardi va uni
    /// keyin qo'lda tozalash kerak bo'lardi.
    ///
    /// ⚠️ <see cref="CourseId"/> BILAN ARALASHTIRILMASIN: kurs — KONTENT
    /// (modul/dars/gating), bu esa YORLIQ. To'liq chegara va ular
    /// TAKRORLANIB qolishi mumkinligi haqidagi ochiq savol
    /// <see cref="GroupCategory"/> sinfi izohida.
    ///
    /// Kategoriya o'chirilsa FK <c>ON DELETE SET NULL</c> qiladi: guruh
    /// o'chib ketmaydi, shunchaki yorliqsiz qoladi (<c>GroupConfiguration</c>).
    /// </summary>
    public long? CategoryId { get; set; }

    public GroupCategory? Category { get; set; }

    /* ===== /R21b ===== */

    /// <summary>
    /// ========================================================================
    /// VIDEO DARSLAR QAYSI QISMDAN BOSHLANADI (guruh darajasidagi sozlama)
    /// ========================================================================
    ///
    /// Bitta kursga KO'P guruh biriktiriladi va ular bir vaqtda boshlamaydi:
    /// yarim yildan keyin ochilgan guruh kursning 1-modulidan emas, O'RTASIDAN
    /// boshlaydi. Bunday sozlama bo'lmasa o'quvchi hech qachon o'tmagan 20 ta
    /// darsni "tugatmagan" bo'lib turadi va sur'at nazorati (gating) BUTUN
    /// kursni qulflab qo'yadi — u zanjirni har doim 0-darsdan yuritadi.
    ///
    /// NIMA UCHUN GURUHDA, KURSDA EMAS: kurs UMUMIY — uni o'zgartirish
    /// o'ntalab guruhga tegadi. Boshlanish nuqtasi esa har guruhda BOSHQA.
    ///
    /// NIMA UCHUN MODUL EMAS, DARS: modul O'RTASIDAN boshlash real ehtiyoj,
    /// dars aniqligi modulni ham qoplaydi (modulning 1-darsi = modul boshi).
    ///
    /// QOIDALAR:
    ///   • dars <b>guruhning kursiga</b> tegishli bo'lishi shart — bu faqat
    ///     bazadan bilinadi, shuning uchun tekshiruv <c>GroupService</c> da (400);
    ///   • <see cref="CourseId"/> <c>null</c> bo'lsa bu ham <c>null</c>
    ///     (invariant shu yerda: <see cref="ValidateScheduleRule"/>);
    ///   • <c>null</c> = guruh kursni BOSHIDAN boshlaydi, ya'ni bugungi
    ///     xatti-harakat bit-to-bit o'zgarmaydi.
    ///
    /// Dars o'chirilsa FK <c>ON DELETE SET NULL</c> qiladi: guruh o'chib
    /// ketmaydi, shunchaki cheklov yo'qoladi (<c>GroupConfiguration</c>).
    /// </summary>
    public long? VideoStartLessonId { get; set; }

    public long? TeacherId { get; set; }

    public long? AssistantId { get; set; }

    public GroupType Type { get; set; } = GroupType.Group;

    /// <summary>
    /// Bu ustoz guruhi qaysi KURATOR guruhiga bog'langan.
    ///
    /// Kurator guruhida o'quvchilar to'g'ridan-to'g'ri a'zo BO'LMAYDI — ular
    /// shu havola orqali bog'langan ustoz guruhlaridan keladi. Ya'ni bitta
    /// kurator darsida bir necha ustoz guruhi birga qatnashadi.
    /// </summary>
    public long? CuratorGroupId { get; set; }

    public Group? CuratorGroup { get; set; }

    /// <summary>Kurs boshlanish sanasi (mahalliy sana).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Kurs necha oy davom etadi — jadval shu muddatga tuziladi.</summary>
    public int CourseMonths { get; set; } = 8;

    /// <summary>
    /// Dars kunlari.
    ///
    /// ⚠️ KONVENSIYA: .NET <see cref="DayOfWeek"/> — <b>Yakshanba = 0</b>,
    /// Dushanba = 1 ... Shanba = 6.
    ///
    /// Eski Python tizimi TESKARI konvensiyada edi (`date.weekday()`:
    /// <b>Dushanba = 0</b> ... Yakshanba = 6). Ma'lumot ko'chirishda bu
    /// BIR KUNLIK SILJISH beradi, shuning uchun konvertatsiya MAJBURIY:
    /// <c>dotnet = (python + 1) % 7</c>
    /// </summary>
    public List<DayOfWeek> Weekdays { get; set; } = [];

    /// <summary>
    /// Dars boshlanish soati — MAHALLIY (Toshkent) devor-vaqti.
    /// Jadval generatsiyasida aniq UTC instant'ga aylantiriladi.
    /// </summary>
    public TimeOnly StartTime { get; set; } = new(19, 0);

    /// <summary>Bitta darsning davomiyligi (daqiqa).</summary>
    public int DurationMinutes { get; set; } = 80;

    public bool IsActive { get; set; } = true;

    /// <summary>Darslar LiveKit orqali yozib olinsinmi.</summary>
    public bool RecordEnabled { get; set; }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// SHU GURUHNING YOZUVLARI O'QUVCHILARGA KO'RINADIMI (talab R5)
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// ★ <see cref="RecordEnabled"/> NING TABIIY JUFTI, LEKIN BOSHQA
    /// SAVOLGA JAVOB BERADI va ularni aralashtirish jiddiy xato bo'lardi:
    ///
    ///   • <c>RecordEnabled</c> — "dars YOZIB OLINSINMI" (Egress ishga
    ///     tushadimi). O'chirilsa fayl UMUMAN yaratilmaydi.
    ///   • bu bayroq — "yozilgan fayl O'QUVCHIGA ko'rsatilsinmi". Yozuv
    ///     baribir olinadi va o'quv bo'limi uni ko'radi.
    ///
    /// Ya'ni ikkinchisi arxivni saqlab, ko'rinishni yopish imkonini
    /// beradi — aynan R5 so'ragan narsa. Birinchisi bilan yopilsa arxiv
    /// ham yo'qolardi va qaror QAYTARIB BO'LMAYDIGAN bo'lardi.
    ///
    /// ── NIMA UCHUN GURUH DARAJASI KERAK ─────────────────────────────────
    ///
    /// O'quv bo'limi amalda AYNAN guruh bilan ishlaydi ("ustozi
    /// almashgan guruh", "qayta o'qitilayotgan oqim"). Bu kalitsiz bitta
    /// guruhni yopishning ikki yo'li qolardi: global sozlamani o'chirish
    /// (butun markazni yopardi) yoki o'nlab yozuvni birma-bir yopish.
    ///
    /// ★ STANDART <c>true</c> — bugungi xulq. Sabab batafsil
    /// <c>SessionRecording.IsVisibleToStudents</c> izohida (u ham
    /// <c>true</c>).
    /// </summary>
    public bool RecordingsVisibleToStudents { get; set; } = true;

    /* ===== R33 + R40 · KIM MAS'UL — O'QUV BO'LIMI TANLAYDI =====

       Alohida blok — bu faylga bir necha tarmoq parallel tegmoqda.

       ★ QOIDANING O'ZI BU YERDA EMAS: u <c>StaffResponsibility</c> da,
         chunki uni IKKI servis (baholash va yozishma) o'qiydi va uchinchi
         nusxa paydo bo'lishi kerak emas. Bu yerda faqat SAQLASH.

       ★ NIMA UCHUN GURUH DARAJASI — ikkala talab uchun ham. Shtat birligi
         AYNAN guruh: "bu guruhda kurator kuchli, savollarni u oladi",
         "bu oqimda ustoz o'zi baholaydi". KURS vazifasi esa
         (<c>Assignment.ModuleLessonId</c>) o'nlab guruhga tegishli va
         ularning har birida BOSHQA-BOSHQA odam o'tiradi — bayroq faqat
         vazifada bo'lsa bitta tanlov hammasini birdan hal qilib qo'yardi.

       ★ IKKI ALOHIDA USTUN, BITTA UMUMIY EMAS: markaz ularni amalda
         ALOHIDA taqsimlaydi (savollarga kurator, baholashga ustoz) va
         standart qiymatlari ham HAR XIL bo'lishi SHART — pastdagi
         izohlarga qarang. */

    /// <summary>
    /// R33 — bu guruhning topshirilgan ishlarini KIM tekshiradi.
    ///
    /// ★ STANDART <see cref="GroupStaffRole.Both"/> — BUGUNGI xatti-harakat
    /// (<c>AssignmentService.StudentIdsOfStaff</c> ustoz va kuratorni bitta
    /// OR ga qo'shadi). Ya'ni migratsiyadan keyin baholashda BIRORTA narsa
    /// o'zgarmaydi; o'quv bo'limi guruhni ochib tanlagandagina o'zgaradi.
    ///
    /// ⚠️ <c>Academic</c>/<c>Admin</c> BU USTUNGA BO'YSUNMAYDI — ular
    /// ustozning xatosini tuzatadi (sabab <c>AssignmentService</c> dagi
    /// ruxsat jadvalida). Bu ustun faqat <c>Teacher</c>/<c>Assistant</c>
    /// uchun.
    /// </summary>
    public GroupStaffRole AssignmentGraderRole { get; set; } = GroupStaffRole.Both;

    /// <summary>
    /// R40 — bu guruh o'quvchilarining darsga oid savollariga KIM javob
    /// beradi (shaxsiy yozishma suhbatdoshi).
    ///
    /// ★ STANDART <see cref="GroupStaffRole.Assistant"/> — BUGUNGI
    /// xatti-harakat: <c>CuratorDirectory</c> faqat kurator o'rindig'iga
    /// qaraydi va ustoz <c>/ustoz/savollar</c> da bo'sh ro'yxat ko'radi.
    /// Standart <c>Both</c> bo'lganda MIGRATSIYA KUNIYOQ har bir ustozning
    /// pochtasiga butun guruh oqib kelardi.
    ///
    /// 🔴 <see cref="GroupStaffRole.Both"/> TANLANSA o'quvchida IKKITA
    /// suhbatdosh bo'ladi (ustoz va kurator) — ya'ni ikkita ALOHIDA
    /// yozishma. Bu ONGLI narx va uning xavfsizlik tomoni yaxshi:
    /// <c>DirectMessage</c> kaliti <c>(StudentId, StaffId)</c> bo'lgani
    /// uchun ustoz kuratorning yozishmasini KO'RA OLMAYDI va aksincha.
    /// </summary>
    public GroupStaffRole QuestionResponderRole { get; set; } = GroupStaffRole.Assistant;

    /* ===== /R33 + R40 ===== */

    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();

    // ---------------------------------------------------------------- hisoblanuvchi

    /// <summary>Kurator guruhimi — dars turi va hostni shu belgilaydi.</summary>
    public bool IsCuratorGroup => Type == GroupType.Curator;

    /// <summary>Bu guruh darslarini kim o'tadi.</summary>
    public long? HostId => IsCuratorGroup ? AssistantId : TeacherId;

    /// <summary>Jadval qaysi turdagi darslar hosil qiladi.</summary>
    public SessionType PlannedSessionType =>
        IsCuratorGroup ? SessionType.Assistant : SessionType.Teacher;

    /// <summary>Jadval oxiri (kurs tugash sanasi).</summary>
    public DateOnly EndDate => StartDate.AddMonths(CourseMonths);

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>Foydalanuvchi shu guruhning ustozi yoki kuratorimi.</summary>
    public bool IsStaff(long userId) => TeacherId == userId || AssistantId == userId;

    /// <summary>
    /// Jadval qoidasini tekshiradi. Noto'g'ri bo'lsa <see cref="DomainException"/>.
    ///
    /// Hafta kunlari soni guruh TURIGA bog'liq. Eski tizimda bu bug edi:
    /// HAMMA tur "aniq 2 kun" shartiga tushardi, shu jumladan kurator guruhi
    /// ham. Kurator darslari haftada 3 kun bo'lgani uchun uni saqlashning
    /// umuman imkoni yo'q edi — 400 xato qaytardi.
    /// </summary>
    public void ValidateScheduleRule()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new DomainException("Guruh nomi kiritilishi shart.");

        if (Weekdays.Count == 0)
            throw new DomainException("Kamida bitta dars kuni tanlanishi kerak.");

        if (Weekdays.Distinct().Count() != Weekdays.Count)
            throw new DomainException("Dars kunlari takrorlanmasligi kerak.");

        if (Type == GroupType.Group && Weekdays.Count != GroupWeekdayCount)
            throw new DomainException(
                $"Oddiy guruh darslari haftada aniq {GroupWeekdayCount} kun bo'lishi kerak.");

        if (DurationMinutes is < MinDurationMinutes or > MaxDurationMinutes)
            throw new DomainException(
                $"Dars davomiyligi {MinDurationMinutes}..{MaxDurationMinutes} daqiqa oralig'ida bo'lsin.");

        if (CourseMonths is < MinCourseMonths or > MaxCourseMonths)
            throw new DomainException(
                $"Kurs davomiyligi {MinCourseMonths}..{MaxCourseMonths} oy oralig'ida bo'lsin.");

        if (StartDate == default)
            throw new DomainException("Kurs boshlanish sanasi kiritilishi shart.");

        // Kurator guruhida ustoz emas, KURATOR mas'ul
        if (IsCuratorGroup && AssistantId is null)
            throw new DomainException("Kurator guruhi uchun kurator (yordamchi) biriktirilishi shart.");

        // Kurator guruhi boshqa kurator guruhiga bog'lanmaydi (halqa bo'lmasin)
        if (IsCuratorGroup && CuratorGroupId is not null)
            throw new DomainException("Kurator guruhi boshqa kurator guruhiga bog'lanmaydi.");

        // Guruh o'zini o'ziga bog'lamasin
        if (CuratorGroupId == Id && Id != 0)
            throw new DomainException("Guruh o'zini o'ziga bog'lay olmaydi.");

        // VIDEO BOSHLANISH NUQTASI kurssiz ma'nosiz: "qaysi kursning qaysi
        // darsi?" degan savol javobsiz qolardi va gating uni hech qachon
        // tanib olmasdi (dars hech bir kursga tegishli bo'lmay ko'rinardi).
        //
        // Bu FAQAT oxirgi himoya: `GroupService` ayni holatni undan OLDIN
        // tutib, 400 va `problem.errors` bilan qaytaradi (foydalanuvchi uchun
        // tushunarli xato). Shu yerdagi tekshiruv servisdan tashqari
        // yo'llarni (seed, fon vazifasi, kelajakdagi import) qo'riqlaydi.
        if (CourseId is null && VideoStartLessonId is not null)
        {
            throw new DomainException(
                "Guruhga kurs biriktirilmagan — video darslar boshlanish nuqtasini "
                + "tanlash uchun avval kurs biriktirilishi kerak.");
        }
    }

    /// <summary>
    /// Jadvalga ta'sir qiluvchi maydonlar o'zgardimi.
    ///
    /// NIMA UCHUN KERAK: eski tizimda guruh tahrirlanganda jadval SHARTSIZ
    /// qayta tuzilardi — kursni yoki kuratorni almashtirsangiz ham butun
    /// kelajak jadval o'chib qayta yaratilardi, dars ID'lari o'zgarib
    /// tashqi havolalar buzilardi.
    /// </summary>
    public bool ScheduleRuleDiffersFrom(
        DateOnly startDate,
        IReadOnlyCollection<DayOfWeek> weekdays,
        TimeOnly startTime,
        int durationMinutes,
        int courseMonths,
        GroupType type)
    {
        ArgumentNullException.ThrowIfNull(weekdays);

        return StartDate != startDate
            || StartTime != startTime
            || DurationMinutes != durationMinutes
            || CourseMonths != courseMonths
            || Type != type
            || !Weekdays.Order().SequenceEqual(weekdays.Order());
    }
}
