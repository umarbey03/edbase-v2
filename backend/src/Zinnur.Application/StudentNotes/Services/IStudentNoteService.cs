using Zinnur.Application.StudentNotes.Dtos;

namespace Zinnur.Application.StudentNotes.Services;

/// <summary>
/// ========================================================================
/// O'QUVCHI HAQIDAGI ICHKI IZOHLAR (eski tizimdagi <c>student_notes</c>)
/// ========================================================================
///
/// RUXSAT QOIDASI (hammasi servis ICHIDA — controller faqat darvoza):
///  • <c>Academic</c>/<c>Admin</c> — hamma izohni ko'radi, yozadi,
///    tahrirlaydi va o'chiradi;
///  • <c>Teacher</c>/<c>Assistant</c> — faqat O'Z guruhidagi o'quvchiga
///    yozadi va faqat O'ZI yozgan izohni tahrirlaydi/o'chiradi;
///  • 🔴 <c>Student</c> — 403. Izohlar ICHKI eslatma ("kech qoladi",
///    "otasi bilan gaplashildi") va o'quvchiga ko'rsatilmaydi.
///
/// Har metod <c>studentId</c> ni ham, <c>noteId</c> ni ham oladi: izoh
/// o'quvchi ostidagi RESURS (<c>/users/{id}/notes/{noteId}</c>). Boshqa
/// o'quvchining izohi Id'si yuborilsa <c>404</c> qaytadi — begona izoh
/// haqida "bor" degan ma'lumot ham berilmaydi.
/// </summary>
public interface IStudentNoteService
{
    Task<IReadOnlyList<StudentNoteDto>> ListAsync(
        long studentId, long actorId, CancellationToken ct = default);

    Task<StudentNoteDto> CreateAsync(
        long studentId, CreateStudentNoteRequest request, long actorId, CancellationToken ct = default);

    Task<StudentNoteDto> UpdateAsync(
        long studentId, long noteId, UpdateStudentNoteRequest request, long actorId,
        CancellationToken ct = default);

    Task DeleteAsync(
        long studentId, long noteId, long actorId, CancellationToken ct = default);
}
