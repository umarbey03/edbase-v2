using Zinnur.Application.Students.Dtos;

namespace Zinnur.Application.Students.Services;

/// <summary>
/// "MENING GURUHIM" oynasi uchun ma'lumot yig'adi — sabab va tarkib
/// <see cref="Dtos.ClassroomDto"/> izohida.
/// </summary>
public interface IStudentClassroomService
{
    /// <summary><paramref name="studentId"/> — TOKENDAN, so'rovdan emas.</summary>
    Task<ClassroomDto> GetAsync(long studentId, CancellationToken ct = default);
}
