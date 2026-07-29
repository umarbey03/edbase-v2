using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Entities;

/// <summary>O'quvchining guruhdagi a'zoligi.</summary>
public class GroupMember : BaseEntity
{
    public long GroupId { get; set; }

    public Group? Group { get; set; }

    public long StudentId { get; set; }

    public User? Student { get; set; }

    public MemberStatus Status { get; set; } = MemberStatus.Active;

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive => Status == MemberStatus.Active;
}
