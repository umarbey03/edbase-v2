using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Courses.Dtos;
using Zinnur.Application.Gating.Services;
using Zinnur.Application.Media;
using Zinnur.Application.Payments.Services;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Courses.Services;

/// <summary>
/// ========================================================================
/// DARS MEDIASI — YUKLASH, OQIM (Range), O'CHIRISH, TARTIB
/// ========================================================================
///
/// HTTP haqida HECH NARSA bilmaydi. Uch nozik joyi bor va uchalasi ham
/// "jimgina yo'qotish yoki jimgina oshkor qilish" xavfi bilan bog'liq.
///
/// ── 1) RUXSAT — SERVERDA, HAR SO'ROVDA ─────────────────────────────────
///
/// 🔴 UI'da yashirish YETARLI EMAS. Video havolasi `assetId` dan iborat va
/// ID'lar ketma-ket — ya'ni o'quvchi qulflangan darsning `assetId` sini
/// TAXMIN QILA OLADI. Shuning uchun <see cref="OpenAsync"/> har chaqiruvda:
///
///   • xodim (Teacher/Assistant/Academic/Admin) -> ruxsat, gatingsiz
///     (ular kontentni ko'rishi ish talabi);
///   • o'quvchi -> TO'LOV BLOKI (`PaymentBlockScope.Video`) va GATING
///     ikkalasidan ham o'tishi shart.
///
/// Tartib ATAYLAB shunday: avval to'lov bloki (`CourseService.GetAsync`
/// dagi AYNI ketma-ketlik). Sabab — qarzdor o'quvchi uchun eng aniq va
/// eng foydali xabar aynan to'lov haqidagisi; gating xabari ("oldingi dars
/// tugatilmagan") uni chalg'itardi.
///
/// ── 2) `Range` — VIDEO UCHUN HAYOTIY ───────────────────────────────────
///
/// Oraliq BAZADAGI hajm bo'yicha normallashtiriladi va OMBORGA uzatiladi
/// (izlash S3 tomonida bo'ladi). Batafsil: <see cref="RangeHeader"/> va
/// <see cref="IMediaStorage"/>.
///
/// ── 3) O'CHIRISH TARTIBI: AVVAL BAZA, KEYIN OMBOR ──────────────────────
///
/// ★ IKKI VARIANT VA TANLASH SABABI:
///
///   A) OMBOR -> BAZA. Oradagi uzilishda bazada yozuv QOLADI, obyekt esa
///      YO'Q. Natija — UI'da "buzuq" asset: ro'yxatda ko'rinadi, bosilsa
///      404. Foydalanuvchi buni BUG deb ko'radi va uni tuzatishning yo'li
///      yo'q (qayta o'chirish ham 404 beradi... aslida beradi, lekin
///      foydalanuvchi buni bilmaydi).
///
///   B) BAZA -> OMBOR (TANLANDI). Oradagi uzilishda omborda YETIM obyekt
///      qoladi. U hech qayerdan ko'rinmaydi, hech nimani buzmaydi va
///      faqat JOY egallaydi — ya'ni bu XARAJAT muammosi, KORREKTLIK
///      muammosi emas. Xarajatni keyinroq yig'ishtiruvchi (GC) vazifa
///      hal qiladi; buzuq ekranni esa hech narsa hal qilmaydi.
///
/// Shu sababli ombordan o'chirish XATO BERSA HAM amal muvaffaqiyatli
/// hisoblanadi (baza allaqachon o'zgargan, qaytarib bo'lmaydi) — xato
/// faqat LOGGA yoziladi.
/// </summary>
public sealed class LessonAssetService(
    IApplicationDbContext db,
    IMediaStorage storage,
    IGatingService gating,
    IPaymentBlockService paymentBlock,
    ISettingsResolver settings,
    ILogger<LessonAssetService> logger) : ILessonAssetService
{
    /// <summary>Ombordagi papka nomi — kalit prefiksidan KEYIN turadi.</summary>
    private const string StorageFolder = "lesson-assets";

    /// <summary>Bitta darsga biriktiriladigan media soni chegarasi.</summary>
    private const int MaxAssetsPerLesson = 50;

    // ================================================================= yuklash

    public async Task<LessonAssetDto> UploadAsync(
        long lessonId, LessonAssetUpload upload, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var actor = await LoadActorAsync(actorId, ct).ConfigureAwait(false);
        EnsureCanManage(actor);

        var lesson = await db.ModuleLessons.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == lessonId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(ModuleLesson), lessonId);

        // ★ TUR DARSDAN KELIB CHIQADI, klientdan EMAS: aks holda invariantni
        //   buzadigan yozuv yasash mumkin bo'lardi.
        var assetKind = lesson.AllowedAssetKind;

        var existing = await db.LessonAssets.AsNoTracking()
            .CountAsync(a => a.LessonId == lessonId, ct)
            .ConfigureAwait(false);

        if (existing >= MaxAssetsPerLesson)
        {
            throw new ConflictException(
                "Bitta darsga ko'pi bilan "
                + MaxAssetsPerLesson.ToString(CultureInfo.InvariantCulture)
                + " ta fayl biriktiriladi. Darsni bo'lib yuboring.");
        }

        // ---- HAJM CHEGARASI: SOZLAMADAN, o'qishdan OLDIN ----
        //
        // ★ `Length` OLDIN tekshiriladi: fayl allaqachon ma'lum hajmda
        //   (multipart bo'lagi to'liq qabul qilingan), ya'ni bitta baytni
        //   ham o'qimasdan rad etish mumkin.
        var limitBytes = await LimitBytesAsync(assetKind, ct).ConfigureAwait(false);

        if (upload.Length <= 0)
            throw Invalid("file", "Fayl bo'sh.");

        if (upload.Length > limitBytes)
            throw TooLarge(assetKind, limitBytes);

        // ---- TUR MAZMUNDAN: kengaytmaga va sarlavhaga ISHONILMAYDI ----
        var signature = await DetectAsync(upload, assetKind, ct).ConfigureAwait(false);

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

        var asset = new LessonAsset
        {
            LessonId = lessonId,
            Kind = assetKind,
            Title = Normalize(upload.Title),
            ObjectKey = objectKey,
            ContentType = signature.ContentType,
            SizeBytes = upload.Length,
            DurationSec = upload.DurationSec,
            Width = upload.Width,
            Height = upload.Height,
            CreatedById = actor.Id,
            Position = await PositionOrdering
                .NextPositionAsync(
                    db.LessonAssets.AsNoTracking()
                        .Where(a => a.LessonId == lessonId)
                        .Select(a => a.Position),
                    ct)
                .ConfigureAwait(false),
        };

        // Domain: kalit, tur, hajm va ko'rsatish maydonlari izchilmi.
        asset.Validate();

        // ⚠️ IKKINCHI TEKSHIRUV ATAYLAB: yuqorida tur DARSDAN olingan, bu
        //    yerda esa invariantning O'ZI (`Normal`->`Video`) tasdiqlanadi.
        //    Ikkisi bir xil natija berishi kerak; farq chiqsa — bu bizning
        //    bug'imiz va u JIMGINA o'tib ketmasligi kerak.
        lesson.EnsureAssetKindAllowed(asset.Kind);

        db.LessonAssets.Add(asset);

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

        return Map(asset);
    }

    // ================================================================= o'qish (oqim)

    public async Task<LessonAssetDownload> OpenAsync(
        long assetId, string? rangeHeader, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct).ConfigureAwait(false);

        var asset = await db.LessonAssets.AsNoTracking()
            .Where(a => a.Id == assetId)
            .Select(a => new AssetRow(
                a.Id, a.LessonId, a.Kind, a.ObjectKey, a.ContentType, a.SizeBytes))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(LessonAsset), assetId);

        // 🔴 RUXSAT — OMBORGA MUROJAATDAN OLDIN. Aks holda begona odam
        //    javob vaqti yoki 503 kabi belgilardan faylning bor-yo'qligini
        //    payqardi (`AssignmentService.OpenFileAsync` dagi AYNI qoida).
        await EnsureCanReadAsync(actor, asset.LessonId, ct).ConfigureAwait(false);

        if (!storage.IsConfigured)
        {
            throw new ServiceUnavailableException(
                "Fayl ombori (R2/S3) sozlanmagan — faylni ochib bo'lmadi. "
                + "Administrator uchun: `Storage:ServiceUrl`, `Storage:Bucket`, "
                + "`Storage:AccessKey`, `Storage:SecretKey` to'ldirilishi kerak.");
        }

        // ORALIQ BAZADAGI hajm bo'yicha normallashtiriladi — omborga faqat
        // ANIQ chegaralar bilan boriladi.
        var outcome = RangeHeader.TryParse(rangeHeader, asset.SizeBytes, out var range);

        if (outcome == RangeParseOutcome.Unsatisfiable)
        {
            // 416 uchun maxsus istisno YO'Q: bu holat amalda faqat buzuq
            // klientda uchraydi va uni 400 sifatida ko'rsatish yetarli
            // ma'noli (controller `Content-Range: bytes */N` sarlavhasini
            // ham qo'yadi).
            throw new RangeNotSatisfiableException(asset.SizeBytes);
        }

        var requested = outcome == RangeParseOutcome.Satisfiable ? range : null;

        var stored = await storage.OpenReadAsync(asset.ObjectKey, requested, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(LessonAsset), assetId);

        // ★ TUR BAZADAN ustun: yuklashda u MAZMUNDAN aniqlangan edi, ombor
        //   esa faqat biz yozgan sarlavhani qaytaradi (va u yo'qolgan ham
        //   bo'lishi mumkin).
        var contentType = Normalize(asset.ContentType) ?? stored.ContentType;

        // Ombor `Range` ni BAJARMAGAN bo'lsa (206 emas) — to'liq javob
        // beriladi. Controller shu qiymatga qarab 200/206 tanlaydi, o'zi
        // taxmin qilmaydi.
        var effectiveRange = stored.IsPartial ? requested : null;

        return new LessonAssetDownload(
            stored,
            contentType,
            SuggestFileName(asset),
            stored.TotalLength ?? asset.SizeBytes,
            effectiveRange);
    }

    // ================================================================= o'chirish

    public async Task DeleteAsync(long assetId, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct).ConfigureAwait(false);
        EnsureCanManage(actor);

        var asset = await db.LessonAssets.AsTracking()
            .FirstOrDefaultAsync(a => a.Id == assetId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(LessonAsset), assetId);

        var lessonId = asset.LessonId;
        var objectKey = asset.ObjectKey;

        db.LessonAssets.Remove(asset);

        // Qolganlar tartibi ZICH qolsin ("teshik" qolmasin) — o'chirish
        // bilan BITTA tranzaksiyada.
        PositionOrdering.Reindex(
            await db.LessonAssets.AsTracking()
                .Where(a => a.LessonId == lessonId && a.Id != assetId)
                .OrderBy(a => a.Position).ThenBy(a => a.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false),
            a => a.Id,
            (a, position) => a.Position = position);

        // ★ AVVAL BAZA (sabab: sinf sarlavhasidagi 3-band).
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await TryDeleteFromStorageAsync(objectKey, ct).ConfigureAwait(false);
    }

    // ================================================================= tartib

    public async Task<IReadOnlyList<PositionDto>> ReorderAsync(
        long lessonId, ReorderRequest request, long actorId, CancellationToken ct = default)
    {
        var actor = await LoadActorAsync(actorId, ct).ConfigureAwait(false);
        EnsureCanManage(actor);

        if (!await db.ModuleLessons.AsNoTracking().AnyAsync(l => l.Id == lessonId, ct)
                .ConfigureAwait(false))
        {
            throw new NotFoundException(nameof(ModuleLesson), lessonId);
        }

        var rows = await db.LessonAssets.AsTracking()
            .Where(a => a.LessonId == lessonId)
            .OrderBy(a => a.Position).ThenBy(a => a.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // 🔴 TO'LIQ ro'yxat kutiladi — yetishmasa 400 va HECH NARSA
        //    yozilmaydi (7-tuzoq).
        var result = PositionOrdering.Reindex(
            PositionOrdering.ArrangeByRequest(rows, request, a => a.Id, "Dars fayllari"),
            a => a.Id,
            (a, position) => a.Position = position);

        // BITTA SaveChanges = BITTA tranzaksiya: yarim tartib MUMKIN EMAS.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return result;
    }

    // ================================================================= RUXSAT

    /// <summary>
    /// Kontentni O'ZGARTIRISH huquqi — <c>ICourseService.EnsureCanManage</c>
    /// bilan AYNI qoida: faqat o'quv bo'limi va admin.
    ///
    /// USTOZ VA KURATOR ATAYLAB CHETDA: dars mediasi BARCHA guruhlarga
    /// tegishli — bitta ustoz videoni o'chirsa yoki tartibini almashtirsa,
    /// bu o'ntalab guruhning o'quvchisiga ta'sir qilardi.
    /// </summary>
    private static void EnsureCanManage(User actor)
    {
        if (actor.Role is not (UserRole.Admin or UserRole.Academic))
        {
            throw new ForbiddenException(
                "Dars fayllarini faqat o'quv bo'limi xodimi yoki administrator "
                + "o'zgartira oladi. Ustoz va kurator ularni faqat ko'ra oladi.");
        }
    }

    /// <summary>
    /// 🔴 O'QISH RUXSATI — SERVERDA, HAR SO'ROVDA (izoh: sinf sarlavhasi).
    /// </summary>
    private async Task EnsureCanReadAsync(User actor, long lessonId, CancellationToken ct)
    {
        // XODIM — har doim. Ustoz/kurator ham ko'radi: darsni o'tishdan oldin
        // materialni ko'rib chiqishi ish talabi.
        if (actor.Role != UserRole.Student) return;

        // 1) TO'LOV BLOKI. Video — bloklashda ENG AVVAL yopiladigan qamrov
        //    (`PaymentBlockScope.Video`).
        await paymentBlock
            .EnsureAllowedAsync(actor.Id, PaymentBlockScope.Video, ct)
            .ConfigureAwait(false);

        // 2) GATING. BITTA ARZON tekshiruv ikkala savolga javob beradi:
        //    "bu dars mening kursimdami" va "dars ochiqmi" — begona kursning
        //    darsi gating uchun ham `NotInCourse` bo'ladi
        //    (`AssignmentService.SubmitAsync` dagi AYNI mulohaza).
        await gating.EnsureLessonUnlockedAsync(actor.Id, lessonId, ct).ConfigureAwait(false);
    }

    private async Task<User> LoadActorAsync(long actorId, CancellationToken ct)
    {
        // Rol TOKEN'dan emas, BAZADAN: kirish tokeni 15 daqiqa yashaydi,
        // ya'ni roli pasaytirilgan xodim eski token bilan kontentni
        // o'zgartira olmasligi kerak.
        var actor = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        return actor;
    }

    // ================================================================= chegara va tur

    /// <summary>
    /// Hajm chegarasi — SOZLAMALAR registridan (kodda QOTMAYDI).
    ///
    /// Buzuq qiymat ilovani yiqitmaydi: registrdagi standartga qaytadi
    /// (`FinanceSettingsStore` bilan AYNI naqsh).
    /// </summary>
    private async Task<long> LimitBytesAsync(LessonAssetKind kind, CancellationToken ct)
    {
        var definition = kind == LessonAssetKind.Video ? VideoLimitSetting : ImageLimitSetting;

        var resolved = await settings.ResolveAsync(definition, ct).ConfigureAwait(false);

        var megabytes = SettingValueParser.TryReadDecimal(definition, resolved.Value, out var value)
            ? value
            : decimal.Parse(definition.DefaultValue, CultureInfo.InvariantCulture);

        return (long)megabytes * 1024 * 1024;
    }

    /// <summary>
    /// Fayl turini SEHRLI BAYTLARDAN aniqlaydi.
    ///
    /// 🔴 KENGAYTMAGA VA `Content-Type` SARLAVHASIGA ISHONILMAYDI: ikkalasini
    /// ham klient xohlagan qiymatga yozadi. `.mp4` deb nomlangan PDF shu
    /// yerda 400 oladi.
    /// </summary>
    private static async Task<MediaSignature> DetectAsync(
        LessonAssetUpload upload, LessonAssetKind kind, CancellationToken ct)
    {
        var allowed = kind == LessonAssetKind.Video
            ? MediaCategories.Video
            : MediaCategories.Image;

        var header = new byte[MediaSignatures.HeaderSize];

        upload.Content.Position = 0;

        // EOF'da xato bermaydi — kichik fayl ham bo'lishi mumkin.
        var length = await upload.Content
            .ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct)
            .ConfigureAwait(false);

        if (length == 0)
            throw Invalid("file", "Fayl bo'sh.");

        if (!MediaSignatures.TryDetect(header.AsSpan(0, length), allowed, out var signature))
        {
            var expected = kind == LessonAssetKind.Video
                ? "Video (mp4, webm, mov)"
                : "Rasm (jpg, png, webp)";

            throw Invalid(
                "file",
                $"Faylning turi qo'llab-quvvatlanmaydi. {expected} yuboring. "
                + $"Klient aytgan tur: {Describe(upload.ClientContentType)}. "
                + "⚠️ Fayl NOMI hisobga olinmaydi — tur fayl MAZMUNIDAN aniqlanadi.");
        }

        return signature;
    }

    // ================================================================= ichki

    /// <summary>
    /// Ombordan o'chirishga urinadi va XATO KO'TARMAYDI.
    ///
    /// Baza allaqachon o'zgargan (yoki o'zgarishi bekor qilingan) — bu
    /// nuqtada istisno ko'tarish foydalanuvchiga yolg'on aytardi
    /// ("o'chirilmadi", holbuki yozuv o'chgan). Yetim obyekt esa faqat joy
    /// egallaydi (sabab: sinf sarlavhasidagi 3-band).
    /// </summary>
    private async Task TryDeleteFromStorageAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(objectKey, ct).ConfigureAwait(false);
        }
        catch (ServiceUnavailableException ex)
        {
            LessonAssetLog.OrphanedObject(logger, ex, objectKey);
        }
    }

    /// <summary>
    /// Yuklab olinadigan fayl nomi.
    ///
    /// 🔴 OBYEKT KALITI NOM SIFATIDA BERILMAYDI: unda ichki tuzilma bor va
    /// u omborimiz sxemasini oshkor qiladi (`AssignmentService.SuggestFileName`
    /// bilan AYNI qoida). Kengaytma MIME turidan olinadi.
    /// </summary>
    private static string SuggestFileName(AssetRow asset)
    {
        var extension = Path.GetExtension(asset.ObjectKey.AsSpan());
        var prefix = asset.Kind == LessonAssetKind.Video ? "video" : "rasm";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}-{asset.Id}{extension}");
    }

    private static LessonAssetDto Map(LessonAsset asset) =>
        new(asset.Id,
            asset.LessonId,
            asset.Kind,
            asset.Position,
            asset.Title,
            asset.ContentType,
            asset.SizeBytes,
            asset.DurationSec,
            asset.Width,
            asset.Height,
            asset.CreatedAt);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Describe(string? clientContentType) =>
        string.IsNullOrWhiteSpace(clientContentType) ? "ko'rsatilmagan" : clientContentType;

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private static PayloadTooLargeException TooLarge(LessonAssetKind kind, long limitBytes)
    {
        var megabytes = (limitBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture);
        var what = kind == LessonAssetKind.Video ? "Video" : "Rasm";
        var key = kind == LessonAssetKind.Video
            ? SettingsRegistry.Keys.LessonVideoMaxMb
            : SettingsRegistry.Keys.LessonImageMaxMb;

        return new PayloadTooLargeException(
            $"{what} hajmi {megabytes} MB dan oshmasligi kerak. "
            + $"Chegarani administrator sozlamalardan (`{key}`) o'zgartira oladi.");
    }

    private static readonly SettingDefinition VideoLimitSetting =
        Definition(SettingsRegistry.Keys.LessonVideoMaxMb);

    private static readonly SettingDefinition ImageLimitSetting =
        Definition(SettingsRegistry.Keys.LessonImageMaxMb);

    private static SettingDefinition Definition(string key) =>
        SettingsRegistry.TryGet(key, out var definition)
            ? definition

            // FAQAT registr buzilganda — ya'ni dasturchi xatosi. Jimgina
            // standartga qaytish chegara ishlamayotganini oylab payqatmasdi.
            : throw new InvalidOperationException($"Registrda '{key}' sozlamasi yo'q.");

    /// <summary>Ruxsat va oqim uchun kerakli MINIMAL maydonlar (butun entity emas).</summary>
    private sealed record AssetRow(
        long Id,
        long LessonId,
        LessonAssetKind Kind,
        string ObjectKey,
        string ContentType,
        long SizeBytes);
}

/// <summary>Manba-generatsiyali log metodlari (CA1848). EventId makoni: 5200–5209.</summary>
internal static partial class LessonAssetLog
{
    [LoggerMessage(
        EventId = 5200,
        Level = LogLevel.Warning,
        Message = "Ombordagi obyekt o'chirilmadi — YETIM qoldi (baza yozuvi allaqachon "
                  + "o'chirilgan). Yig'ishtiruvchi vazifa uni keyinroq olib tashlaydi. key={Key}")]
    internal static partial void OrphanedObject(ILogger logger, Exception exception, string key);
}
