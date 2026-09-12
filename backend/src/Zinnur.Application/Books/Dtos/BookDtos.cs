namespace Zinnur.Application.Books.Dtos;

/// <summary>Kutubxona kitobi — ro'yxat va yuklash javobi.</summary>
public sealed record BookDto(
    long Id,
    string Title,
    long SizeBytes,
    int? PageCount,
    bool IsActive,
    string? CreatedByName,
    DateTimeOffset CreatedAt);

/// <summary>
/// Yuklash so'rovi. <paramref name="ClientContentType"/> FAQAT xato xabari
/// uchun — haqiqiy tur fayl MAZMUNIDAN aniqlanadi (<c>MediaSignatures</c>).
/// </summary>
public sealed record BookUpload(
    string? ClientFileName,
    string? ClientContentType,
    Stream Content,
    long Length,
    string? Title,
    int? PageCount);

/// <summary><c>PUT /books/{id}</c> tanasi.</summary>
public sealed record UpdateBookRequest(string Title, bool IsActive, int? PageCount);
