using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// USTOZ/KURATOR JARIMASI (2026-08-18)
/// ════════════════════════════════════════════════════════════════════════
///
/// Loyiha egasi talabi: kech boshlangan va umuman o'tilmagan darslar uchun
/// jarima; summa daqiqasiga tarif bilan; oylikdan FAQAT tasdiqlangandan
/// keyin ushlanadi.
///
/// ★ OYLIKDAN ALOHIDA ENTITY: <c>PayrollAdjustment</c> allaqachon bor va
/// jarima oxir-oqibat AYNAN unga aylanadi. Lekin ular bir xil narsa EMAS:
///   • `PayrollAdjustment` — pul harakati, davr yopilgach o'zgarmaydi;
///   • `Penalty` — HODISA (qaysi dars, necha daqiqa kech, kim aniqladi)
///     va u tasdiqlanishi yoki BEKOR QILINISHI mumkin.
/// Ikkisini bitta jadvalga siqish "nega bu ushlanma bo'ldi?" degan
/// savolga javobni yo'qotardi va bekor qilingan jarima oylikda iz
/// qoldirardi.
///
/// ★ HISOB VAQTIDA MUZLATILADI: <see cref="Amount"/> jarima yaratilganda
/// hisoblanadi va SAQLANADI. Tarif keyin o'zgarsa, eski jarimalar
/// o'zgarmaydi — aks holda o'tgan oyning tasdiqlangan jarimasi bugungi
/// tarif bo'yicha "qayta hisoblanib" ketardi (`SessionPayout` dagi AYNI
/// mulohaza).
/// </summary>
public class Penalty : BaseEntity
{
    public const int MaxReasonLength = 500;

    /// <summary>Jarima KIMGA yozilgan (ustoz yoki kurator).</summary>
    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Qaysi dars uchun. Qo'lda kiritilgan jarimada <c>null</c>.
    ///
    /// ★ AVTOMATIK JARIMADA TAKRORGA QARSHI KALIT: bitta dars uchun bitta
    /// turdagi jarima FAQAT BIR MARTA yoziladi (unikal indeks
    /// <c>(SessionId, Kind)</c>) — fon vazifasi qayta yursa ham ikkinchi
    /// jarima paydo bo'lmaydi.
    /// </summary>
    public long? SessionId { get; set; }

    public LiveSession? Session { get; set; }

    public PenaltyKind Kind { get; set; }

    /// <summary>
    /// Qaysi tarif bo'yicha yozilgan (<see cref="PenaltyCategory"/>).
    ///
    /// ★ NEGA IXTIYORIY: kategoriyalar tizimidan OLDIN yozilgan jarimalarda
    /// bo'sh, va qo'lda kiritishda administrator kategoriyasiz, erkin
    /// summa bilan ham jarima yoza oladi (masalan takrorlanmaydigan
    /// bir martalik holat).
    /// </summary>
    public long? CategoryId { get; set; }

    public PenaltyCategory? Category { get; set; }

    /// <summary>
    /// Songa qarab hisoblanadigan kategoriyada — necha birlik
    /// (masalan 15 daqiqa). Qat'iy summali kategoriyada <c>null</c>.
    ///
    /// ★ <see cref="LateMinutes"/> DAN FARQI: u — kechikish jarimasining
    /// TIPLANGAN isboti (butun daqiqa, hisobotda jamlanadi); bu esa
    /// HAR QANDAY birlik uchun umumiy maydon. Kechikishda ikkalasi ham
    /// to'ldiriladi — jadvalda "(15 daqiqa)" deb ko'rsatish uchun
    /// kategoriyaning birlik nomi bilan birga aynan shu maydon o'qiladi.
    /// </summary>
    public decimal? Quantity { get; set; }

    public PenaltyStatus Status { get; set; } = PenaltyStatus.Pending;

    /// <summary>
    /// Necha daqiqa kechikkan — faqat <see cref="PenaltyKind.LateStart"/> da.
    /// Isbot uchun saqlanadi: ustoz bilan bahsda "qancha" degan savolga
    /// javob shu yerda.
    /// </summary>
    public int? LateMinutes { get; set; }

    /// <summary>Ushlab qolinadigan summa (musbat son, so'm).</summary>
    public decimal Amount { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>Hodisa qachon sodir bo'lgan (dars vaqti yoki qo'lda kiritilgan sana).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Qaysi oylik davriga tegishli — oyning 1-kuni (mahalliy).</summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>Qo'lda kiritilgan bo'lsa — kim kiritgan. Avtomatikda <c>null</c>.</summary>
    public long? CreatedById { get; set; }

    public User? CreatedBy { get; set; }

    public long? ReviewedById { get; set; }

    public User? ReviewedBy { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>
    /// Tasdiqlangach yaratilgan oylik tuzatmasi. <c>null</c> — hali
    /// tasdiqlanmagan yoki bekor qilingan.
    /// </summary>
    public long? PayrollAdjustmentId { get; set; }

    public PayrollAdjustment? PayrollAdjustment { get; set; }

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// Kechikish jarimasi. Summa = kechikkan daqiqa × kategoriya tarifi.
    /// </summary>
    /// <param name="category">
    /// <see cref="PenaltyCategory.LateStartKey"/> tizim kategoriyasi —
    /// tarif AYNAN shundan olinadi (sozlamadan emas).
    /// </param>
    public static Penalty ForLateStart(
        long userId,
        long sessionId,
        int lateMinutes,
        PenaltyCategory category,
        DateTimeOffset occurredAt,
        DateOnly periodStart)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (lateMinutes <= 0)
            throw new DomainException("Kechikish daqiqasi musbat bo'lishi kerak.");

        if (category.Amount <= 0)
            throw new DomainException("Kechikish tarifi belgilanmagan.");

        return new Penalty
        {
            UserId = userId,
            SessionId = sessionId,
            Kind = PenaltyKind.LateStart,
            CategoryId = category.Id,
            LateMinutes = lateMinutes,
            Quantity = lateMinutes,
            Amount = category.ComputeAmount(lateMinutes),
            Reason = $"Dars {lateMinutes} daqiqa kech boshlandi.",
            OccurredAt = occurredAt,
            PeriodStart = periodStart,
        };
    }

    /// <summary>O'tilmagan dars — QAT'IY summa (sabab kategoriya izohida).</summary>
    public static Penalty ForMissedLesson(
        long userId,
        long sessionId,
        PenaltyCategory category,
        DateTimeOffset occurredAt,
        DateOnly periodStart)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (category.Amount <= 0)
            throw new DomainException("O'tilmagan dars jarimasi belgilanmagan.");

        return new Penalty
        {
            UserId = userId,
            SessionId = sessionId,
            Kind = PenaltyKind.MissedLesson,
            CategoryId = category.Id,
            Amount = category.ComputeAmount(null),
            Reason = "Dars vaqti o'tdi, lekin dars boshlanmadi.",
            OccurredAt = occurredAt,
            PeriodStart = periodStart,
        };
    }

    /// <summary>
    /// Qo'lda kiritilgan jarima.
    ///
    /// ★ SUMMA IKKI YO'L BILAN: kategoriya berilsa — TARIFDAN hisoblanadi
    /// (operator raqamni o'zi yozmaydi, ya'ni bir xil qoidabuzarlik bir
    /// xil pul); kategoriya bo'lmasa — <paramref name="amount"/> to'g'ridan
    /// olinadi (takrorlanmaydigan bir martalik holatlar uchun).
    /// </summary>
    public static Penalty Manual(
        long userId,
        PenaltyCategory? category,
        decimal? quantity,
        decimal? amount,
        string? reason,
        long createdById,
        DateTimeOffset occurredAt,
        DateOnly periodStart)
    {
        decimal finalAmount;

        if (category is not null)
        {
            if (category.Amount <= 0)
                throw new DomainException($"\"{category.Label}\" kategoriyasining tarifi belgilanmagan.");

            finalAmount = category.ComputeAmount(quantity);
        }
        else
        {
            finalAmount = amount ?? 0m;
        }

        if (finalAmount <= 0)
            throw new DomainException("Jarima summasi musbat bo'lishi kerak.");

        var trimmed = (reason ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            throw new DomainException("Jarima sababini kiriting.");

        return new Penalty
        {
            UserId = userId,
            Kind = PenaltyKind.Manual,
            CategoryId = category?.Id,
            Quantity = category is { PerUnit: true } ? quantity : null,
            Amount = finalAmount,
            Reason = trimmed.Length > MaxReasonLength ? trimmed[..MaxReasonLength] : trimmed,
            CreatedById = createdById,
            OccurredAt = occurredAt,
            PeriodStart = periodStart,
        };
    }

    /// <summary>Tasdiqlash — shundan keyin oylikka tushadi.</summary>
    public void Approve(long reviewerId, DateTimeOffset now)
    {
        EnsurePending();

        Status = PenaltyStatus.Approved;
        ReviewedById = reviewerId;
        ReviewedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Bekor qilish (uzrli sabab yoki xato yozuv).</summary>
    public void Cancel(long reviewerId, string? reason, DateTimeOffset now)
    {
        EnsurePending();

        Status = PenaltyStatus.Cancelled;
        ReviewedById = reviewerId;
        ReviewedAt = now;
        UpdatedAt = now;

        var note = (reason ?? string.Empty).Trim();

        if (note.Length > 0)
        {
            var combined = $"{Reason} · Bekor qilindi: {note}";
            Reason = combined.Length > MaxReasonLength ? combined[..MaxReasonLength] : combined;
        }
    }

    private void EnsurePending()
    {
        if (Status != PenaltyStatus.Pending)
            throw new DomainException("Bu jarima allaqachon ko'rib chiqilgan.");
    }
}
