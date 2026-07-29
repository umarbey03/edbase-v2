using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>Kurs ichidagi modul (masalan "Harf moduli").</summary>
public class CourseModule : BaseEntity
{
    public long CourseId { get; set; }

    public Course? Course { get; set; }

    public required string Name { get; set; }

    public int Position { get; set; }

    public ICollection<ModuleLesson> Lessons { get; set; } = new List<ModuleLesson>();
}
