using System.Security.Cryptography;
using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Jonli (video) dars. LiveKit xonasi bilan bir-biriga mos keladi.
/// Biznes qoidalari shu yerda — servis qatlamida takrorlanmaydi (DRY).
/// </summary>
public class LiveSession : BaseEntity
{
    /// <summary>Darsni rejadan necha daqiqa oldin ochish mumkin.</summary>
    public const int StartLeadMinutes = 5;

    /// <summary>Uzaytirish JAMI chegarasi (daqiqa).</summary>
    public const int MaxExtendMinutes = 10;

    public long GroupId { get; set; }

    public Group? Group { get; set; }

    /// <summary>Darsni o'tuvchi (ustoz yoki kurator). O'rinbosar tayinlansa — SHU maydon o'zgaradi (oylik ham shunga qarab hisoblanadi).</summary>
    public long? HostId { get; set; }

    /// <summary>
    /// Asl (rejadagi) ustoz — faqat <see cref="HostId"/> o'rinbosarga
    /// almashtirilganda to'ldiriladi. Audit/UI belgisi uchun ("bugun
    /// o'rinbosar o'tayapti") — oylik hisobiga TA'SIR QILMAYDI, u faqat
    /// <see cref="HostId"/>ga bog'liq (<c>LessonAccrualService</c>).
    /// </summary>
    public long? OriginalHostId { get; set; }

    public string? Title { get; set; }

    public SessionType Type { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Scheduled;

    public DateTimeOffset ScheduledStart { get; set; }

    public DateTimeOffset ScheduledEnd { get; set; }

    public DateTimeOffset? ActualStart { get; set; }

    public DateTimeOffset? ActualEnd { get; set; }

    /// <summary>
    /// LiveKit xona nomi. UNIKAL bo'lishi SHART.
    ///
    /// NIMA UCHUN: eski tizimda xona nomi `g{guruh}-l{tartib}` edi va jadval
    /// qayta tuzilganda tartib noldan sanalardi — ikki dars bir xil nom olib,
    /// webhook `MultipleResultsFound` bilan yiqilardi va davomat butunlay to'xtardi.
    /// </summary>
    public required string RoomName { get; set; }

    public string? RecordingUrl { get; set; }

    /// <summary>
    /// Bekor qilish sababi — faqat <see cref="SessionStatus.Cancelled"/> da
    /// ma'noli ("Bayram: Mustaqillik kuni", "Ustoz kasal"). Guruh jadvalida
    /// "nega bu dars yo'q?" degan savolga javob beradi.
    /// </summary>
    public string? CancelReason { get; set; }

    /// <summary>Uzaytirilgan daqiqalar (jami, maksimum <see cref="MaxExtendMinutes"/>).</summary>
    public int ExtendedMin { get; set; }

    /// <summary>
    /// BEPUL DARS (2026-08-16) — bu darsdan HECH BIR o'quvchidan pul
    /// yechilmaydi (`LessonAccrualService` tekshiradi, izoh o'sha yerda).
    ///
    /// `Attendance.IsExcused` dan ATAYLAB FARQ QILADI: excused —
    /// O'QUVCHINING holati ("sababli kelolmadi"), bu esa DARSNING o'zi
    /// haqidagi qaror ("sinov darsi", "texnik nosozlik bo'ldi") — barcha
    /// o'quvchilarga baravar tegishli.
    /// </summary>
    public bool IsFreeLesson { get; set; }

    /// <summary>Bepul deb belgilash sababi. Faqat <see cref="IsFreeLesson"/> da ma'noli.</summary>
    public string? FreeLessonReason { get; set; }

    /// <summary>
    /// Bepul darsda USTOZ/KURATOR HAM haq olmasinmi?
    ///
    /// Standart — <c>false</c>: dars real o'tilgan, ustoz mehnati baholanadi,
    /// faqat O'QUVCHIDAN pul yechilmaydi. Loyiha egasi (2026-08-16): ba'zan
    /// ikkalasi ham kerak — shu sabab alohida bayroq, `IsFreeLesson` bilan
    /// birga QO'YILADI (u yolg'iz holda ma'nosiz).
    /// </summary>
    public bool PayrollExcluded { get; set; }

    // ---------------------------------------------------------------- hisoblanuvchi

    public int PlannedDurationMinutes =>
        PlannedMinutesOf(ScheduledStart, ScheduledEnd);

    /// <summary>
    /// REJADAGI davomiylik — entity YUKLANMAGAN holat uchun ham.
    ///
    /// NIMA UCHUN STATIK NUSXA BOR: darslar jadvali (R31) darslarni to'liq
    /// entity sifatida emas, TOR proyeksiya bilan o'qiydi (faqat kerakli
    /// ustunlar), ya'ni <see cref="PlannedDurationMinutes"/> ga yetib
    /// bo'lmaydi. Formulani Application qatlamiga ko'chirib yozish esa
    /// qoidani IKKI joyga bo'lardi — bir kun kimdir uzaytirishni
    /// (<see cref="ExtendedMin"/>) hisobga olishga qaror qilsa, ikkinchi
    /// nusxa jimgina eskirardi. Naqsh yangi emas: <c>IsHost</c> ham AYNAN
    /// shu sababdan ikki shaklda mavjud (<c>LiveSessionService</c>).
    ///
    /// ★ Kamida 1 daqiqa: nol uzunlikdagi dars ma'lumotdagi xato bo'lardi
    /// va u davomat foizining MAXRAJIGA tushib, nolga bo'lish berardi.
    ///
    /// 🔴 BAZAGA TA'SIRI YO'Q: statik metod ham, hisoblanuvchi property ham
    /// EF modeliga tushmaydi — migratsiya TALAB QILINMAYDI.
    /// </summary>
    public static int PlannedMinutesOf(DateTimeOffset start, DateTimeOffset end) =>
        Math.Max(1, (int)(end - start).TotalMinutes);

    /// <summary>
    /// HAQIQIY davomiylik (daqiqa) — dars qancha DAVOM ETDI.
    ///
    /// <c>null</c> qaytadi, agar dars boshlanmagan yoki yakunlanmagan bo'lsa
    /// (<see cref="ActualStart"/> / <see cref="ActualEnd"/> bo'sh) — ya'ni
    /// "hali ma'lum emas" va "0 daqiqa" BIR-BIRIDAN farq qiladi. Nol bilan
    /// almashtirilsa, rejalashtirilgan dars jadvalda "0 daqiqa o'tdi" deb
    /// ko'rinardi.
    ///
    /// ★ <see cref="PlannedMinutesOf"/> DAN FARQLI o'laroq pastdan 1 ga
    /// yaxlitlanmaydi: 40 soniyada yopilgan dars uchun "0 daqiqa" —
    /// TO'G'RI javob va aynan shu narsa e'tiborni tortishi kerak.
    ///
    /// ⚠️ Teskari oraliq (<c>end &lt; start</c>) ham <c>null</c>: bu soat
    /// sozlanishi yoki qo'lda tuzatishdan qoladigan buzuq ma'lumot, uni
    /// manfiy son qilib ko'rsatish jadvalni tushunarsiz qilardi.
    /// </summary>
    public static int? ActualMinutesOf(DateTimeOffset? start, DateTimeOffset? end) =>
        start is { } from && end is { } to && to >= from
            ? (int)(to - from).TotalMinutes
            : null;

    /// <summary>Dars qachon avtomatik yakunlanishi kerak. Faqat jonli darsda mavjud.</summary>
    public DateTimeOffset? EndsAt =>
        Status != SessionStatus.Live || ActualStart is null
            ? null
            : ActualStart.Value.AddMinutes(PlannedDurationMinutes + ExtendedMin);

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// Takrorlanmas LiveKit xona nomini yaratadi.
    ///
    /// 8 BAYT tasodifiy qism (4 emas): jadval generatsiyasi bir guruhga 8 oylik
    /// darslarni BITTA paketda yaratadi, ya'ni bir sekundda minglab nom. 4 bayt
    /// bilan 10 000 nomda to'qnashuv ehtimoli ~1.2% edi — `UX_LiveSessions_RoomName`
    /// unikal indeksi tufayli bu vaqti-vaqti bilan yiqiladigan INSERT degani.
    /// 8 bayt bilan ehtimol amalda nolga tushadi.
    /// </summary>
    public static string GenerateRoomName() =>
        $"s-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";

    /// <summary>Darsni boshlaydi (idempotent).</summary>
    public void Start(DateTimeOffset now)
    {
        if (Status == SessionStatus.Ended)
            throw new DomainException("Dars allaqachon yakunlangan.");

        if (Status == SessionStatus.Cancelled)
            throw new DomainException("Bekor qilingan darsni boshlab bo'lmaydi.");

        if (Status == SessionStatus.Scheduled && now < ScheduledStart.AddMinutes(-StartLeadMinutes))
            throw new DomainException(
                $"Darsni boshlanishidan {StartLeadMinutes} daqiqa oldin boshlash mumkin.");

        Status = SessionStatus.Live;

        // MUHIM: faqat BIRINCHI boshlashda yoziladi.
        // Eski tizimda bu shartsiz qayta yozilardi va ustoz "Boshlash"ni qayta
        // bosса dars muddati yana 80 daqiqaga surilib, uzaytirish chegarasi
        // (10 daqiqa) ma'nosiz bo'lib qolardi.
        ActualStart ??= now;

        UpdatedAt = now;
    }

    public void End(DateTimeOffset now)
    {
        if (Status == SessionStatus.Ended) return;   // idempotent

        // BEKOR QILINGAN dars YAKUNLANMAYDI.
        //
        // Sabab: `Start()` bekor qilingan darsni rad etadi, lekin `End()` da bu
        // tekshiruv yo'q edi. Natijada `POST /live-sessions/{id}/end` bekor
        // qilingan darsni jimgina "Ended" ga o'tkazib, bekor qilish yozuvini
        // yo'q qilardi — va `Finalize()` umuman bo'lmagan dars uchun davomat
        // yozardi. Xuddi shu xavf avto-yakunlash fon vazifasida ham bor.
        if (Status == SessionStatus.Cancelled)
            throw new DomainException("Bekor qilingan darsni yakunlab bo'lmaydi.");

        Status = SessionStatus.Ended;
        ActualEnd = now;
        UpdatedAt = now;
    }

    /// <summary>Darsni uzaytiradi. Haqiqatan qo'shilgan daqiqalarni qaytaradi.</summary>
    public int Extend(int minutes, DateTimeOffset now)
    {
        if (Status != SessionStatus.Live)
            throw new DomainException("Faqat jonli darsni uzaytirish mumkin.");

        if (minutes <= 0)
            throw new DomainException("Uzaytirish musbat bo'lishi kerak.");

        var added = Math.Min(minutes, MaxExtendMinutes - ExtendedMin);
        if (added <= 0)
            throw new DomainException($"Uzaytirish limiti tugagan (maksimum {MaxExtendMinutes} daqiqa).");

        ExtendedMin += added;
        UpdatedAt = now;
        return added;
    }

    /// <summary>Muddati o'tganmi (fon vazifasi avto-yakunlash uchun).</summary>
    public bool IsOverdue(DateTimeOffset now) => EndsAt is { } end && now >= end;

    /// <summary>
    /// O'rinbosar ustozni tayinlaydi (<c>ISubstituteOfferService.RespondAsync</c>
    /// dan chaqiriladi, taklif qabul qilinganda). Faqat hali BOSHLANMAGAN
    /// darsda mumkin — jonli/yakunlangan darsda host allaqachon amalda
    /// ishtirok etgan, uni orqaga qaytarish tarixni buzardi.
    /// </summary>
    public void AssignSubstitute(long substituteTeacherId, DateTimeOffset now)
    {
        if (Status != SessionStatus.Scheduled)
            throw new DomainException("Faqat hali boshlanmagan darsga o'rinbosar tayinlash mumkin.");

        OriginalHostId ??= HostId;
        HostId = substituteTeacherId;
        UpdatedAt = now;
    }

    /// <summary>
    /// Darsni bekor qiladi (bayram yoki qo'lda, faqat Academic/Admin —
    /// ruxsat servis qatlamida). FAQAT hali boshlanmagan (<c>Scheduled</c>)
    /// darsni bekor qilish mumkin: <c>Live</c>/<c>Ended</c> darsda allaqachon
    /// davomat/chat tarixi bor, uni "bo'lmagan dars" deb belgilash tarixni
    /// buzardi. Idempotent — allaqachon bekor qilingan darsda jimgina
    /// qaytadi (masalan bir sana ikki marta bayram sifatida qayta ishlansa).
    /// </summary>
    public void Cancel(string? reason, DateTimeOffset now)
    {
        if (Status == SessionStatus.Cancelled) return;

        if (Status != SessionStatus.Scheduled)
            throw new DomainException("Faqat hali boshlanmagan darsni bekor qilish mumkin.");

        Status = SessionStatus.Cancelled;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = now;
    }

    /// <summary>
    /// Darsni bepul deb belgilaydi/bekor qiladi (faqat Academic/Admin —
    /// ruxsat servis qatlamida).
    ///
    /// Bekor qilingan darsda ma'nosiz — pul UMUMAN hisoblanmaydi. Dars
    /// ALLAQACHON yakunlangan (va hisoblangan) bo'lsa ham chaqirish mumkin:
    /// haqiqiy pul tuzatishi (`LessonAccrualService` reconciliation)
    /// Application qatlamida, chaqiruvchi shu bayroq o'zgargach uni
    /// ISHGA TUSHIRISHI kerak — bu METOD faqat bayroqni qo'yadi.
    /// </summary>
    public void SetFreeLesson(bool isFree, bool payrollExcluded, string? reason, DateTimeOffset now)
    {
        if (Status == SessionStatus.Cancelled)
            throw new DomainException("Bekor qilingan darsni bepul deb belgilab bo'lmaydi.");

        if (!isFree && payrollExcluded)
            throw new DomainException("Ustozni haqdan mahrum qilish faqat bepul dars uchun mumkin.");

        IsFreeLesson = isFree;
        PayrollExcluded = isFree && payrollExcluded;
        FreeLessonReason = isFree && !string.IsNullOrWhiteSpace(reason) ? reason.Trim() : null;
        UpdatedAt = now;
    }
}
