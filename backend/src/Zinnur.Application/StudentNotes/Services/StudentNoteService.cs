using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.StudentNotes.Dtos;
using Zinnur.Application.Users;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.StudentNotes.Services;

/// <summary>
/// <see cref="IStudentNoteService"/> ning amalga oshirilishi.
/// HTTP haqida hech nima bilmaydi — faqat Application xatolarini ko'taradi.
/// </summary>
public sealed class StudentNoteService(
    IApplicationDbContext db,
    TimeProvider clock) : IStudentNoteService
{
    public async Task<IReadOnlyList<StudentNoteDto>> ListAsync(
        long studentId, long actorId, CancellationToken ct = default)
    {
        var (audience, _) = await AuthorizeAsync(studentId, actorId, ct);

        return await StudentNoteQueries
            .Project(db, studentId, actorId, canEditAll: audience == StudentAudience.Manage)
            .ToListAsync(ct);
    }

    public async Task<StudentNoteDto> CreateAsync(
        long studentId, CreateStudentNoteRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (audience, _) = await AuthorizeAsync(studentId, actorId, ct);

        await EnsureGroupContextAsync(studentId, request.GroupId, ct);

        // Matn tekshiruvi Domain'da (`StudentNote.Create` -> 409): chegara
        // entity bilan birga turadi va uni ikki joyda yozib, keyin bittasini
        // o'zgartirib qo'yish mumkin emas.
        var note = StudentNote.Create(
            studentId, actorId, request.GroupId, request.Body, clock.GetUtcNow());

        db.StudentNotes.Add(note);
        await db.SaveChangesAsync(ct);

        return await GetDtoAsync(note.Id, studentId, actorId, audience, ct);
    }

    public async Task<StudentNoteDto> UpdateAsync(
        long studentId, long noteId, UpdateStudentNoteRequest request, long actorId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (audience, _) = await AuthorizeAsync(studentId, actorId, ct);

        var note = await LoadForWriteAsync(studentId, noteId, actorId, audience, ct);

        note.Edit(request.Body, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        return await GetDtoAsync(note.Id, studentId, actorId, audience, ct);
    }

    public async Task DeleteAsync(
        long studentId, long noteId, long actorId, CancellationToken ct = default)
    {
        var (audience, _) = await AuthorizeAsync(studentId, actorId, ct);

        var note = await LoadForWriteAsync(studentId, noteId, actorId, audience, ct);

        // QATTIQ o'chirish (yumshoq emas): izoh — xodimning shaxsiy ish
        // yozuvi, unga havola qiladigan boshqa yozuv yo'q. "O'chirilgan
        // izohlar" ro'yxati esa aynan yashirishni ISTAGAN odamga foydasiz
        // ma'lumot bo'lardi.
        db.StudentNotes.Remove(note);
        await db.SaveChangesAsync(ct);
    }

    // ================================================================= RUXSAT

    /// <summary>
    /// Izohlar bo'yicha ruxsat.
    ///
    /// 🔴 <c>Student</c> uchun 403 AYNAN SHU YERDA: <see cref="StudentAccess"/>
    /// o'quvchiga o'z profilini ko'rishga ruxsat beradi (bu to'g'ri —
    /// u o'z to'lovini va natijalarini ko'radi), lekin IZOHLAR undan
    /// yopiq. Ya'ni umumiy qoidaga qo'shimcha, izohlarga XOS chegara.
    /// </summary>
    private async Task<(StudentAudience Audience, StudentSubject Student)> AuthorizeAsync(
        long studentId, long actorId, CancellationToken ct)
    {
        var (student, audience) = await StudentAccess.AuthorizeAsync(db, actorId, studentId, ct);

        if (audience == StudentAudience.Self)
        {
            throw new ForbiddenException(
                "Izohlar — xodimlarning ichki yozuvlari va o'quvchiga ko'rsatilmaydi.");
        }

        return (audience, student);
    }

    /// <summary>
    /// Tahrirlash/o'chirish uchun izohni yuklaydi.
    ///
    /// ★ <c>StudentId</c> ham shartda: <c>/users/5/notes/77</c> so'rovida 77
    /// boshqa o'quvchining izohi bo'lsa <c>404</c> qaytadi. Aks holda yo'l
    /// ichidagi <c>studentId</c> bezak bo'lib qolardi va ustoz o'z guruhidagi
    /// o'quvchi orqali BEGONA o'quvchining izohiga tega olardi.
    /// </summary>
    private async Task<StudentNote> LoadForWriteAsync(
        long studentId, long noteId, long actorId, StudentAudience audience, CancellationToken ct)
    {
        var note = await db.StudentNotes.AsTracking()
            .FirstOrDefaultAsync(n => n.Id == noteId && n.StudentId == studentId, ct)
            ?? throw new NotFoundException(nameof(StudentNote), noteId);

        // O'quv bo'limi va admin — hamma izohni boshqaradi (xodim ishdan
        // ketganda uning izohlarini tozalash kerak bo'ladi).
        if (audience == StudentAudience.Manage) return note;

        if (note.AuthorId != actorId)
        {
            throw new ForbiddenException(
                "Bu izohni siz yozmagansiz. Ustoz va kurator faqat O'Z izohini "
                + "tahrirlashi yoki o'chirishi mumkin.");
        }

        return note;
    }

    /// <summary>
    /// Guruh konteksti haqiqiy ekanini tekshiradi: o'quvchi SHU guruhda
    /// a'zo (yoki bo'lgan) bo'lishi kerak.
    ///
    /// Nima uchun kerak: aks holda xodim istalgan guruh Id'sini yozib
    /// qo'yardi va izoh ro'yxatida o'quvchi hech qachon o'qimagan guruh
    /// nomi ko'rinardi — bu ma'lumotni jimgina buzish yo'li.
    /// </summary>
    private async Task EnsureGroupContextAsync(long studentId, long? groupId, CancellationToken ct)
    {
        if (groupId is not { } id) return;

        var member = await db.GroupMembers.AsNoTracking()
            .AnyAsync(m => m.GroupId == id && m.StudentId == studentId, ct);

        if (!member)
        {
            throw new ValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["groupId"] = ["O'quvchi bu guruhda a'zo emas."],
            });
        }
    }

    private async Task<StudentNoteDto> GetDtoAsync(
        long noteId, long studentId, long actorId, StudentAudience audience, CancellationToken ct) =>
        await StudentNoteQueries
            .Project(
                db, studentId, actorId,
                canEditAll: audience == StudentAudience.Manage,
                noteId: noteId)
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(StudentNote), noteId);
}

/// <summary>
/// Izoh proyeksiyasi — YAGONA nusxa.
///
/// Profil agregati (<c>UserProfileService</c>) ham AYNAN shu proyeksiyani
/// ishlatadi: ikki joyda ikki nusxa bo'lsa, <c>canEdit</c> qoidasi bir joyda
/// o'zgarib, ikkinchisida eski qolib ketardi — ya'ni drawer'da tugma
/// ko'rinib, <c>PUT</c> 403 qaytaradigan holat.
/// </summary>
internal static class StudentNoteQueries
{
    /// <param name="canEditAll">
    /// So'rovchi (o'quv bo'limi/admin) hamma izohni tahrirlay oladimi.
    /// Aks holda faqat O'Z izohi.
    /// </param>
    /// <param name="noteId">
    /// Bitta izohni olish uchun. ★ Filtr AYNAN SHU YERDA, proyeksiyadan
    /// KEYIN emas: <c>Select(...).Where(dto =&gt; dto.Id == id)</c> shakli EF
    /// tomonidan SQL'ga tarjima qilinmaydi va so'rov ish vaqtida 500 bilan
    /// yiqiladi (bu integratsiya testida aynan shunday ushlandi).
    /// </param>
    internal static IQueryable<StudentNoteDto> Project(
        IApplicationDbContext db, long studentId, long actorId, bool canEditAll, long? noteId = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        var rows = db.StudentNotes.AsNoTracking()
            .Where(n => n.StudentId == studentId);

        if (noteId is { } id)
            rows = rows.Where(n => n.Id == id);

        return rows
            // Yangisidan eskisiga. `Id` bo'yicha: `CreatedAt` bir xil
            // millisekundda ikki izoh yozilsa tartib beqaror bo'lardi.
            .OrderByDescending(n => n.Id)
            .Select(n => new StudentNoteDto(
                n.Id,
                n.StudentId,
                n.Body,
                n.AuthorId,
                n.Author!.FullName,
                n.GroupId,
                n.Group == null ? null : n.Group.Name,
                n.CreatedAt,
                n.UpdatedAt,
                canEditAll || n.AuthorId == actorId));
    }
}
