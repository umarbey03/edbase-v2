using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>Kurs — modullar va darslar to'plami (masalan "ATF").</summary>
public class Course : BaseEntity
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Ro'yxatdagi tartib.</summary>
    public int Position { get; set; }

    public ICollection<CourseModule> Modules { get; set; } = new List<CourseModule>();
}
