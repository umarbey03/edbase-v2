using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

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
