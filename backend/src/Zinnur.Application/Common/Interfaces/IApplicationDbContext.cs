using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.Common.Interfaces;

/// <summary>
/// Baza uchun PORT. Application qatlami faqat shu interfeysni biladi —
/// haqiqiy <c>DbContext</c> Infrastructure'da (Dependency Inversion).
/// Shu tufayli use-case'lar bazasiz, InMemory yoki mock bilan test qilinadi.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Course> Courses { get; }
    DbSet<CourseModule> Modules { get; }
    DbSet<ModuleLesson> ModuleLessons { get; }
    DbSet<Group> Groups { get; }
    DbSet<GroupMember> GroupMembers { get; }
    DbSet<LiveSession> LiveSessions { get; }
    DbSet<Attendance> Attendances { get; }
    DbSet<ChatMessage> ChatMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
