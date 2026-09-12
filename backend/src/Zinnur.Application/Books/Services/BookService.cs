using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Books.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Media;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Books.Services;

/// <summary>
/// <see cref="IBookService"/> — <c>LessonAssetService</c> bilan AYNI naqsh:
/// tur MAZMUNDAN aniqlanadi, fayl omborga ketadi, yozuv bazada; bazaga
/// yozish yiqilsa fayl ombordan olib tashlanadi.
///
/// ★ CHIPTA ID MAKONI: <see cref="IMediaAccessTicketService"/> imzoga
/// <c>assetId</c> ni qo'shadi, lekin "qaysi jadval" degan tushuncha yo'q —
/// dars fayli #5 uchun berilgan chipta kitob #5 uchun ham o'tib ketardi.
/// Shuning uchun kitob chiptasi MANFIY id bilan imzolanadi
/// (<see cref="TicketKey"/>): dars fayllari id'lari doim musbat, ya'ni
/// ikki makon hech qachon kesishmaydi.
/// </summary>
public sealed class BookService(
    IApplicationDbContext db,
    IMediaStorage storage,
    IMediaAccessTicketService tickets) : IBookService
{
    private const string StorageFolder = "books";

    /// <summary>50 MB — darslik PDF odatda 2–5 MB; skanerlanganlari 30 MB gacha.</summary>
    public const long MaxBytes = 50L * 1024 * 1024;

    public async Task<IReadOnlyList<BookDto>> ListAsync(
        long actorId, bool includeInactive, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct).ConfigureAwait(false);

        // Arxivni faqat boshqaruvchi ko'radi — ustozga jonli darsda kerak emas.
        var showInactive = includeInactive && IsManager(actor);

        return await db.Books.AsNoTracking()
            .Where(b => showInactive || b.IsActive)
            .OrderByDescending(b => b.IsActive)
            .ThenBy(b => b.Title)
            .Select(b => new BookDto(
                b.Id,
                b.Title,
                b.SizeBytes,
                b.PageCount,
                b.IsActive,
                db.Users.Where(u => u.Id == b.CreatedById).Select(u => u.FullName).FirstOrDefault(),
                b.CreatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<BookDto> UploadAsync(BookUpload upload, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var actor = await LoadActorAsync(actorId, ct).ConfigureAwait(false);
        EnsureCanManage(actor);

        if (upload.Length <= 0)
            throw Invalid("file", "Fayl bo'sh.");

        if (upload.Length > MaxBytes)
        {
            throw new PayloadTooLargeException(
                $"Kitob {MaxBytes / (1024 * 1024)} MB dan katta bo'lmasin. "
                + "Skanerlangan PDF'ni siqib (compress) qayta yuklang.");
        }

        var signature = await DetectAsync(upload, ct).ConfigureAwait(false);

        if (!storage.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori (R2/S3) sozlanmagan — fayl qabul qilinmaydi. "
                + "Administrator uchun: `Storage:ServiceUrl`, `Storage:Bucket`, "
                + "`Storage:AccessKey`, `Storage:SecretKey` to'ldirilishi kerak.");
        }

        upload.Content.Position = 0;

        var objectKey = await storage
            .SaveAsync(
                new MediaUpload(
                    StorageFolder, signature.Extension, signature.ContentType,
                    upload.Content, upload.Length),
                ct)
            .ConfigureAwait(false);

        var book = new Book
        {
            Title = Normalize(upload.Title) ?? TitleFromFileName(upload.ClientFileName),
            ObjectKey = objectKey,
            ContentType = signature.ContentType,
            SizeBytes = upload.Length,
            PageCount = upload.PageCount,
            CreatedById = actor.Id,
        };

        book.Validate();

        db.Books.Add(book);

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            await TryDeleteFromStorageAsync(objectKey, ct).ConfigureAwait(false);

            throw new ConflictException(
                "Yozuv boshqa so'rov bilan to'qnashdi. Qaytadan urinib ko'ring.");
        }

        return new BookDto(
            book.Id, book.Title, book.SizeBytes, book.PageCount, book.IsActive,
            actor.FullName, book.CreatedAt);
    }

    public async Task<BookDto> UpdateAsync(
        long bookId, UpdateBookRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = await LoadActorAsync(actorId, ct).ConfigureAwait(false);
        EnsureCanManage(actor);

        var book = await db.Books.AsTracking()
            .FirstOrDefaultAsync(b => b.Id == bookId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Book), bookId);

        book.Title = Normalize(request.Title) ?? book.Title;
        book.IsActive = request.IsActive;
        // Sahifalar soni faqat KLIENT aniqlagach yoziladi; `null` — tegilmaydi.
        if (request.PageCount is { } pages) book.PageCount = pages;
        book.UpdatedAt = DateTimeOffset.UtcNow;

        book.Validate();

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var creator = await db.Users.AsNoTracking()
            .Where(u => u.Id == book.CreatedById)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return new BookDto(
            book.Id, book.Title, book.SizeBytes, book.PageCount, book.IsActive, creator, book.CreatedAt);
    }

    public async Task<LessonAssetDownload> OpenAsync(
        long bookId, string? rangeHeader, long actorId, CancellationToken ct = default)
    {
        await LoadActorAsync(actorId, ct).ConfigureAwait(false);

        var book = await db.Books.AsNoTracking()
            .Where(b => b.Id == bookId)
            .Select(b => new { b.Id, b.Title, b.ObjectKey, b.ContentType, b.SizeBytes })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Book), bookId);

        if (!storage.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori (R2/S3) sozlanmagan — faylni ochib bo'lmadi.");
        }

        var outcome = RangeHeader.TryParse(rangeHeader, book.SizeBytes, out var range);

        if (outcome == RangeParseOutcome.Unsatisfiable)
            throw new RangeNotSatisfiableException(book.SizeBytes);

        var requested = outcome == RangeParseOutcome.Satisfiable ? range : null;

        var stored = await storage.OpenReadAsync(book.ObjectKey, requested, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Book), bookId);

        return new LessonAssetDownload(
            stored,
            Normalize(book.ContentType) ?? stored.ContentType,
            SafeFileName(book.Title) + ".pdf",
            stored.TotalLength ?? book.SizeBytes,
            stored.IsPartial ? requested : null);
    }

    public async Task<MediaAccessTicket> CreateTicketAsync(
        long bookId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct).ConfigureAwait(false);

        var exists = await db.Books.AsNoTracking()
            .AnyAsync(b => b.Id == bookId, ct)
            .ConfigureAwait(false);

        if (!exists)
            throw new NotFoundException(nameof(Book), bookId);

        return tickets.Issue(TicketKey(bookId), actor.Id);
    }

    public long? ResolveTicket(string? token, long bookId) =>
        tickets.TryResolveUserId(token, TicketKey(bookId));

    public async Task DeleteAsync(long bookId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct).ConfigureAwait(false);
        EnsureCanManage(actor);

        var book = await db.Books.AsTracking()
            .FirstOrDefaultAsync(b => b.Id == bookId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Book), bookId);

        var objectKey = book.ObjectKey;

        db.Books.Remove(book);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Bazadan KEYIN ombordan: fayl o'chirish yiqilsa yetim fayl qoladi
        // (zararsiz), teskarisi bo'lsa "fayli yo'q yozuv" qolardi.
        await TryDeleteFromStorageAsync(objectKey, ct).ConfigureAwait(false);
    }

    // ================================================================= yordamchilar

    /// <summary>Kitob chiptasi makoni — MANFIY id (izoh sinf tepasida).</summary>
    private static long TicketKey(long bookId) => -bookId;

    private static async Task<MediaSignature> DetectAsync(BookUpload upload, CancellationToken ct)
    {
        var header = new byte[MediaSignatures.HeaderSize];

        upload.Content.Position = 0;

        var length = await upload.Content
            .ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct)
            .ConfigureAwait(false);

        if (length == 0)
            throw Invalid("file", "Fayl bo'sh.");

        if (!MediaSignatures.TryDetect(header.AsSpan(0, length), MediaCategories.Document, out var signature)
            || !string.Equals(signature.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(
                "file",
                "Faqat PDF qabul qilinadi. "
                + $"Klient aytgan tur: {(string.IsNullOrWhiteSpace(upload.ClientContentType) ? "ko'rsatilmagan" : upload.ClientContentType)}. "
                + "⚠️ Fayl NOMI hisobga olinmaydi — tur fayl MAZMUNIDAN aniqlanadi.");
        }

        return signature;
    }

    private async Task<User> LoadActorAsync(long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return actor;
    }

    private static bool IsManager(User actor) => actor.Role is UserRole.Admin or UserRole.Academic;

    private static void EnsureCanManage(User actor)
    {
        if (!IsManager(actor))
        {
            throw new ForbiddenException(
                "Kutubxonani faqat o'quv bo'limi xodimi yoki administrator "
                + "o'zgartira oladi. Ustoz va kurator kitoblarni faqat ochadi.");
        }
    }

    private async Task TryDeleteFromStorageAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(objectKey, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Yetim fayl — zararsiz; jurnal ombor klientida.
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Nom berilmasa fayl nomidan (kengaytmasiz), u ham bo'lmasa "Kitob".</summary>
    private static string TitleFromFileName(string? fileName)
    {
        var name = Normalize(Path.GetFileNameWithoutExtension(fileName ?? string.Empty));
        if (name is null) return "Kitob";
        return name.Length > Book.MaxTitleLength ? name[..Book.MaxTitleLength] : name;
    }

    /// <summary>Yuklab olish nomi — fayl tizimida yaroqsiz belgilar `_` bilan.</summary>
    private static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = title.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var safe = new string(chars).Trim();
        return safe.Length == 0 ? "kitob" : safe;
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
