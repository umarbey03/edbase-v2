using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Entities;

/// <summary>O'quvchining guruhdagi a'zoligi.</summary>
public class GroupMember : BaseEntity
{
    /// <summary>
    /// <see cref="Reason"/> uzunlik chegarasi — <c>LessonGrade.MaxCommentLength</c>
    /// bilan AYNI: bir-ikki jumlalik izoh, tergov emas.
    /// </summary>
    public const int MaxReasonLength = 500;

    public long GroupId { get; set; }

    public Group? Group { get; set; }

    public long StudentId { get; set; }

    public User? Student { get; set; }

    public MemberStatus Status { get; set; } = MemberStatus.Active;

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive => Status == MemberStatus.Active;

    // ---------------------------------------------------------------- TARIX (chiqarilgan/ko'chirilgan)

    /// <summary>
    /// A'zolik <see cref="MemberStatus.Stopped"/> yoki <see cref="MemberStatus.Moved"/>
    /// bo'lgan vaqt. <c>null</c> — hozir faol yoki pauzada (hech qachon
    /// chiqarilmagan/ko'chirilmagan).
    ///
    /// ★ QAYTA QO'SHILGANDA TOZALANADI (<c>GroupService.AddMemberAsync</c>):
    /// o'quvchi arxivdan chiqib faol bo'lsa, eski chiqarilish izi endi
    /// noto'g'ri "hozirgi holat"ni ko'rsatib qolmasin.
    /// </summary>
    public DateTimeOffset? LeftAt { get; set; }

    /// <summary>Chiqarish/ko'chirishni KIM bajargan (xodim).</summary>
    public long? LeftById { get; set; }

    public User? LeftBy { get; set; }

    /// <summary>
    /// <see cref="MemberStatus.Moved"/> bo'lsa — QAYSI guruhga ko'chirilgan.
    /// <see cref="MemberStatus.Stopped"/>da <c>null</c> (guruhdan chiqarilgan,
    /// boshqa guruhga emas).
    /// </summary>
    public long? MovedToGroupId { get; set; }

    public Group? MovedToGroup { get; set; }

    /// <summary>
    /// Ko'chirish sababi. Ko'chirishda MAJBURIY (loyiha egasi: *"guruhdan
    /// guruhga olib o'tishda sabab kiritilishi shart"*) — tekshiruv
    /// <c>GroupService.MoveMemberAsync</c>da. Oddiy chiqarishda (Stopped)
    /// IXTIYORIY qoladi — bu talab faqat ko'chirishga tegishli.
    /// </summary>
    public string? Reason { get; set; }
}
