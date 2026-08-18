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
    /// Kechikish jarimasi. Summa = kechikkan daqiqa × tarif.
    /// </summary>
    public static Penalty ForLateStart(
        long userId,
        long sessionId,
        int lateMinutes,
        decimal perMinute,
        DateTimeOffset occurredAt,
        DateOnly periodStart)
    {
        if (lateMinutes <= 0)
            throw new DomainException("Kechikish daqiqasi musbat bo'lishi kerak.");

        if (perMinute <= 0)
            throw new DomainException("Kechikish tarifi belgilanmagan.");

        return new Penalty
        {
            UserId = userId,
            SessionId = sessionId,
            Kind = PenaltyKind.LateStart,
            LateMinutes = lateMinutes,
            Amount = lateMinutes * perMinute,
            Reason = $"Dars {lateMinutes} daqiqa kech boshlandi.",
            OccurredAt = occurredAt,
            PeriodStart = periodStart,
        };
    }

    /// <summary>O'tilmagan dars — QAT'IY summa (sabab sozlama izohida).</summary>
    public static Penalty ForMissedLesson(
        long userId,
        long sessionId,
        decimal amount,
        DateTimeOffset occurredAt,
        DateOnly periodStart)
    {
        if (amount <= 0)
            throw new DomainException("O'tilmagan dars jarimasi belgilanmagan.");

        return new Penalty
        {
            UserId = userId,
            SessionId = sessionId,
            Kind = PenaltyKind.MissedLesson,
            Amount = amount,
            Reason = "Dars vaqti o'tdi, lekin dars boshlanmadi.",
            OccurredAt = occurredAt,
            PeriodStart = periodStart,
        };
    }

    /// <summary>Qo'lda kiritilgan jarima.</summary>
    public static Penalty Manual(
        long userId,
        decimal amount,
        string? reason,
        long createdById,
        DateTimeOffset occurredAt,
        DateOnly periodStart)
    {
        if (amount <= 0)
            throw new DomainException("Jarima summasi musbat bo'lishi kerak.");

        var trimmed = (reason ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            throw new DomainException("Jarima sababini kiriting.");

        return new Penalty
        {
            UserId = userId,
            Kind = PenaltyKind.Manual,
            Amount = amount,
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
