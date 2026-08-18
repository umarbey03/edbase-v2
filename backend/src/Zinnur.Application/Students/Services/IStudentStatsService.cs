using Zinnur.Application.Students.Dtos;

namespace Zinnur.Application.Students.Services;

/// <summary>
/// O'quvchilar bo'yicha umumiy ko'rsatkichlar ("Foydalanuvchilar" paneli
/// kartalari). Faqat O'QIYDI.
///
/// Ma'no va chegaralar <see cref="StudentStatsDto"/> izohida.
/// </summary>
public interface IStudentStatsService
{
    Task<StudentStatsDto> GetAsync(long actorId, CancellationToken ct = default);
}
