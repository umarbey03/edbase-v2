using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// O'quvchining kurs darsi bo'yicha progressi — sur'at nazorati (gating) uchun.
/// <c>(StudentId, ModuleLessonId)</c> — UNIKAL.
///
/// Vazifa va test holati bu yerda TAKRORLANMAYDI — ular
/// <see cref="Submission"/> va <see cref="TestAttempt"/> dan hisoblanadi.
/// Denormalizatsiya qilinsa ikki manba bir-biriga mos kelmay qolardi.
/// </summary>
public class LessonProgress : BaseEntity
{
    /// <summary>Video "ko'rilgan" hisoblanishi uchun kerakli ulush.</summary>
    public const double WatchedThreshold = 0.9;

    public long StudentId { get; set; }

    public User? Student { get; set; }

    public long ModuleLessonId { get; set; }

    public ModuleLesson? ModuleLesson { get; set; }

    /// <summary>
    /// Video 90% ko'rilgan payt. <c>null</c> — hali ko'rilmagan.
    ///
    /// Bir marta yozilgach O'ZGARMAYDI — o'quvchi videoni qayta ko'rsa
    /// progress orqaga ketmasligi kerak.
    /// </summary>
    public DateTimeOffset? VideoWatchedAt { get; set; }

    /// <summary>
    /// O'quv bo'limi qo'lda ochib bergan (gating istisnosi).
    /// Kasallik, kursga kech qo'shilish kabi holatlar uchun.
    /// </summary>
    public bool UnlockedOverride { get; set; }

    /// <summary>Istisno sababi — keyinchalik "nega ochilgan?" degan savolga javob.</summary>
    public string? OverrideReason { get; set; }

    public long? OverrideById { get; set; }

    // ---------------------------------------------------------------- hisoblanuvchi

    public bool IsVideoWatched => VideoWatchedAt is not null;

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// Videoni ko'rilgan deb belgilaydi (idempotent — birinchi payt saqlanadi).
    /// </summary>
    public void MarkVideoWatched(DateTimeOffset now)
    {
        VideoWatchedAt ??= now;
        UpdatedAt = now;
    }

    /// <summary>Qo'lda ochish / yopish.</summary>
    public void SetOverride(bool unlocked, string? reason, long actorId, DateTimeOffset now)
    {
        UnlockedOverride = unlocked;
        OverrideReason = unlocked && !string.IsNullOrWhiteSpace(reason) ? reason.Trim() : null;
        OverrideById = unlocked ? actorId : null;
        UpdatedAt = now;
    }
}
