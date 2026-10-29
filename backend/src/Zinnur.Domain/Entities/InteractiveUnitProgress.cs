using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// O'quvchi × birlik progressi (2026-10-29): qaysi bloklar bajarildi va
/// birinchi urinishlar yig'indisi (tez ko'rsatish uchun denormal).
/// </summary>
public class InteractiveUnitProgress : BaseEntity
{
    public long StudentId { get; set; }

    public long UnitId { get; set; }

    public InteractiveUnit? Unit { get; set; }

    public long LastVersionId { get; set; }

    /// <summary>`jsonb`: bajarilgan blok id'lari (<c>["A0", "A1"]</c>).</summary>
    public string BlocksDone { get; set; } = "[]";

    /// <summary>Barcha bloklar bajarilgan payt — ball (`PointKind.InteractiveUnit`) shundan.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public int ExerciseRight { get; set; }

    public int ExerciseTotal { get; set; }
}
