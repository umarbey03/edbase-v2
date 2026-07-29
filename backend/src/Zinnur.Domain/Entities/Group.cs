using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>O'quv guruhi — o'quvchilar, ustoz va kurator biriktiriladi.</summary>
public class Group : BaseEntity
{
    public required string Name { get; set; }

    public long? CourseId { get; set; }

    public Course? Course { get; set; }

    public long? TeacherId { get; set; }

    public long? AssistantId { get; set; }

    public DateOnly StartDate { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Darslar LiveKit orqali yozib olinsinmi.</summary>
    public bool RecordEnabled { get; set; }

    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();

    /// <summary>Foydalanuvchi shu guruhning ustozi yoki kuratorimi.</summary>
    public bool IsStaff(long userId) => TeacherId == userId || AssistantId == userId;
}
