using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Import auditi: kim, qachon, nima o'zgardi (2026-10-29). Har `import`
/// (shu jumladan `--dry-run`) bitta qator yozadi.
/// </summary>
public class InteractiveImportRun : BaseEntity
{
    public required string BookCode { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>CLI'dan ishga tushirilsa <c>null</c> (tizim).</summary>
    public long? ActorId { get; set; }

    public bool DryRun { get; set; }

    /// <summary>"ok" yoki "failed".</summary>
    public required string Result { get; set; }

    /// <summary>`jsonb`: birlik bo'yicha natija (unchanged / new-version / error).</summary>
    public required string Report { get; set; }
}
