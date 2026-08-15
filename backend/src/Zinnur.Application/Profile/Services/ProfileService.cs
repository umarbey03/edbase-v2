using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Auth.Services;
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
    IPhoneChangeStore changes,
    IPhoneLoginCodeStore codes,
    ISettingsResolver settings,
    IRuntimeSettings runtimeSettings,
    TimeProvider clock,
    ILogger<ProfileService> logger) : IProfileService
{
    /// <summary>Ombordagi mantiqiy papka — kalit prefiksi shundan yasaladi.</summary>
    private const string StorageFolder = "avatars";

    /// <summary>`UserService.MaxFullNameLength` bilan AYNI (ustun 200 belgi).</summary>
    private const int MaxFullNameLength = 200;

    /// <summary>
    /// Avatar uchun FAQAT rasm.
    ///
    /// ⚠️ PDF va OVOZ ATAYLAB YO'Q (vazifa biriktirmalaridan farqli):
    /// profil rasmi `&lt;img&gt;` ichida chiziladi va boshqa turni qabul
    /// qilish ekranda buzilgan element bilan tugardi.
    /// </summary>
    private const MediaCategories AllowedCategories = MediaCategories.Image;

    /* ------------------------------------------------------------------ ism */

    /// <inheritdoc />
    public async Task<UserDto> UpdateNameAsync(
        long userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fullName = RequireFullName(request.FullName);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(User), userId);

        user.FullName = fullName;
        user.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Map(user);
    }

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

    /* -------------------------------------------------------------- telefon */

    /// <inheritdoc />
    public async Task<PhoneChangeStatusDto> RequestPhoneChangeAsync(
        long userId, ChangePhoneRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ★ NORMALIZATSIYA MAVJUD QOIDA BILAN — `User.NormalizePhone`.
        //   Ikkinchi nusxa yozilsa, bot topgan raqam bilan bu yerda
        //   saqlangan raqam bir kun mos kelmay qolardi.
        var normalized = User.NormalizePhone(request.Phone)
            ?? throw Invalid("Telefon raqami noto'g'ri.");

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(User), userId);

        if (user.PhoneNormalized == normalized)
            throw new ConflictException("Bu raqam allaqachon sizning profilingizda.");

        // 🔴 BAND RAQAM — DARHOL RAD ETILADI. Aks holda foydalanuvchi
        //    butun oqimni (bot, kod) bosib o'tib, faqat OXIRIDA unikal
        //    indeks xatosiga urilardi.
        var taken = await db.Users.AsNoTracking()
            .AnyAsync(u => u.PhoneNormalized == normalized && u.Id != userId, ct)
            .ConfigureAwait(false);

        if (taken)
        {
            // ⚠️ BU YERDA "HISOB SANASH" XAVFI YO'Q, `PhoneLoginService`
            //    dan farqli o'laroq: chaqiruvchi allaqachon TIZIMDA va
            //    uning kimligi ma'lum. Aniq xabar bermasak, foydalanuvchi
            //    nima uchun kod kelmayotganini hech qachon bilmasdi.
            throw new ConflictException(
                "Bu raqam boshqa profilga biriktirilgan. O'quv bo'limi bilan bog'laning.");
        }

        var pending = new PendingPhoneChange(userId, normalized);

        // Eski niyat (boshqa raqam uchun) BEKOR QILINADI: bir vaqtda
        // ikkita kutayotgan almashtirish bo'lsa, bot qaysi biriga kod
        // yuborishini bilmasdi.
        var previous = await changes.FindByUserAsync(userId, ct).ConfigureAwait(false);

        if (previous is not null && previous.PhoneNormalized != normalized)
            await changes.RemoveAsync(previous, ct).ConfigureAwait(false);

        await changes.SaveAsync(pending, ct).ConfigureAwait(false);

        ProfileLog.PhoneChangeRequested(logger, userId);

        return Status(pending);
    }

    /// <inheritdoc />
    public async Task<PhoneChangeStatusDto?> GetPhoneChangeAsync(
        long userId, CancellationToken ct = default)
    {
        var pending = await changes.FindByUserAsync(userId, ct).ConfigureAwait(false);

        return pending is null ? null : Status(pending);
    }

    /// <inheritdoc />
    public async Task CancelPhoneChangeAsync(long userId, CancellationToken ct = default)
    {
        var pending = await changes.FindByUserAsync(userId, ct).ConfigureAwait(false);

        if (pending is null) return;

        await changes.RemoveAsync(pending, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UserDto> ConfirmPhoneChangeAsync(
        long userId, ConfirmPhoneRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pending = await changes.FindByUserAsync(userId, ct).ConfigureAwait(false)
            ?? throw new ConflictException(
                "Telefon almashtirish so'rovi topilmadi yoki muddati o'tgan. Qaytadan boshlang.");

        if (pending.TelegramId is not { } telegramId)
        {
            throw new ConflictException(
                "Avval yangi raqamdan botga «Raqamni ulashish» tugmasini bosing — "
                + "kod o'sha Telegram hisobiga yuboriladi.");
        }

        // ★ KOD `IPhoneLoginCodeStore` DA — YANGI raqam bo'yicha
        //   kalitlangan (uni bot ham shu yo'l bilan saqlagan). Ikkinchi
        //   kod mexanizmi yozilmadi: urinishlar chegarasi, TTL va
        //   hash'lash allaqachon o'sha yerda va sinovdan o'tgan.
        var check = await codes
            .ConsumeAsync(pending.PhoneNormalized, request.Code ?? string.Empty, ct)
            .ConfigureAwait(false);

        if (check == PhoneCodeCheck.TooManyAttempts)
        {
            throw new TooManyRequestsException(
                "Juda ko'p noto'g'ri urinish. Yangi kod so'rang.",
                (int)PhoneLoginCodeStore.CodeTtl.TotalSeconds);
        }

        if (check != PhoneCodeCheck.Ok)
            throw new UnauthorizedException("Kod noto'g'ri yoki muddati o'tgan.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(User), userId);

        // ══════════════════════════════════════════════════════════════
        // QAYTA TEKSHIRUV — KOD BERILGANDAN KEYINGI 5 DAQIQADA HOLAT
        // O'ZGARGAN BO'LISHI MUMKIN.
        //
        // `PhoneLoginService.VerifyAsync` dagi AYNI qoida: kod to'g'ri
        // bo'lgani "amal hali ham mumkin" degani emas. Shu oraliqda
        // o'quv bo'limi o'sha raqamni boshqa profilga bergan bo'lishi
        // mumkin.
        // ══════════════════════════════════════════════════════════════
        var taken = await db.Users.AsNoTracking()
            .AnyAsync(u => u.PhoneNormalized == pending.PhoneNormalized && u.Id != userId, ct)
            .ConfigureAwait(false);

        if (taken)
        {
            await changes.RemoveAsync(pending, ct).ConfigureAwait(false);

            throw new ConflictException(
                "Bu raqam endi boshqa profilga biriktirilgan. O'quv bo'limi bilan bog'laning.");
        }

        // 🔴 TELEGRAM HISOBI HAM BAND BO'LISHI MUMKIN: foydalanuvchi
        //    kodni kutayotgan paytda o'sha hisob boshqa profilga
        //    bog'langan bo'lsa, unikal indeks (`IX_Users_TelegramId`)
        //    `SaveChanges` da yiqilardi — va foydalanuvchi "nimadir xato
        //    ketdi" degan tushunarsiz xabar olardi.
        var telegramTaken = await db.Users.AsNoTracking()
            .AnyAsync(u => u.TelegramId == telegramId && u.Id != userId, ct)
            .ConfigureAwait(false);

        if (telegramTaken)
        {
            await changes.RemoveAsync(pending, ct).ConfigureAwait(false);

            throw new ConflictException(
                "Bu Telegram hisobi boshqa profilga bog'langan.");
        }

        var now = clock.GetUtcNow();

        user.SetPhone(pending.PhoneNormalized);

        // ★ PROFIL YANGI TELEGRAM HISOBIGA KO'CHADI — sabab
        //   `PendingPhoneChange.TelegramId` izohida: kirish kodi HAR
        //   DOIM `User.TelegramId` ga ketadi, ya'ni bog'lanish eski
        //   hisobda qolsa foydalanuvchi yangi raqami bilan kira olmasdi.
        user.LinkTelegram(telegramId, pending.TelegramUsername, now);

        user.UpdatedAt = now;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await changes.RemoveAsync(pending, ct).ConfigureAwait(false);

        ProfileLog.PhoneChanged(logger, userId);

        return Map(user);
    }

    // ================================================================ ichki

    private PhoneChangeStatusDto Status(PendingPhoneChange pending) =>
        new(pending.PhoneNormalized,
            pending.CodeSent,
            BotUsername(),
            (int)IPhoneChangeStore.Ttl.TotalSeconds);

    /// <summary>
    /// Bot <c>@username</c> i — sozlamalardan (`telegram.bot_username`).
    /// Bo'sh bo'lsa <c>null</c>: ekran havolasiz ko'rsatma beradi.
    /// </summary>
    private string? BotUsername()
    {
        var value = runtimeSettings.Current.Value(SettingsRegistry.Keys.TelegramBotUsername)?.Trim();

        return string.IsNullOrEmpty(value) ? null : value.TrimStart('@');
    }

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

    private static string RequireFullName(string? fullName)
    {
        var value = fullName?.Trim();

        if (string.IsNullOrEmpty(value))
            throw Invalid("F.I.Sh. kiritilishi shart.");

        return value.Length > MaxFullNameLength
            ? throw Invalid("F.I.Sh. juda uzun.")
            : value;
    }

    private static ValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]> { ["profile"] = [message] });

    // `Phone` — XOM ustun (`PhoneNormalized` emas): `AuthService.Map`
    // bilan AYNI kelishuv.
    private static UserDto Map(User u) =>
        new(u.Id, u.FullName, u.Email, u.Phone, u.Role.ToString(),
            // Rasm YO'Q bo'lsa tamg'a ham `null` — sabab `AuthService.AvatarStamp` da.
            u.AvatarKey is null ? null : u.AvatarUpdatedAt);
}

/// <summary>
/// Manba-generatsiyali loglar (CA1848).
///
/// 🔴 TELEFON RAQAMI VA KOD LOGGA YOZILMAYDI — `PhoneLoginLog` dagi AYNI
/// qoida. Profil <c>Id</c> si qo'llab-quvvatlash uchun yetarli.
/// </summary>
internal static partial class ProfileLog
{
    [LoggerMessage(EventId = 6101, Level = LogLevel.Information,
        Message = "Profil {UserId}: telefon almashtirish so'raldi.")]
    public static partial void PhoneChangeRequested(ILogger logger, long userId);

    [LoggerMessage(EventId = 6102, Level = LogLevel.Information,
        Message = "Profil {UserId}: telefon almashtirildi.")]
    public static partial void PhoneChanged(ILogger logger, long userId);

    [LoggerMessage(EventId = 6103, Level = LogLevel.Warning,
        Message = "Eski avatar ombordan o'chirilmadi: {ObjectKey}. Yetim obyekt qoldi.")]
    public static partial void OrphanedAvatar(ILogger logger, Exception ex, string objectKey);
}
