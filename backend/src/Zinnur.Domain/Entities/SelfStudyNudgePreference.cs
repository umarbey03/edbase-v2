using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// O'quvchining eslatmalar bo'yicha tanlovi (2026-11-25): o'chirib qo'ydimi,
/// «keyinroq eslating» dedimi. Har o'quvchiga ko'pi bilan bitta qator.
///
/// ★ NIMA UCHUN O'CHIRISH IMKONI SHART: eslatmani to'xtatib bo'lmasa,
/// o'quvchi butun botni bloklaydi — va shu bilan kurator xabarlari ham,
/// «keyingi dars ochildi» xabari ham yetib bormay qoladi. O'chirish faqat
/// AVTOMATIK eslatmalarga tegishli; kurator va dars xabarlari kelaveradi.
/// </summary>
public class SelfStudyNudgePreference : BaseEntity
{
    public long StudentId { get; set; }

    public User? Student { get; set; }

    /// <summary>O'quvchi avtomatik eslatmalarni o'chirgan payt (<c>null</c> — yoqiq).</summary>
    public DateTimeOffset? OptedOutAt { get; private set; }

    /// <summary>Shu paytgacha o'quvchiga eslatma yuborilmaydi; tugagach bitta «qaytdik» eslatmasi.</summary>
    public DateTimeOffset? SnoozedUntil { get; private set; }

    public bool IsOptedOut => OptedOutAt is not null;

    public static SelfStudyNudgePreference Create(long studentId, DateTimeOffset now) =>
        new() { StudentId = studentId, CreatedAt = now };

    public void OptOut(DateTimeOffset now)
    {
        OptedOutAt ??= now;
        SnoozedUntil = null;
        UpdatedAt = now;
    }

    public void OptIn(DateTimeOffset now)
    {
        OptedOutAt = null;
        UpdatedAt = now;
    }

    public void Snooze(DateTimeOffset until, DateTimeOffset now)
    {
        SnoozedUntil = until;
        UpdatedAt = now;
    }

    public void ClearSnooze(DateTimeOffset now)
    {
        SnoozedUntil = null;
        UpdatedAt = now;
    }
}
