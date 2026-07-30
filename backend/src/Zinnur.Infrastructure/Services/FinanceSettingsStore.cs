using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Options;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="IFinanceSettingsStore"/> port'ining amalga oshirilishi:
/// chegara va qamrov BAZADAN (<c>AppSettings</c>), qattiq rejim kaliti
/// KONFIGURATSIYADAN. Sabab port izohida.
///
/// ── KALIT NOMLARI ESKI TIZIM BILAN BIR XIL ─────────────────────────────
/// <c>payment_block_threshold</c>, <c>payment_block_scope</c> — ma'lumot
/// ko'chirish skripti eski <c>settings</c> jadvalidan qiymatlarni AYNAN shu
/// kalitlar bilan ko'chira oladi va qiymatni o'zgartirish shart emas
/// (qamrov <c>"video"</c> ham, <c>"Video"</c> ham o'qiladi).
///
/// ── KESH YO'Q (ONGLI TANLOV) ───────────────────────────────────────────
/// Bu ikki qator birlamchi kalit bo'yicha o'qiladi — Postgres uchun eng
/// arzon so'rov, va u faqat blok tekshiruvida bajariladi. Redis keshi
/// qo'shilsa, chegara o'zgartirilganda uni bekor qilishni UNUTISH xavfi
/// paydo bo'lardi: xodim raqamni o'zgartirib, "nega ishlamayapti" deb
/// qolardi. Kesh kerak bo'lsa — o'lchov bilan, TTL emas, oshkor bekor
/// qilish bilan qo'shilsin.
/// </summary>
public sealed class FinanceSettingsStore(
    ApplicationDbContext db,
    IOptions<PaymentsOptions> options) : IFinanceSettingsStore
{
    /// <summary>Eski tizim bilan bir xil kalit — ko'chirish skripti uchun.</summary>
    public const string ThresholdKey = "payment_block_threshold";

    /// <summary>Eski tizim bilan bir xil kalit.</summary>
    public const string ScopeKey = "payment_block_scope";

    public async Task<FinanceSettings> GetAsync(CancellationToken ct = default)
    {
        var rows = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == ThresholdKey || s.Key == ScopeKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        var defaults = options.Value;

        return new FinanceSettings(
            ParseThreshold(Find(rows, ThresholdKey), defaults.DefaultBlockThreshold),
            ParseScope(Find(rows, ScopeKey), defaults.DefaultBlockScope),
            defaults.EnforceBlock);
    }

    public async Task<FinanceSettings> SaveAsync(
        decimal blockThreshold,
        PaymentBlockScope blockScope,
        long? actorId,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        await UpsertAsync(
            ThresholdKey,
            blockThreshold.ToString(CultureInfo.InvariantCulture),
            actorId, now, ct).ConfigureAwait(false);

        // Qiymat ENUM NOMI sifatida yoziladi ("Video"), raqam sifatida emas:
        // bazani qo'lda ko'rgan odam nima yozilganini tushunishi kerak, va
        // enum raqamlari kelajakda ma'no o'zgartirsa qiymat jimgina boshqa
        // qamrovga aylanardi.
        await UpsertAsync(
            ScopeKey,
            blockScope.ToString(),
            actorId, now, ct).ConfigureAwait(false);

        // ★ SaveChanges CHAQIRILMAYDI: chaqiruvchi (PaymentService) audit
        // yozuvini ham qo'shadi va HAMMASINI bitta tranzaksiyada saqlaydi.
        // Bu yerda saqlansa, sozlama o'zgarib, audit esa yozilmay qolishi
        // mumkin edi.
        return new FinanceSettings(blockThreshold, blockScope, options.Value.EnforceBlock);
    }

    private async Task UpsertAsync(
        string key, string value, long? actorId, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = value,
                UpdatedAt = now,
                UpdatedById = actorId,
            });

            return;
        }

        row.Value = value;
        row.UpdatedAt = now;
        row.UpdatedById = actorId;
    }

    private static string? Find(Dictionary<string, string> rows, string key) =>
        rows.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Buzuq qiymat ilovani YIQITMAYDI — standartga qaytadi. Sabab: bu qator
    /// qo'lda ham tahrirlanishi mumkin, va "chegara satri xato" degan holat
    /// butun platformani ishdan chiqarmasligi kerak. Xavfsiz yo'nalish —
    /// standart qiymat (bloklash O'CHIB qolmaydi va tasodifan hamma
    /// bloklanmaydi ham).
    /// </summary>
    private static decimal ParseThreshold(string? raw, decimal fallback) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
        && value >= 0
            ? value
            : fallback;

    /// <summary>Eski tizimdagi <c>"video"</c> kabi kichik harfli qiymat ham o'qiladi.</summary>
    private static PaymentBlockScope ParseScope(string? raw, PaymentBlockScope fallback) =>
        Enum.TryParse<PaymentBlockScope>(raw, ignoreCase: true, out var value)
        && Enum.IsDefined(value)
            ? value
            : fallback;
}
