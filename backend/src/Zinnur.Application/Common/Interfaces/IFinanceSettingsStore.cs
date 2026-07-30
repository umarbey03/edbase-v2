using Zinnur.Domain.Enums;

namespace Zinnur.Application.Common.Interfaces;

/// <summary>
/// Bloklash sozlamalarining JORIY qiymati.
/// </summary>
/// <param name="BlockThreshold">
/// Qarz chegarasi (so'm). Shundan OSHGANDA blok tushadi (teng bo'lsa —
/// bloklanmaydi, qoida <c>PaymentBlockPolicy</c> da).
/// </param>
/// <param name="BlockScope">Qamrov: <c>None|Video|Live|Platform</c>.</param>
/// <param name="Enforce">
/// "Qattiq rejim" kaliti. <c>false</c> bo'lsa qarz baribir hisoblanadi va
/// ko'rsatiladi, lekin hech kim bloklanmaydi.
/// </param>
public sealed record FinanceSettings(
    decimal BlockThreshold,
    PaymentBlockScope BlockScope,
    bool Enforce);

/// <summary>
/// ========================================================================
/// MOLIYA SOZLAMALARI UCHUN PORT
/// ========================================================================
///
/// ★ QAYERDA SAQLANADI VA NIMA UCHUN (qaror, FAZA 4.3):
///
///  • <c>payment_block_threshold</c> va <c>payment_block_scope</c> — BAZADA
///    (<c>AppSettings</c> kalit-qiymat jadvali, eski tizimdagi <c>settings</c>
///    jadvalining o'rnini bosadi). Sababi: chegara — BIZNES qarori. Tariflar
///    ko'tarilganda o'quv bo'limi boshlig'i uni "540 000 dan 600 000 ga"
///    o'zgartirishi kerak, va buning uchun relizni kutish yoki server
///    konfiguratsiyasiga tegish noto'g'ri bo'lardi. Bazada bo'lgani uchun
///    o'zgarish darhol kuchga kiradi va auditga tushadi.
///
///  • <c>Enforce</c> (yumshoq rejim) — KONFIGURATSIYADA
///    (<c>Payments:EnforceBlock</c>, muhit o'zgaruvchisi). Sababi: bu MUHIT
///    xossasi, biznes qarori emas. Staging bazasi odatda prod nusxasidan
///    tiklanadi — kalit bazada tursa prod'ning "qattiq rejim" qiymati
///    staging'ga ham ko'chib o'tardi va sinov foydalanuvchilari bloklanib
///    qolardi. Konfiguratsiyada esa u muhit bilan birga keladi.
///
/// Amalga oshirilishi Infrastructure'da (<c>FinanceSettingsStore</c>) —
/// Application kalit-qiymat jadvali borligini bilmaydi.
/// </summary>
public interface IFinanceSettingsStore
{
    Task<FinanceSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Chegara va qamrovni yozadi. <c>Enforce</c> o'zgartirilmaydi (u konfiguratsiyadan).</summary>
    Task<FinanceSettings> SaveAsync(
        decimal blockThreshold,
        PaymentBlockScope blockScope,
        long? actorId,
        CancellationToken ct = default);
}
