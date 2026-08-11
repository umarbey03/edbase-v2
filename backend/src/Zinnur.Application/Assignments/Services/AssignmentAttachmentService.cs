using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Assignments.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Courses;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Media;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Assignments.Services;

/// <summary>
/// ========================================================================
/// UY VAZIFASI SHARTINING BIRIKTIRMALARI — YUKLASH, OQIM, O'CHIRISH
/// ========================================================================
///
/// ★ RUXSAT QOIDASI TAKRORLANMAYDI: u <see cref="IAssignmentService"/> dagi
/// ikki darvoza orqali chaqiriladi
/// (<c>EnsureCanReadAssignmentAsync</c> / <c>EnsureCanWriteAssignmentAsync</c>).
/// Qoida juda nozik — kurs vazifasini HAR ustoz ko'radi, lekin faqat o'quv
/// bo'limi tahrirlaydi; o'quvchi esa faqat o'ziga tegishlisini ko'radi —
/// va uni ikkinchi joyda qayta yozish kafolatlangan xato bo'lardi.
///
/// ★ YUKLASH VA O'QISH OQIMI dars mediasi bilan AYNI
/// (<see cref="IMediaStorage"/>, <see cref="MediaSignatures"/>,
/// <see cref="RangeHeader"/>): sehrli baytlar, hajm chegarasi va `Range`
/// mantiqi bir joyda yozilgan va shu yerda faqat QAYTA ISHLATILADI.
///
/// ★ O'CHIRISH TARTIBI: AVVAL BAZA, KEYIN OMBOR — sabab
/// <see cref="LessonAssetService"/> izohida (yetim obyekt — xarajat
/// muammosi, dangling qator — korrektlik muammosi).
/// </summary>
public sealed class AssignmentAttachmentService(
    IApplicationDbContext db,
    IMediaStorage storage,
    IAssignmentService assignments,
    ISettingsResolver settings,
    ILogger<AssignmentAttachmentService> logger) : IAssignmentAttachmentService
{
    /// <summary>Ombordagi papka nomi — kalit prefiksidan KEYIN turadi.</summary>
    private const string StorageFolder = "assignment-attachments";

    /// <summary>
    /// SHART biriktirmalarida qabul qilinadigan turkumlar.
    ///
    /// `Video` ATAYLAB YO'Q: shart uchun video kerak bo'lsa u DARS mediasi
    /// (`LessonAsset`) — u yerda `Range` bilan oqim, tartiblash va alohida
    /// hajm chegarasi bor. Bu yerda video ruxsat etilsa, 1 GB fayl uchun
    /// mo'ljallanmagan yo'ldan o'tib ketardi.
    /// </summary>
    private const MediaCategories AllowedCategories =
        MediaCategories.Image | MediaCategories.Audio | MediaCategories.Document;

    // ================================================================= yuklash

    public async Task<AssignmentAttachmentDto> UploadAsync(
        long assignmentId, LessonAssetUpload upload, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        // RUXSAT: shartni TAHRIRLASH huquqi (qoida `IAssignmentService` da).
        await assignments.EnsureCanWriteAssignmentAsync(assignmentId, actorId, ct)
            .ConfigureAwait(false);

        var existing = await db.AssignmentAttachments.AsNoTracking()
            .CountAsync(a => a.AssignmentId == assignmentId, ct)
            .ConfigureAwait(false);

        if (existing >= Assignment.MaxAttachments)
        {
            throw new ConflictException(
                "Vazifa shartiga ko'pi bilan "
                + Assignment.MaxAttachments.ToString(CultureInfo.InvariantCulture)
                + " ta fayl biriktiriladi.");
        }

        if (upload.Length <= 0)
            throw Invalid("Fayl bo'sh.");

        // ---- TUR MAZMUNDAN (kengaytmaga ISHONILMAYDI) ----
        var signature = await DetectAsync(upload, ct).ConfigureAwait(false);

        var kind = ToAttachmentKind(signature.Category);

        // ---- HAJM CHEGARASI: SOZLAMADAN ----
        //
        // ★ CHEGARA TURGA BOG'LIQ, shuning uchun u TUR ANIQLANGANDAN KEYIN
        //   tekshiriladi (`SubmissionAttachmentReader` dagi AYNI tartib).
        var limitBytes = await LimitBytesAsync(kind, ct).ConfigureAwait(false);

        if (upload.Length > limitBytes)
            throw TooLarge(kind, limitBytes);

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

        var attachment = new AssignmentAttachment
        {
            AssignmentId = assignmentId,
            Kind = kind,
            ObjectKey = objectKey,
            ContentType = signature.ContentType,
            SizeBytes = upload.Length,
            DurationSec = upload.DurationSec,
            CreatedById = actorId,
            Position = await PositionOrdering
                .NextPositionAsync(
                    db.AssignmentAttachments.AsNoTracking()
                        .Where(a => a.AssignmentId == assignmentId)
                        .Select(a => a.Position),
                    ct)
                .ConfigureAwait(false),
        };

        attachment.Validate();

        db.AssignmentAttachments.Add(attachment);

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Baza qabul qilmadi — omborda YETIM obyekt qolmasin.
            await TryDeleteFromStorageAsync(objectKey, ct).ConfigureAwait(false);

            throw new ConflictException(
                "Yozuv boshqa so'rov bilan to'qnashdi. Qaytadan urinib ko'ring.");
        }

        return Map(attachment);
    }

    // ================================================================= o'qish (oqim)

    public async Task<LessonAssetDownload> OpenAsync(
        long attachmentId, string? rangeHeader, long actorId, CancellationToken ct = default)
    {
        var row = await db.AssignmentAttachments.AsNoTracking()
            .Where(a => a.Id == attachmentId)
            .Select(a => new AttachmentRow(
                a.Id, a.AssignmentId, a.Kind, a.ObjectKey, a.ContentType, a.SizeBytes))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(AssignmentAttachment), attachmentId);

        // 🔴 RUXSAT — OMBORGA MUROJAATDAN OLDIN: aks holda javob vaqti yoki
        //    503 kabi belgilardan faylning bor-yo'qligi payqalardi.
        await assignments.EnsureCanReadAssignmentAsync(row.AssignmentId, actorId, ct)
            .ConfigureAwait(false);

        if (!storage.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori (R2/S3) sozlanmagan — faylni ochib bo'lmadi. "
                + "Administrator uchun: `Storage:ServiceUrl`, `Storage:Bucket`, "
                + "`Storage:AccessKey`, `Storage:SecretKey` to'ldirilishi kerak.");
        }

        var outcome = RangeHeader.TryParse(rangeHeader, row.SizeBytes, out var range);

        if (outcome == RangeParseOutcome.Unsatisfiable)
            throw new RangeNotSatisfiableException(row.SizeBytes);

        var requested = outcome == RangeParseOutcome.Satisfiable ? range : null;

        var stored = await storage.OpenReadAsync(row.ObjectKey, requested, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(AssignmentAttachment), attachmentId);

        // TUR BAZADAN ustun (yuklashda mazmundan aniqlangan).
        var contentType = Normalize(row.ContentType) ?? stored.ContentType;

        return new LessonAssetDownload(
            stored,
            contentType,
            SuggestFileName(row),
            stored.TotalLength ?? row.SizeBytes,
            stored.IsPartial ? requested : null);
    }

    // ================================================================= o'chirish

    public async Task DeleteAsync(long attachmentId, long actorId, CancellationToken ct = default)
    {
        var attachment = await db.AssignmentAttachments.AsTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(AssignmentAttachment), attachmentId);

        await assignments.EnsureCanWriteAssignmentAsync(attachment.AssignmentId, actorId, ct)
            .ConfigureAwait(false);

        var assignmentId = attachment.AssignmentId;
        var objectKey = attachment.ObjectKey;

        db.AssignmentAttachments.Remove(attachment);

        // Qolganlar tartibi ZICH qolsin.
        PositionOrdering.Reindex(
            await db.AssignmentAttachments.AsTracking()
                .Where(a => a.AssignmentId == assignmentId && a.Id != attachmentId)
                .OrderBy(a => a.Position).ThenBy(a => a.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false),
            a => a.Id,
            (a, position) => a.Position = position);

        // ★ AVVAL BAZA, KEYIN OMBOR.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await TryDeleteFromStorageAsync(objectKey, ct).ConfigureAwait(false);
    }

    // ================================================================= ichki

    /// <summary>
    /// Chegara — SOZLAMALAR registridan.
    ///
    /// ⚠️ Rasm/hujjat uchun `lesson.image_max_mb`, audio uchun ham AYNI
    /// kalit ishlatiladi. NIMA UCHUN alohida `assignment.*` kalitlari
    /// qo'shilmadi: sozlamalar registri qanchalik kichik bo'lsa, uni
    /// to'g'ri sozlash ehtimoli shunchalik yuqori. Ikkita ma'nosi deyarli
    /// bir xil kalit ("rasm hajmi" va "shart rasmi hajmi") administratorni
    /// chalg'itardi va biri albatta e'tibordan chetda qolardi.
    /// Ehtiyoj chiqsa alohida kalit qo'shish MUMKIN — bu yerda faqat bitta
    /// joy o'zgaradi.
    /// </summary>
    private async Task<long> LimitBytesAsync(AttachmentKind kind, CancellationToken ct)
    {
        _ = kind;

        var resolved = await settings.ResolveAsync(ImageLimitSetting, ct).ConfigureAwait(false);

        var megabytes = SettingValueParser
            .TryReadDecimal(ImageLimitSetting, resolved.Value, out var value)
            ? value
            : decimal.Parse(ImageLimitSetting.DefaultValue, CultureInfo.InvariantCulture);

        return (long)megabytes * 1024 * 1024;
    }

    /// <summary>
    /// Fayl turini SEHRLI BAYTLARDAN aniqlaydi.
    ///
    /// 🔴 Kengaytma va `Content-Type` sarlavhasi HISOBGA OLINMAYDI.
    /// </summary>
    private static async Task<MediaSignature> DetectAsync(
        LessonAssetUpload upload, CancellationToken ct)
    {
        var header = new byte[MediaSignatures.HeaderSize];

        upload.Content.Position = 0;

        var length = await upload.Content
            .ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct)
            .ConfigureAwait(false);

        if (length == 0)
            throw Invalid("Fayl bo'sh.");

        if (!MediaSignatures.TryDetect(header.AsSpan(0, length), AllowedCategories, out var signature))
        {
            throw Invalid(
                "Faylning turi qo'llab-quvvatlanmaydi. Rasm (jpg, png, webp, gif, heic), "
                + "ovoz (mp3, m4a, ogg, webm, wav) yoki PDF yuboring. "
                + $"Klient aytgan tur: {Describe(upload.ClientContentType)}. "
                + "⚠️ Fayl NOMI hisobga olinmaydi — tur fayl MAZMUNIDAN aniqlanadi.");
        }

        return signature;
    }

    /// <summary>
    /// Turkum -> saqlanadigan tur. `AllowedCategories` faqat uchtasini
    /// o'tkazadi, ya'ni to'rtinchi holat BO'LMAYDI.
    /// </summary>
    private static AttachmentKind ToAttachmentKind(MediaCategories category) => category switch
    {
        MediaCategories.Audio => AttachmentKind.Audio,
        MediaCategories.Document => AttachmentKind.Document,
        _ => AttachmentKind.Image,
    };

    private async Task TryDeleteFromStorageAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(objectKey, ct).ConfigureAwait(false);
        }
        catch (ServiceUnavailableException ex)
        {
            AttachmentLog.OrphanedObject(logger, ex, objectKey);
        }
    }

    /// <summary>
    /// 🔴 OBYEKT KALITI NOM SIFATIDA BERILMAYDI (ichki tuzilma oshkor
    /// bo'lmasin) — faqat kengaytma olinadi.
    /// </summary>
    private static string SuggestFileName(AttachmentRow row)
    {
        var extension = Path.GetExtension(row.ObjectKey.AsSpan());

        var prefix = row.Kind switch
        {
            AttachmentKind.Audio => "ovoz",
            AttachmentKind.Document => "hujjat",
            _ => "rasm",
        };

        return string.Create(CultureInfo.InvariantCulture, $"shart-{prefix}-{row.Id}{extension}");
    }

    private static AssignmentAttachmentDto Map(AssignmentAttachment attachment) =>
        new(attachment.Id,
            attachment.AssignmentId,
            attachment.Kind,
            attachment.Position,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.DurationSec,
            attachment.CreatedAt);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Describe(string? clientContentType) =>
        string.IsNullOrWhiteSpace(clientContentType) ? "ko'rsatilmagan" : clientContentType;

    private static ValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["file"] = [message] });

    private static PayloadTooLargeException TooLarge(AttachmentKind kind, long limitBytes)
    {
        var megabytes = (limitBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture);

        var what = kind switch
        {
            AttachmentKind.Audio => "Ovoz",
            AttachmentKind.Document => "Hujjat",
            _ => "Rasm",
        };

        return new PayloadTooLargeException(
            $"{what} hajmi {megabytes} MB dan oshmasligi kerak. Chegarani administrator "
            + $"sozlamalardan (`{SettingsRegistry.Keys.LessonImageMaxMb}`) o'zgartira oladi.");
    }

    private static readonly SettingDefinition ImageLimitSetting =
        SettingsRegistry.TryGet(SettingsRegistry.Keys.LessonImageMaxMb, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"Registrda '{SettingsRegistry.Keys.LessonImageMaxMb}' sozlamasi yo'q.");

    private sealed record AttachmentRow(
        long Id,
        long AssignmentId,
        AttachmentKind Kind,
        string ObjectKey,
        string ContentType,
        long SizeBytes);
}

/// <summary>Manba-generatsiyali log metodlari (CA1848). EventId makoni: 5210–5219.</summary>
internal static partial class AttachmentLog
{
    [LoggerMessage(
        EventId = 5210,
        Level = LogLevel.Warning,
        Message = "Shart biriktirmasining ombordagi obyekti o'chirilmadi — YETIM qoldi "
                  + "(baza yozuvi allaqachon o'chirilgan). key={Key}")]
    internal static partial void OrphanedObject(ILogger logger, Exception exception, string key);
}
