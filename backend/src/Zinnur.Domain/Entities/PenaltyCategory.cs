using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// JARIMA KATEGORIYASI — TARIFLAR KATALOGI (2026-08-18)
/// ════════════════════════════════════════════════════════════════════════
///
/// Loyiha egasi talabi: jarima turlari va ularning summalari SOZLAMALARDAN
/// boshqarilsin, ya'ni yangi jarima turini qo'shish uchun dastur qayta
/// yozilmasin ("dinamik ma'lumotlar saqlash").
///
/// ★ NEGA SOZLAMALAR JADVALIDA EMAS: `AppSettings` — kalit/qiymat, ya'ni
/// BITTA qiymat. Kategoriyada esa to'rtta bog'liq maydon bor (nomi, summa,
/// songa qarab hisoblanadimi, birlik nomi) va ular RO'YXAT bo'lib o'sadi.
/// Kalit/qiymatga siqilsa `penalty.cat.7.unit_label` ko'rinishidagi soxta
/// kalitlar paydo bo'lardi va "shu kategoriya nechta jarimada ishlatilgan?"
/// degan savolga javob berib bo'lmasdi.
///
/// ★ SUMMA KO'CHIRIB OLINADI: <c>Penalty.Amount</c> jarima yaratilganda
/// hisoblanib SAQLANADI. Bu yerdagi tarif keyin o'zgarsa, eski jarimalar
/// o'zgarmaydi (<see cref="Penalty"/> dagi AYNI mulohaza) — shuning uchun
/// kategoriyani tahrirlash tarixni buzmaydi.
/// </summary>
public class PenaltyCategory : BaseEntity
{
    public const int MaxLabelLength = 100;
    public const int MaxUnitLength = 30;

    /// <summary>Avtomatik kechikish jarimasi tarifi (daqiqasiga).</summary>
    public const string LateStartKey = "late_start";

    /// <summary>Avtomatik "dars o'tilmadi" jarimasi tarifi.</summary>
    public const string MissedLessonKey = "missed_lesson";

    /// <summary>Ko'rinadigan nomi — "Darsga kechikish", "Kiyim-bosh qoidasi".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Tarif (so'm). <see cref="PerUnit"/> bo'lsa — BIR BIRLIK uchun,
    /// aks holda qat'iy summa.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>Summa = <see cref="Amount"/> × miqdor (masalan daqiqa soni).</summary>
    public bool PerUnit { get; set; }

    /// <summary>Birlik nomi — "daqiqa", "dars", "kun". <see cref="PerUnit"/> da majburiy.</summary>
    public string? UnitLabel { get; set; }

    /// <summary>
    /// O'chirilgan kategoriya yangi jarimada tanlanmaydi, lekin ESKI
    /// jarimalarda nomi ko'rinib turaveradi.
    ///
    /// ★ NEGA O'CHIRISH EMAS, ARXIVLASH: jarima kategoriyaga havola
    /// qiladi. Qator o'chirilsa, o'tgan oyning tasdiqlangan jarimasi
    /// "nomsiz" bo'lib qolardi va oylik hisoboti buzilardi.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// TIZIM kategoriyasi belgisi (<see cref="LateStartKey"/> yoki
    /// <see cref="MissedLessonKey"/>). Bo'sh — oddiy, qo'lda qo'shilgan
    /// kategoriya.
    ///
    /// ★ NIMA UCHUN KERAK: avtomatik jarima kodi tarifni AYNAN shu
    /// kalit orqali topadi. Nomi bo'yicha izlansa, administrator
    /// kategoriyani "Kechikish" deb qayta nomlagani zahoti avtomatik
    /// jarima jimgina ishlamay qolardi.
    ///
    /// Tizim kategoriyasini o'chirib ham, arxivlab ham bo'lmaydi —
    /// faqat summasi tahrirlanadi (`0` = jarima yozilmaydi).
    /// </summary>
    public string? SystemKey { get; set; }

    /// <summary>Tizim kategoriyasimi — o'chirish/arxivlash taqiqlanadi.</summary>
    public bool IsSystem => !string.IsNullOrEmpty(SystemKey);

    /// <summary>
    /// Berilgan miqdor uchun summani hisoblaydi.
    ///
    /// ★ HISOB DOMENDA: bu qoida ikki joydan chaqiriladi (qo'lda kiritish
    /// va avtomatik aniqlash). Servisda takrorlansa, biri o'zgarib
    /// ikkinchisi qolib ketardi.
    /// </summary>
    /// <param name="quantity">Miqdor — faqat <see cref="PerUnit"/> da talab qilinadi.</param>
    public decimal ComputeAmount(decimal? quantity)
    {
        if (!PerUnit) return Amount;

        if (quantity is not { } qty || qty <= 0)
            throw new DomainException($"\"{Label}\" uchun necha {UnitLabel ?? "dona"} ekanini kiriting.");

        return Amount * qty;
    }

    /// <summary>Nom/summa/birlikni tekshiradi va normallashtiradi.</summary>
    public void Apply(string label, decimal amount, bool perUnit, string? unitLabel)
    {
        var trimmedLabel = (label ?? string.Empty).Trim();

        if (trimmedLabel.Length == 0)
            throw new DomainException("Kategoriya nomini kiriting.");

        if (trimmedLabel.Length > MaxLabelLength)
            trimmedLabel = trimmedLabel[..MaxLabelLength];

        // ★ `0` RUXSAT ETILADI (referens paneldan FARQLI): tizim
        //   kategoriyasida `0` — "bu jarima hozircha yozilmasin" degan
        //   O'CHIRGICH. Uni taqiqlasak, administrator avtomatik jarimani
        //   vaqtincha to'xtata olmasdi.
        if (amount < 0)
            throw new DomainException("Jarima summasi manfiy bo'lishi mumkin emas.");

        var trimmedUnit = (unitLabel ?? string.Empty).Trim();

        if (perUnit && trimmedUnit.Length == 0)
            throw new DomainException("Birlik nomini kiriting (masalan: daqiqa).");

        if (trimmedUnit.Length > MaxUnitLength)
            trimmedUnit = trimmedUnit[..MaxUnitLength];

        Label = trimmedLabel;
        Amount = amount;
        PerUnit = perUnit;
        UnitLabel = perUnit ? trimmedUnit : null;
    }
}
