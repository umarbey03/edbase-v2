using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Media;
using Zinnur.Application.Profile.Dtos;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.Profile.Services;

/// <summary>
/// <see cref="IProfileService"/> amalga oshirilishi. HTTP haqida HECH
/// NARSA bilmaydi.
/// </summary>
public sealed class ProfileService(
    IApplicationDbContext db,
    IMediaStorage storage,
    ISettingsResolver settings,
    TimeProvider clock,
    ILogger<ProfileService> logger) : IProfileService
{
    /// <summary>Ombordagi mantiqiy papka — kalit prefiksi shundan yasaladi.</summary>
    private const string StorageFolder = "avatars";

    /// <summary>
    /// Avatar uchun FAQAT rasm.
    ///
    /// ⚠️ PDF va OVOZ ATAYLAB YO'Q (vazifa biriktirmalaridan farqli):
    /// profil rasmi `&lt;img&gt;` ichida chiziladi va boshqa turni qabul
    /// qilish ekranda buzilgan element bilan tugardi.
    /// </summary>
    private const MediaCategories AllowedCategories = MediaCategories.Image;

    /* ----------------------------------------------------------------- rasm */

    /// <inheritdoc />
    public async Task<AvatarUploadedDto> UploadAvatarAsync(
        long userId, LessonAssetUpload upload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        if (upload.Length <= 0)
            throw Invalid("Fayl bo'sh.");

        // ---- TUR MAZMUNDAN (kengaytmaga ISHONILMAYDI) ----
        var signature = await DetectAsync(upload, ct).ConfigureAwait(false);

        // ---- HAJM CHEGARASI: SOZLAMADAN ----
        var limitBytes = await LimitBytesAsync(ct).ConfigureAwait(false);

        if (upload.Length > limitBytes)
        {
            throw new PayloadTooLargeException(
                $"Rasm hajmi {limitBytes / (1024 * 1024)} MB dan oshmasligi kerak.");
        }

        if (!storage.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori (R2/S3) sozlanmagan — rasm qabul qilinmaydi. "
                + "Administrator uchun: `Storage:ServiceUrl`, `Storage:Bucket`, "
                + "`Storage:AccessKey`, `Storage:SecretKey` to'ldirilishi kerak.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(User), userId);

        upload.Content.Position = 0;

        var objectKey = await storage
            .SaveAsync(
                new MediaUpload(
                    StorageFolder, signature.Extension, signature.ContentType,
                    upload.Content, upload.Length),
                ct)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();
        var previousKey = user.SetAvatar(objectKey, now);

        user.UpdatedAt = now;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // ★ ESKI FAYL BAZA SAQLANGANDAN KEYIN o'chiriladi. Teskarisida
        //   (avval o'chirib, keyin saqlash) `SaveChanges` yiqilsa
        //   foydalanuvchi rasmsiz qolardi: bazada eski kalit turardi,
        //   ombor esa bo'sh bo'lardi.
        if (previousKey is not null)
            await TryDeleteFromStorageAsync(previousKey, ct).ConfigureAwait(false);

        return new AvatarUploadedDto(now);
    }

    /// <inheritdoc />
    public async Task RemoveAvatarAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(User), userId);

        var now = clock.GetUtcNow();
        var previousKey = user.SetAvatar(null, now);

        // Rasm allaqachon yo'q — IDEMPOTENT, xato bermaymiz.
        if (previousKey is null) return;

        user.UpdatedAt = now;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await TryDeleteFromStorageAsync(previousKey, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LessonAssetDownload?> OpenAvatarAsync(
        long targetUserId, CancellationToken ct = default)
    {
        var key = await db.Users.AsNoTracking()
            .Where(u => u.Id == targetUserId)
            .Select(u => u.AvatarKey)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(key)) return null;

        if (!storage.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori sozlanmagan — rasmni ko'rsatib bo'lmaydi.");
        }

        // `Range` UZATILMAYDI: avatar bir necha yuz kilobayt va u
        // `<img>` ichida to'liq yuklanadi — qisman so'rov bu yerda
        // hech qanday foyda bermaydi (video uchun esa u SHART).
        var media = await storage.OpenReadAsync(key, range: null, ct).ConfigureAwait(false);

        if (media is null) return null;

        // ★ `LessonAssetDownload` QAYTA ISHLATILDI, yangi tur yasalmadi:
        //   `MediaResponse.WriteAsync` (sarlavhalar, `Range`, oqim
        //   egaligi) AYNAN shu shakl bilan ishlaydi va u dars mediasi
        //   uchun allaqachon sinovdan o'tgan.
        return new LessonAssetDownload(
            media,
            media.ContentType,

            // Fayl nomi — `Content-Disposition` uchun. Ombordagi kalit
            // (tasodifiy nom) ISHLATILMAYDI: u foydalanuvchiga hech
            // nima anglatmaydi va ichki tuzilishni oshkor qilardi.
            $"avatar-{targetUserId.ToString(CultureInfo.InvariantCulture)}",
            media.TotalLength ?? media.ContentLength ?? 0,
            Range: null);
    }

    // ================================================================ ichki

    /// <summary>
    /// Rasm hajmi chegarasi — `lesson.image_max_mb` sozlamasidan.
    ///
    /// ★ MAVJUD SOZLAMA QAYTA ISHLATILDI, yangisi qo'shilmadi: operator
    /// uchun "rasm chegarasi" bitta tushuncha, ikkita sozlama esa faqat
    /// chalkashlik berardi ("qaysi birini o'zgartiray?").
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

    private static readonly SettingDefinition ImageLimitSetting =
        SettingsRegistry.TryGet(SettingsRegistry.Keys.LessonImageMaxMb, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"Registrda '{SettingsRegistry.Keys.LessonImageMaxMb}' sozlamasi yo'q.");

    /// <summary>
    /// Fayl turini MAZMUNIDAN aniqlaydi.
    ///
    /// 🔴 Kengaytma va <c>Content-Type</c> sarlavhasi HISOBGA OLINMAYDI —
    /// `SubmissionFeedbackFileService` bilan AYNI qoida.
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
                "Profil rasmi uchun faqat rasm yuboriladi (jpg, png, webp, gif, heic). "
                + "⚠️ Fayl NOMI hisobga olinmaydi — tur fayl MAZMUNIDAN aniqlanadi.");
        }

        return signature;
    }

    private async Task TryDeleteFromStorageAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(objectKey, ct).ConfigureAwait(false);
        }
        catch (ServiceUnavailableException ex)
        {
            // Ombor javob bermadi — bu FOYDALANUVCHI uchun xato emas:
            // uning yangi rasmi allaqachon saqlangan. Yetim obyekt logda
            // qoladi (`SubmissionFeedbackFileService` dagi AYNI yechim).
            ProfileLog.OrphanedAvatar(logger, ex, objectKey);
        }
    }

    private static ValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]> { ["profile"] = [message] });
}

/// <summary>
/// Manba-generatsiyali loglar (CA1848).
///
/// 🔴 TELEFON RAQAMI VA KOD LOGGA YOZILMAYDI — `PhoneLoginLog` dagi AYNI
/// qoida. Profil <c>Id</c> si qo'llab-quvvatlash uchun yetarli.
///
/// ⚠️ `PhoneChangeRequested` (6101) va `PhoneChanged` (6102) OLIB
/// TASHLANDI (2026-08-17) — ism/telefonni o'zi tahrirlash imkoniyati
/// bilan birga (sabab `IProfileService` izohida). EventId'lar QAYTA
/// ISHLATILMAYDI — sabab `TelegramUpdateHandler` dagi 6205 izohi bilan
/// AYNI.
/// </summary>
internal static partial class ProfileLog
{
    [LoggerMessage(EventId = 6103, Level = LogLevel.Warning,
        Message = "Eski avatar ombordan o'chirilmadi: {ObjectKey}. Yetim obyekt qoldi.")]
    public static partial void OrphanedAvatar(ILogger logger, Exception ex, string objectKey);
}
