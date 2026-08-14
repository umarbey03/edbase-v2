using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Assignments.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Media;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Assignments.Services;

/// <summary>
/// <see cref="ISubmissionFeedbackFileService"/> ning amalga oshirilishi.
///
/// ★ TUZILISHI <see cref="AssignmentAttachmentService"/> BILAN ATAYLAB
/// BIR XIL (yuklash -> tur aniqlash -> chegara -> ombor -> baza; o'chirish
/// esa AVVAL BAZA, KEYIN OMBOR). Ikkita bir xil ishni ikki xil tartibda
/// qilish kelajakdagi o'quvchini "bu yerda nega boshqacha?" degan savolga
/// ko'mib tashlardi — holbuki javob "sababsiz" bo'lardi.
///
/// ★ RUXSAT QOIDASI BU YERDA YO'Q: u <see cref="IAssignmentService"/> ning
/// ikki darvozasidan (<c>EnsureCanReadSubmissionAsync</c> /
/// <c>EnsureCanGradeSubmissionAsync</c>) chaqiriladi. Qoidani bu yerda
/// qayta yozish eski tizimning X-6 kamchiligini (begona o'quvchining ishi
/// ko'rinishi) qaytarishning eng ehtimolli yo'li edi.
/// </summary>
public sealed class SubmissionFeedbackFileService(
    IApplicationDbContext db,
    IMediaStorage storage,
    IAssignmentService assignments,
    ISettingsResolver settings,
    ILogger<SubmissionFeedbackFileService> logger) : ISubmissionFeedbackFileService
{
    /// <summary>Ombordagi mantiqiy papka — operator uni PREFIKS bo'yicha taniydi.</summary>
    private const string StorageFolder = "submission-feedback";

    /// <summary>
    /// TEKSHIRUV fayllarida qabul qilinadigan turkumlar.
    ///
    /// 🔴 <c>Document</c> BOR (o'quvchining topshirish yo'lidan farqi shu) —
    /// ustozning sharhi ko'pincha PDF. <c>Video</c> esa YO'Q: u dars
    /// mediasi yo'liga tegishli (izohi <c>AssignmentAttachmentService</c> da).
    ///
    /// ⚠️ O'QUVCHINING yo'li (<see cref="SubmissionAttachmentReader"/>)
    /// KENGAYTIRILMADI — sabab <see cref="ISubmissionFeedbackFileService"/>
    /// izohida.
    /// </summary>
    private const MediaCategories AllowedCategories =
        MediaCategories.Image | MediaCategories.Audio | MediaCategories.Document;

    // ================================================================= yuklash

    /// <inheritdoc />
    public async Task<SubmissionFeedbackFileDto> UploadAsync(
        long submissionId, LessonAssetUpload upload, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        // RUXSAT: baholash huquqi (qoida `IAssignmentService` da).
        await assignments.EnsureCanGradeSubmissionAsync(submissionId, actorId, ct)
            .ConfigureAwait(false);

        var existing = await db.SubmissionFeedbackFiles.AsNoTracking()
            .CountAsync(f => f.SubmissionId == submissionId, ct)
            .ConfigureAwait(false);

        if (existing >= SubmissionFeedbackFile.MaxPerSubmission)
        {
            throw new ConflictException(
                "Bitta tekshiruvga ko'pi bilan "
                + SubmissionFeedbackFile.MaxPerSubmission.ToString(CultureInfo.InvariantCulture)
                + " ta fayl biriktiriladi.");
        }

        if (upload.Length <= 0)
            throw Invalid("Fayl bo'sh.");

        // ---- TUR MAZMUNDAN (kengaytmaga ISHONILMAYDI) ----
        var signature = await DetectAsync(upload, ct).ConfigureAwait(false);

        var kind = ToAttachmentKind(signature.Category);

        // ---- HAJM CHEGARASI: SOZLAMADAN, TUR ANIQLANGANDAN KEYIN ----
        var limitBytes = await LimitBytesAsync(ct).ConfigureAwait(false);

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

        var file = new SubmissionFeedbackFile
        {
            SubmissionId = submissionId,
            Kind = kind,
            ObjectKey = objectKey,
            ContentType = signature.ContentType,
            FileName = GroupChatAttachment.SanitizeFileName(upload.ClientFileName),
            SizeBytes = upload.Length,
            CreatedById = actorId,
        };

        file.Validate();

        db.SubmissionFeedbackFiles.Add(file);

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

        return Map(file);
    }

    // ================================================================= o'qish (oqim)

    /// <inheritdoc />
    public async Task<LessonAssetDownload> OpenAsync(
        long fileId, string? rangeHeader, long actorId, CancellationToken ct = default)
    {
        var row = await db.SubmissionFeedbackFiles.AsNoTracking()
            .Where(f => f.Id == fileId)
            .Select(f => new FileRow(
                f.Id, f.SubmissionId, f.Kind, f.ObjectKey, f.ContentType, f.FileName, f.SizeBytes))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(SubmissionFeedbackFile), fileId);

        // 🔴 RUXSAT — OMBORGA MUROJAATDAN OLDIN: aks holda javob vaqti yoki
        //    503 kabi belgilardan faylning bor-yo'qligi payqalardi.
        //
        // ★ KO'RISH huquqi, BAHOLASH emas: o'quvchi O'ZIGA qo'yilgan
        //   tuzatishni ko'rishi kerak (R37 ning mohiyati).
        await assignments.EnsureCanReadSubmissionAsync(row.SubmissionId, actorId, ct)
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
            ?? throw new NotFoundException(nameof(SubmissionFeedbackFile), fileId);

        // TUR BAZADAN ustun (yuklashda mazmundan aniqlangan).
        var contentType = string.IsNullOrWhiteSpace(row.ContentType)
            ? stored.ContentType
            : row.ContentType;

        return new LessonAssetDownload(
            stored,
            contentType,
            SuggestFileName(row),
            stored.TotalLength ?? row.SizeBytes,
            stored.IsPartial ? requested : null);
    }

    // ================================================================= o'chirish

    /// <inheritdoc />
    public async Task DeleteAsync(long fileId, long actorId, CancellationToken ct = default)
    {
        var file = await db.SubmissionFeedbackFiles.AsTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(SubmissionFeedbackFile), fileId);

        await assignments.EnsureCanGradeSubmissionAsync(file.SubmissionId, actorId, ct)
            .ConfigureAwait(false);

        var objectKey = file.ObjectKey;

        db.SubmissionFeedbackFiles.Remove(file);

        // ★ AVVAL BAZA, KEYIN OMBOR — sabab `LessonAssetService.DeleteAsync`
        //   izohida (yetim obyekt — XARAJAT muammosi, dangling qator —
        //   KORREKTLIK muammosi; ikkinchisi og'irroq).
        //
        // ⚠️ `ChatRetentionJob` da tartib TESKARI va bu qarama-qarshilik
        //   emas: u yerda o'chirish AVTOMATIK va qator o'chgach kalitni
        //   qayta topib bo'lmasdi, bu yerda esa o'chirishni ODAM boshlaydi
        //   va u qayta urinishi mumkin.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await TryDeleteFromStorageAsync(objectKey, ct).ConfigureAwait(false);
    }

    // ================================================================= ichki

    /// <summary>
    /// Chegara — SOZLAMALAR registridan (<c>lesson.image_max_mb</c>).
    ///
    /// ⚠️ Alohida `submission.feedback_max_mb` kaliti ATAYLAB qo'shilmadi —
    /// sabab <see cref="AssignmentAttachmentService"/> dagi bilan bir xil
    /// (registr qanchalik kichik bo'lsa, to'g'ri sozlash ehtimoli
    /// shunchalik yuqori).
    /// </summary>
    private async Task<long> LimitBytesAsync(CancellationToken ct)
    {
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
    /// 🔴 Kengaytma va <c>Content-Type</c> sarlavhasi HISOBGA OLINMAYDI.
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
    /// Turkum -> saqlanadigan tur. <see cref="AllowedCategories"/> faqat
    /// uchtasini o'tkazadi, ya'ni to'rtinchi holat BO'LMAYDI.
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
            FeedbackFileLog.OrphanedObject(logger, ex, objectKey);
        }
    }

    /// <summary>
    /// Yuklab olinadigan fayl nomi.
    ///
    /// ★ USTOZ BERGAN NOM USTUN (tozalangan): "tuzatilgan-varaq.pdf" o'sha
    /// ko'rinishda yetib borishi kerak.
    ///
    /// 🔴 NOM YO'Q BO'LSA HAM OBYEKT KALITI BERILMAYDI — faqat kengaytma.
    /// </summary>
    private static string SuggestFileName(FileRow row)
    {
        if (row.FileName is { Length: > 0 } name) return name;

        var extension = Path.GetExtension(row.ObjectKey.AsSpan());

        var prefix = row.Kind switch
        {
            AttachmentKind.Audio => "ovoz",
            AttachmentKind.Document => "hujjat",
            _ => "rasm",
        };

        return string.Create(
            CultureInfo.InvariantCulture, $"tekshiruv-{prefix}-{row.Id}{extension}");
    }

    private static SubmissionFeedbackFileDto Map(SubmissionFeedbackFile file) =>
        new(file.Id,
            file.SubmissionId,
            file.Kind,
            file.ContentType,
            file.FileName,
            file.SizeBytes,
            file.CreatedById,
            file.CreatedAt);

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

    private sealed record FileRow(
        long Id,
        long SubmissionId,
        AttachmentKind Kind,
        string ObjectKey,
        string ContentType,
        string? FileName,
        long SizeBytes);
}

/// <summary>Manba-generatsiyali log metodlari (CA1848). EventId makoni: 5240–5249.</summary>
internal static partial class FeedbackFileLog
{
    [LoggerMessage(
        EventId = 5240,
        Level = LogLevel.Warning,
        Message = "Tekshiruv faylining ombordagi obyekti o'chirilmadi — YETIM qoldi "
                  + "(baza yozuvi allaqachon o'chirilgan). key={Key}")]
    internal static partial void OrphanedObject(ILogger logger, Exception exception, string key);
}
