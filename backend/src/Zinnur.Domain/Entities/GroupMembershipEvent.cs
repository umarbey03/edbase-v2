using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// A'ZOLIK TARIXI — O'CHMAYDIGAN HODISA JURNALI (2026-08-17)
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN YANGI JADVAL KERAK BO'LDI (aniqlangan muammo):
/// ilgari "qachon, nega chiqdi" ma'lumoti <c>GroupMember</c> qatorining
/// O'ZIDA (<c>LeftAt</c>, <c>Reason</c>) turardi. Uchta jiddiy kamchiligi
/// bor edi:
///   1) O'quvchi guruhga QAYTSA, <c>GroupService.AddMemberAsync</c> o'sha
///      maydonlarni NOLGA tushirardi — to'kilish izi butunlay yo'qolardi;
///   2) har (guruh, o'quvchi) juftligiga BITTA qator (unikal indeks), ya'ni
///      ikki marta chiqib ketgan o'quvchining birinchi ketishi ko'rinmasdi;
///   3) ustoz keyinroq almashtirilsa, eski to'kilish YANGI ustozga
///      yozilib qolardi.
///
/// Bu jadval FAQAT QO'SHILADI (append-only): hech qachon yangilanmaydi va
/// o'chirilmaydi. Shu tufayli to'kilish hisoboti tarixiy jihatdan barqaror.
///
/// ★ USTOZ SURATGA OLINADI (<see cref="TeacherId"/>): hodisa paytidagi
/// ustoz. <c>Group.TeacherId</c> o'zgaruvchan — unga ishonib bo'lmaydi.
///
/// ★ "PROBNIY" HISOBLANADIGAN QIYMAT, BELGI EMAS (loyiha egasi ta'rifi,
/// 2026-08-17): *"probniy deganda har bir guruhni birinchi 8 darsi
/// tushuniladi. 8 darsdan to'kilmasdan o'qib ketgan o'quvchilar aktiv
/// hisoblanadi"*. Shuning uchun bayroq saqlanmaydi —
/// <see cref="LessonsCompleted"/> (hodisa paytida o'quvchi nechta darsni
/// o'tagani) saqlanadi va <see cref="IsTrial"/> uni chegara bilan
/// solishtiradi. Ikki foydasi: (a) chegara o'zgarsa tarixni qayta
/// hisoblash mumkin, (b) hisobotda "3-darsda ketdi" kabi ANIQ ma'lumot
/// ko'rinadi.
/// </summary>
public class GroupMembershipEvent : BaseEntity
{
    /// <summary>Sabab uchun eng ko'p belgi (<c>GroupMember.MaxReasonLength</c> bilan AYNI).</summary>
    public const int MaxReasonLength = 500;

    /// <summary>
    /// "Probniy" davri necha dars davom etadi.
    ///
    /// Shu sondan KAM dars o'tab ketgan o'quvchi — sinov (probniy/demo)
    /// to'kilishi; shu son yoki undan ko'p o'tagan — AKTIV o'quvchining
    /// to'kilishi. Ikkisi markaz uchun butunlay boshqa ma'no: birinchisi
    /// "sotuv/moslashuv" muammosi, ikkinchisi "sifat/ushlab qolish" muammosi.
    /// </summary>
    public const int TrialLessonCount = 8;

    public long StudentId { get; set; }

    public User? Student { get; set; }

    public long GroupId { get; set; }

    public Group? Group { get; set; }

    /// <summary>
    /// Hodisa paytidagi guruh ustozi — SURAT (snapshot). <c>null</c> bo'lishi
    /// mumkin: guruhga hali ustoz tayinlanmagan bo'lsa.
    /// </summary>
    public long? TeacherId { get; set; }

    public MembershipEventKind Kind { get; set; }

    /// <summary>
    /// Sabab. <see cref="MembershipEventKind.Stopped"/>,
    /// <see cref="MembershipEventKind.Paused"/> va
    /// <see cref="MembershipEventKind.Moved"/> uchun MAJBURIY (loyiha egasi,
    /// 2026-08-17) — tekshiruv servis qatlamida, chunki qo'shilish/qaytish
    /// hodisalarida sabab TALAB QILINMAYDI.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>Faqat <see cref="MembershipEventKind.Moved"/> da — qaysi guruhga.</summary>
    public long? MovedToGroupId { get; set; }

    /// <summary>Amalni bajargan xodim.</summary>
    public long ActorId { get; set; }

    public User? Actor { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Hodisa paytida o'quvchi shu guruhda nechta YAKUNLANGAN darsni
    /// o'tagan. <see cref="IsTrial"/> shunga tayanadi.
    /// </summary>
    public int LessonsCompleted { get; set; }

    /// <summary>
    /// Hodisa "probniy" davrida sodir bo'ldimi (sabab sinf izohida).
    ///
    /// 🔴 BAZAGA TUSHMAYDI — hisoblanuvchi property (EF konfiguratsiyasida
    /// `Ignore`). Chegara o'zgarsa eski qatorlar ham AVTOMATIK qayta
    /// baholanadi.
    /// </summary>
    public bool IsTrial => LessonsCompleted < TrialLessonCount;

    /// <summary>
    /// Hodisa yozuvini yaratadi.
    ///
    /// ★ FABRIKA METOD: majburiy sabab qoidasi va bo'sh/uzun matn bilan
    /// ishlash BITTA joyda — beshta chaqiruv joyida takrorlansa, biri
    /// vaqt o'tib boshqacha ishlab ketardi.
    /// </summary>
    public static GroupMembershipEvent Create(
        long studentId,
        long groupId,
        long? teacherId,
        MembershipEventKind kind,
        string? reason,
        long? movedToGroupId,
        long actorId,
        int lessonsCompleted,
        DateTimeOffset now)
    {
        if (studentId <= 0) throw new DomainException("O'quvchi ko'rsatilmagan.");
        if (groupId <= 0) throw new DomainException("Guruh ko'rsatilmagan.");
        if (actorId <= 0) throw new DomainException("Amalni bajaruvchi ko'rsatilmagan.");

        var trimmed = reason?.Trim();

        if (RequiresReason(kind) && string.IsNullOrEmpty(trimmed))
            throw new DomainException("Bu amal uchun sabab ko'rsatilishi shart.");

        if (trimmed is { Length: > MaxReasonLength })
            trimmed = trimmed[..MaxReasonLength];

        return new GroupMembershipEvent
        {
            StudentId = studentId,
            GroupId = groupId,
            TeacherId = teacherId,
            Kind = kind,
            Reason = string.IsNullOrEmpty(trimmed) ? null : trimmed,
            MovedToGroupId = kind == MembershipEventKind.Moved ? movedToGroupId : null,
            ActorId = actorId,
            LessonsCompleted = Math.Max(0, lessonsCompleted),
            OccurredAt = now,
            CreatedAt = now,
        };
    }

    /// <summary>Qaysi hodisalarda sabab MAJBURIY.</summary>
    public static bool RequiresReason(MembershipEventKind kind) =>
        kind is MembershipEventKind.Stopped
             or MembershipEventKind.Paused
             or MembershipEventKind.Moved;
}
