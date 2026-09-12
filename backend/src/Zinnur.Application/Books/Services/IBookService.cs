using Zinnur.Application.Books.Dtos;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Media;

namespace Zinnur.Application.Books.Services;

/// <summary>
/// KUTUBXONA (2026-09-09) — PDF kitoblar: yuklash, ro'yxat, oqim, chipta.
///
/// RUXSAT:
///   • ro'yxat va o'qish — har qanday FAOL foydalanuvchi (ustoz jonli
///     darsda tanlaydi; o'quvchi kelajakda o'zi ochishi mumkin);
///   • yuklash, o'zgartirish, o'chirish — Academic/Admin.
/// </summary>
public interface IBookService
{
    Task<IReadOnlyList<BookDto>> ListAsync(long actorId, bool includeInactive, CancellationToken ct = default);

    Task<BookDto> UploadAsync(BookUpload upload, long actorId, CancellationToken ct = default);

    Task<BookDto> UpdateAsync(long bookId, UpdateBookRequest request, long actorId, CancellationToken ct = default);

    /// <summary>Faylni OQIM bilan ochadi (<c>Range</c> qo'llab-quvvatlanadi — PDF o'quvchi bo'lak-bo'lak so'raydi).</summary>
    Task<LessonAssetDownload> OpenAsync(long bookId, string? rangeHeader, long actorId, CancellationToken ct = default);

    /// <summary>Brauzerdagi PDF o'quvchi uchun qisqa muddatli chipta (sarlavhasiz <c>GET</c>).</summary>
    Task<MediaAccessTicket> CreateTicketAsync(long bookId, long actorId, CancellationToken ct = default);

    /// <summary>Chiptani tekshiradi — controller uchun (<c>?ticket=</c>).</summary>
    long? ResolveTicket(string? token, long bookId);

    Task DeleteAsync(long bookId, long actorId, CancellationToken ct = default);
}
