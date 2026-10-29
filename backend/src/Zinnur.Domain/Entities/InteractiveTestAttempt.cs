using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Birlik oxiridagi mini-test urinishi (2026-10-29).
///
/// ★ Birinchi topshirilgan urinish (<see cref="IsFirst"/>) ball beradi
///   (`PointKind.InteractiveTest`, 0–10); keyingilari mashq.
/// ★ Mavjud <c>Tests</c> jadvaliga ATAYLAB qo'shilmagan: u faqat variantli
///   savollarni biladi va dars testi sifatida `HasTest` faktini yoqib, oddiy
///   Mustaqil o'quvchining darsini ham qulflab qo'yardi.
/// </summary>
public class InteractiveTestAttempt : BaseEntity
{
    public long StudentId { get; set; }

    public long UnitId { get; set; }

    public long VersionId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public decimal? Score { get; set; }

    public decimal? MaxScore { get; set; }

    /// <summary>`jsonb`: savol bo'yicha javob va natija.</summary>
    public string Answers { get; set; } = "[]";

    public bool IsFirst { get; set; }
}
