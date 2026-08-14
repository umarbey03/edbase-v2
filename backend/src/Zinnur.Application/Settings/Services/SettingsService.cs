using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Settings.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Settings.Services;

/// <summary>
/// <see cref="ISettingsService"/> amalga oshirilishi.
/// HTTP haqida hech narsa bilmaydi — faqat Application xatolarini ko'taradi.
/// </summary>
public sealed class SettingsService(
    IApplicationDbContext db,
    ISettingsStore store,
    ISettingsResolver resolver,
    IRuntimeSettings runtime,
    TimeProvider clock,
    ILogger<SettingsService> logger) : ISettingsService
{
    /// <summary>
    /// Audit yozuvidagi obyekt nomi. <c>PaymentService.UpdateSettingsAsync</c>
    /// moliya sozlamasini AYNAN shu nom bilan yozadi — bir xil qoldirilishi
    /// SHART, aks holda "shu chegara kim tomonidan o'zgartirilgan?" degan
    /// savolga javob ikki xil nom ostida bo'linib ketardi.
    /// </summary>
    private const string AuditEntity = "settings";

    private const string ActionUpdate = "update";
    private const string ActionReset = "reset";

    public async Task<SettingsPageDto> ListAsync(long actorId, CancellationToken ct = default)
    {
        await LoadAdminAsync(actorId, ct).ConfigureAwait(false);

        var resolved = await resolver.ResolveAllAsync(ct).ConfigureAwait(false);

        var groups = new List<SettingGroupDto>(SettingsRegistry.Groups.Count);

        foreach (var group in SettingsRegistry.Groups)
        {
            var items = resolved
                .Where(r => r.Definition.Group == group)
                .Select(ToDto)
                .ToArray();

            groups.Add(new SettingGroupDto(
                group,
                SettingsRegistry.GroupName(group),
                SettingsRegistry.GroupDescription(group),
                items));
        }

        return new SettingsPageDto(groups);
    }

    public async Task<SettingDto> GetAsync(
        string key, long actorId, CancellationToken ct = default)
    {
        await LoadAdminAsync(actorId, ct).ConfigureAwait(false);

        var definition = Require(key);
        var resolved = await resolver.ResolveAsync(definition, ct).ConfigureAwait(false);

        return ToDto(resolved);
    }

    public async Task<SettingDto> UpdateAsync(
        string key, UpdateSettingRequest request, long actorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await LoadAdminAsync(actorId, ct).ConfigureAwait(false);

        var definition = Require(key);
        EnsureEditable(definition);
        await EnsureNotOverriddenAsync(definition, actorId, ct).ConfigureAwait(false);

        if (!SettingValueParser.TryNormalize(definition, request.Value, out var normalized, out var error))
            throw Invalid(definition.Key, error);

        // Bog'langan to'plam (`Storage:*`, `Telegram:*`) buzilmasin — bu
        // ilgari `ValidateOnStart` bajargan "TO'LIQ yoki BO'SH" himoyasining
        // yozish paytidagi o'rinbosari (izoh: `SettingCoupling`).
        await EnsureSetNotBrokenAsync(definition, normalized, ct).ConfigureAwait(false);

        var before = await resolver.ResolveAsync(definition, ct).ConfigureAwait(false);

        // Qiymat o'zgarmagan bo'lsa ham yozib qo'yamiz (idempotent PUT), lekin
        // AUDITGA yozmaymiz: "o'zgarish yo'q" degan yozuv audit izini shovqin
        // bilan to'ldirib, haqiqiy o'zgarishlarni ko'rinmas qilardi.
        var changed = !string.Equals(before.Value, normalized, StringComparison.Ordinal);

        await store.SetAsync(definition.StorageKey, normalized, actorId, ct).ConfigureAwait(false);

        if (changed)
            AddAudit(definition, ActionUpdate, before.Value, normalized, actorId);

        // ★ BITTA SaveChanges: sozlama qatori ham, audit yozuvi ham AYNI
        // `DbContext` kuzatuvchisida to'plangan, ya'ni bitta tranzaksiyada
        // saqlanadi. Alohida saqlansa, sozlama o'zgarib audit yozilmay
        // qolishi mumkin edi.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // 🔴 SHU YERDA VA AYNAN SHU TARTIBDA. Kesim SaveChanges'DAN KEYIN
        // yangilanadi (aks holda o'zi hali ko'rmagan qiymatni o'qirdi) va
        // javob qaytishidan OLDIN (aks holda panel "saqlandi" deb turgan
        // paytda tizim eski qiymat bilan ishlayverardi — ya'ni tuzatilayotgan
        // muammoning o'zi qaytardi).
        await runtime.RefreshAsync(ct).ConfigureAwait(false);

        SettingsLog.Changed(logger, definition.Key, ActionUpdate, actorId, definition.IsSecret);

        return ToDto(await resolver.ResolveAsync(definition, ct).ConfigureAwait(false));
    }

    public async Task<SettingDto> ResetAsync(
        string key, long actorId, CancellationToken ct = default)
    {
        await LoadAdminAsync(actorId, ct).ConfigureAwait(false);

        var definition = Require(key);
        EnsureEditable(definition);
        await EnsureNotOverriddenAsync(definition, actorId, ct).ConfigureAwait(false);

        var targets = await ResolveResetTargetsAsync(definition, ct).ConfigureAwait(false);
        var anyRemoved = false;

        foreach (var target in targets)
        {
            var before = await resolver.ResolveAsync(target, ct).ConfigureAwait(false);

            if (!await store.RemoveAsync(target.StorageKey, ct).ConfigureAwait(false))
                continue;

            AddAudit(target, ActionReset, before.Value, newValue: null, actorId);
            anyRemoved = true;
        }

        if (anyRemoved)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await runtime.RefreshAsync(ct).ConfigureAwait(false);

            foreach (var target in targets)
                SettingsLog.Changed(logger, target.Key, ActionReset, actorId, target.IsSecret);
        }

        return ToDto(await resolver.ResolveAsync(definition, ct).ConfigureAwait(false));
    }

    // ================================================================= to'plam qoidasi

    /// <summary>
    /// Yangi qiymat ISHLAB TURGAN bog'langan to'plamni buzmasligini tekshiradi.
    ///
    /// ★ NIMA UCHUN "hozirgi holat + nomzod" solishtiriladi, "nomzod o'zi"
    /// emas: to'plam qoidasi bitta kalit haqida emas, ULARNING BIRGALIKDAGI
    /// holati haqida. Shuning uchun qiymatlar registrdagi USTUNLIK qoidasi
    /// bo'yicha (baza -&gt; muhit -&gt; standart) o'qiladi — ya'ni panel
    /// ko'rsatayotgan va tizim ishlatayotgan AYNI qiymatlar.
    /// </summary>
    private async Task EnsureSetNotBrokenAsync(
        SettingDefinition definition, string candidate, CancellationToken ct)
    {
        var rule = SettingCoupling.RuleFor(definition.Key);

        if (rule is null)
            return;

        var before = await ReadSetAsync(rule, ct).ConfigureAwait(false);

        var after = new Dictionary<string, string?>(before, StringComparer.Ordinal)
        {
            [definition.Key] = candidate,
        };

        var breakage = SettingCoupling.Breakage(
            rule, k => before.GetValueOrDefault(k), k => after.GetValueOrDefault(k));

        if (breakage is not null)
            throw Invalid(definition.Key, breakage);
    }

    /// <summary>
    /// "Standartga qaytarish" QAYSI kalitlarga tegishi kerakligini aniqlaydi.
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// ★★ BOG'LANGAN TO'PLAM BUTUNLIGICHA QAYTARILADI — VA BU ATAYLAB.
    ///
    /// Holat: `Storage:*` ning to'rttasi ham FAQAT bazada (muhitda yo'q).
    /// Bitta kalitni qaytarish to'plamni YARIM sozlangan holatga tushirardi,
    /// ya'ni "TO'LIQ yoki BO'SH" qoidasini buzardi. Uni shunchaki RAD ETISH
    /// esa boshi berk ko'chaga olib borardi: to'plamni o'chirish uchun
    /// to'rttasini ham qaytarish kerak, LEKIN har birinchi urinish rad
    /// etilardi — ya'ni omborni paneldan o'chirish UMUMAN mumkin bo'lmasdi.
    ///
    /// Shuning uchun bunday holatda amal BUTUN to'plamga qo'llanadi:
    /// natijada to'plam YARIM emas, BO'SH bo'ladi — qoida saqlanadi va
    /// operator maqsadiga eriladi. Har kalit uchun ALOHIDA audit yozuvi
    /// qoladi, ya'ni "nima o'zgardi?" degan savol javobsiz qolmaydi.
    ///
    /// Odatiy holatda (muhitda qiymat bor) bu shart umuman ishlamaydi:
    /// qaytarilgan kalit muhitdagi qiymatga qaytadi, to'plam TO'LIQ qoladi
    /// va faqat SO'RALGAN kalit o'chiriladi.
    /// ══════════════════════════════════════════════════════════════════════
    /// </summary>
    private async Task<IReadOnlyList<SettingDefinition>> ResolveResetTargetsAsync(
        SettingDefinition definition, CancellationToken ct)
    {
        var rule = SettingCoupling.RuleFor(definition.Key);

        if (rule is null)
            return [definition];

        var before = await ReadSetAsync(rule, ct).ConfigureAwait(false);

        var fallback = await resolver
            .ResolveWithoutStoredAsync(definition, ct)
            .ConfigureAwait(false);

        var after = new Dictionary<string, string?>(before, StringComparer.Ordinal)
        {
            [definition.Key] = fallback.Value,
        };

        var breaks = SettingCoupling.Breakage(
            rule, k => before.GetValueOrDefault(k), k => after.GetValueOrDefault(k)) is not null;

        if (!breaks)
            return [definition];

        return [.. rule.Keys.Select(k => SettingsRegistry.TryGet(k, out var member) ? member : null!)];
    }

    /// <summary>To'plam a'zolarining AMALDAGI qiymatlari.</summary>
    private async Task<Dictionary<string, string?>> ReadSetAsync(
        SettingCouplingRule rule, CancellationToken ct)
    {
        var definitions = rule.Keys
            .Select(k => SettingsRegistry.TryGet(k, out var member) ? member : null!)
            .ToArray();

        var resolved = await resolver.ResolveManyAsync(definitions, ct).ConfigureAwait(false);

        return resolved.ToDictionary(r => r.Definition.Key, r => r.Value, StringComparer.Ordinal);
    }

    // ================================================================= ruxsat

    /// <summary>
    /// Aktyorni BAZADAN o'qiydi va <c>Admin</c> ekanini tekshiradi.
    ///
    /// 🔴 Rol JWT claim'idan OLINMAYDI: kirish tokeni 15 daqiqa yaroqli,
    /// ya'ni roli endigina pasaytirilgan xodim eski token bilan sozlamalarni
    /// o'zgartira olardi. Bu tekshiruv controller'dagi <c>[Authorize]</c>
    /// darvozasining O'RNIGA emas, USTIGA qo'yiladi.
    /// </summary>
    private async Task<User> LoadAdminAsync(long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(User), actorId);

        if (!actor.IsActive)
            throw new ForbiddenException("Profilingiz faol emas.");

        if (actor.Role != UserRole.Admin)
        {
            SettingsLog.AccessDenied(logger, actorId, actor.Role.ToString());
            throw new ForbiddenException("Tizim sozlamalari faqat administrator uchun.");
        }

        return actor;
    }

    private static SettingDefinition Require(string key)
    {
        if (!SettingsRegistry.TryGet(key, out var definition))
        {
            // Noma'lum kalit 404: registr yopiq ro'yxat, ya'ni "shunday
            // sozlama umuman yo'q" — bu 400 emas, topilmadi holati.
            throw new NotFoundException("Sozlama", key ?? "(bo'sh)");
        }

        return definition;
    }

    /// <summary>
    /// "Faqat o'qish" kalitini o'zgartirishga urinish.
    ///
    /// ★ NIMA UCHUN 400, 403 EMAS: 403 "sizning huquqingiz yetmaydi" degani
    /// bo'lardi — lekin bu kalitni HECH KIM, hatto administrator ham
    /// o'zgartira olmaydi. Muammo so'rovning O'ZIDA. 400 tanlangani yana bir
    /// amaliy sabab bilan: frontend <c>problem.errors</c> ni o'qiydi va
    /// sababni maydon yonida ko'rsata oladi.
    /// </summary>
    private static void EnsureEditable(SettingDefinition definition)
    {
        if (definition.IsEditable)
            return;

        throw Invalid(
            definition.Key,
            definition.ReadOnlyReason ?? "Bu sozlama paneldan o'zgartirilmaydi.");
    }

    /// <summary>
    /// 🔴 Qiymat shoshilinch muhit o'zgaruvchisi bilan ustidan yozilgan
    /// bo'lsa yozishni RAD ETADI.
    ///
    /// ★ NIMA UCHUN <see cref="EnsureEditable"/> GA QO'SHILMADI: u sof
    /// funksiya (faqat registrga qaraydi) va shu sababli testda bazasiz
    /// tekshiriladi. Bu shart esa MUHITGA bog'liq, ya'ni hisoblash uchun
    /// resolver kerak. Ikkalasini aralashtirish sof funksiyani ham
    /// asinxron qilib, oddiy holatni qimmatlashtirardi.
    ///
    /// ★ 400 (403 emas): sabab administratorning huquqida emas — bu
    /// kalitni hozir HECH KIM o'zgartira olmaydi. Frontend
    /// <c>problem.errors[key][0]</c> ni o'qib, sababni maydon yonida
    /// ko'rsatadi (registrdagi "faqat o'qish" holati bilan bir xil naqsh).
    /// </summary>
    private async Task EnsureNotOverriddenAsync(
        SettingDefinition definition, long actorId, CancellationToken ct)
    {
        if (definition.OverrideConfigurationKey is not { Length: > 0 })
            return;

        var resolved = await resolver.ResolveAsync(definition, ct).ConfigureAwait(false);

        if (!resolved.IsOverridden)
            return;

        SettingsLog.OverrideBlocked(logger, definition.Key, actorId);

        throw Invalid(
            definition.Key,
            definition.OverrideReason ?? "Bu qiymat muhit o'zgaruvchisi bilan ustidan yozilgan.");
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    // ================================================================= audit

    private void AddAudit(
        SettingDefinition definition,
        string action,
        string? oldValue,
        string? newValue,
        long actorId)
    {
        // 🔴 Sir bo'lsa qiymatlar KESILADI. Qoida ATAYLAB alohida sinfda
        // (`SettingAuditPolicy`): u bazasiz test qilinadi va yangi audit
        // chaqiruvi qo'shilganda takrorlash unutilmaydi.
        var values = SettingAuditPolicy.For(definition, oldValue, newValue);

        db.PaymentAudits.Add(new PaymentAudit
        {
            // ★ NIMA UCHUN ALOHIDA JADVAL YARATILMADI: `PaymentAudits`
            // allaqachon "kim, qachon, nimani, nimadan-nimaga" shaklida
            // umumiy audit izi bo'lib ishlaydi va moliya sozlamasi o'zgarishi
            // HOZIRNING O'ZIDA shu yerga yoziladi (`PaymentService`).
            // Ikkinchi jadval qo'shilsa, bitta chegara ikki xil joyda
            // auditlanib, "kim o'zgartirdi?" degan savolga javob bo'linib
            // ketardi — ya'ni topshiriqda ogohlantirilgan "ikki parallel
            // tizim" aynan audit ichida paydo bo'lardi.
            Entity = AuditEntity,
            Action = action,

            // Polimorf havola ishlatilmaydi: sozlamaning raqamli id'si yo'q,
            // uni ANIQLAYDIGAN narsa — kalit, u esa `Field` ga yoziladi.
            EntityId = null,
            StudentId = null,

            // Sozlamani ANIQLAYDIGAN narsa — kalit (registrdagi ommaviy nom).
            Field = definition.Key,

            OldValue = values.OldValue,
            NewValue = values.NewValue,
            Note = values.Note,

            ActorId = actorId,
            CreatedAt = clock.GetUtcNow(),
        });
    }

    // ================================================================= xaritalash

    /// <summary>
    /// Ichki <see cref="ResolvedSetting"/> ni javob DTO'siga o'giradi.
    ///
    /// 🔴 BU YAGONA CHIQISH NUQTASI. Sir qiymat aynan shu yerda kesiladi —
    /// shuning uchun DTO'ni boshqa joyda qo'lda yasash TAQIQ.
    /// </summary>
    private static SettingDto ToDto(ResolvedSetting resolved)
    {
        var definition = resolved.Definition;
        var secret = definition.IsSecret;

        // ══════════════════════════════════════════════════════════════
        // 🔴 SHOSHILINCH USTIDAN YOZISH — MAYDON QULFLANADI.
        //
        // `IsEditable` odatda STATIK (registrdagi manbaga qarab). Bu
        // yagona holat uni ISH JARAYONIDA pasaytiradi, chunki sabab ham
        // ish jarayonida paydo bo'ladi: muhit o'zgaruvchisi qo'yilgan.
        //
        // ★ BUNSIZ NIMA BO'LARDI: administrator tokenni paneldan
        //   almashtirardi, server "saqlandi" deb 200 qaytarardi, qator
        //   bazaga tushardi — va tizim baribir muhitdagi eski qiymat
        //   bilan ishlayverardi. Sozlamalar registrining butun izohi
        //   aynan shu holatni ("jimgina yolg'on") eng yomon xato deb
        //   ataydi.
        //
        // ★ NIMA UCHUN YOZISH HAM TO'SILADI (`EnsureEditable`): faqat
        //   UI'da yashirish yetarli emasdi — API to'g'ridan-to'g'ri
        //   chaqirilishi mumkin, va bazaga tushib qolgan qator
        //   o'zgaruvchi olib tashlangan kunda KUTILMAGANDA kuchga
        //   kirardi.
        // ══════════════════════════════════════════════════════════════
        var editable = definition.IsEditable && !resolved.IsOverridden;

        var readOnlyReason = resolved.IsOverridden
            ? definition.OverrideReason
            : definition.ReadOnlyReason;

        return new SettingDto(
            definition.Key,
            definition.Group,
            SettingsRegistry.GroupName(definition.Group),
            definition.DisplayName,
            definition.Description,
            definition.Kind,
            secret,
            editable,
            readOnlyReason,
            resolved.Origin,
            resolved.IsSet,

            // Sir HECH QACHON to'liq qaytmaydi.
            secret ? null : resolved.Value,
            secret ? SettingMask.Mask(resolved.Value) : null,

            // Standart qiymat ham: sirning standarti bo'lmaydi, bo'lsa ham
            // uni ko'rsatish sirni oshkor qilish bilan barobar.
            secret || definition.DefaultValue.Length == 0 ? null : definition.DefaultValue,

            new SettingConstraintsDto(
                definition.Choices,
                definition.Minimum,
                definition.Maximum,
                definition.MaxLength,
                definition.Format),

            resolved.UpdatedAt,
            resolved.UpdatedById);
    }
}

/// <summary>
/// Sozlamalar moduli loglari.
///
/// ★ NIMA UCHUN <c>[LoggerMessage]</c>: CA1848. <c>logger.LogInformation(...)</c>
/// har chaqiruvda massiv ajratadi va argumentlarni bokslaydi; manba-generator
/// esa buni kompilyatsiya vaqtida hal qiladi.
///
/// 🔴 QIYMAT LOGGA YOZILMAYDI — faqat kalit nomi. Sozlama qiymati sir
/// bo'lishi mumkin, log esa Sentry'ga va konteyner chiqishiga ketadi.
/// </summary>
internal static partial class SettingsLog
{
    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Information,
        Message = "Sozlama o'zgardi. key={Key} amal={Action} actorId={ActorId} sir={IsSecret}")]
    internal static partial void Changed(
        ILogger logger, string key, string action, long actorId, bool isSecret);

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Warning,
        Message = "Sozlamalarga ruxsatsiz urinish. actorId={ActorId} rol={Role}")]
    internal static partial void AccessDenied(ILogger logger, long actorId, string role);

    /// <summary>
    /// ★ NIMA UCHUN OGOHLANTIRISH DARAJASIDA: bu hodisa tizim SHOSHILINCH
    /// rejimda ekanini bildiradi — kimdir tokenni paneldan tuzatmoqchi,
    /// lekin muhit o'zgaruvchisi hali olib tashlanmagan. Aynan shu
    /// "yarim tiklangan" holat unutilib ketishi mumkin, shuning uchun u
    /// logda ko'rinib turishi kerak.
    /// </summary>
    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Warning,
        Message = "Sozlama muhit o'zgaruvchisi bilan ustidan yozilgan — yozish rad etildi. "
                  + "key={Key} actorId={ActorId}")]
    internal static partial void OverrideBlocked(ILogger logger, string key, long actorId);
}
