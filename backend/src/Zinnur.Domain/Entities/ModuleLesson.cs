using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Modul ichidagi VIDEO dars (o'quv kontenti).
/// Diqqat: bu <see cref="LiveSession"/> EMAS — u jonli dars.
/// </summary>
public class ModuleLesson : BaseEntity
{
    public long ModuleId { get; set; }

    public CourseModule? Module { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public int Position { get; set; }

    public int? DurationMin { get; set; }
}
