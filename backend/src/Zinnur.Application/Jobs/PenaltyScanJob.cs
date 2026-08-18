using Zinnur.Application.Penalties.Services;

namespace Zinnur.Application.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// O'TILMAGAN DARSLAR UCHUN JARIMA (2026-08-18)
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN FON VAZIFASI KERAK: kechikish HODISA — dars boshlangan
/// paytda aniq bo'ladi va o'sha yerda yoziladi
/// (<c>LiveSessionService.StartAsync</c>). "Dars umuman o'tilmadi" esa
/// hodisa EMAS: u hech kim hech narsa qilmagani uchun, VAQT o'tishi bilan
/// yuzaga keladi. Bunday faktni faqat vaqti-vaqti bilan tekshirish orqali
/// aniqlash mumkin.
///
/// ★ JARIMA DARHOL USHLANMAYDI: yozuv <c>Pending</c> bo'lib tug'iladi va
/// administrator tasdiqlaguncha oylikka TEGMAYDI (sabab
/// <c>PenaltyStatus</c> izohida). Ya'ni bu vazifa hech qachon o'z-o'zidan
/// pul yechmaydi — u faqat "ko'rib chiqing" ro'yxatini to'ldiradi.
///
/// ★ IDEMPOTENT: bitta darsga bitta jarima — baza darajasidagi unikal
/// indeks (<c>UX_Penalties_SessionId_Kind</c>) qayta yozishni to'sadi,
/// so'rovning o'zi ham allaqachon jarimasi borlarni chetlab o'tadi.
///
/// ⚠️ SOZLAMA NOLGA TENG BO'LSA hech narsa qilmaydi — servis buni
/// tekshiradi va darhol chiqadi (jarima summasi belgilanmagan bo'lsa
/// jarima yozishning ma'nosi yo'q).
/// </summary>
public sealed class PenaltyScanJob(
    IPenaltyService penalties,
    PenaltyScanSettings settings) : IScheduledJob
{
    /// <inheritdoc />
    public string Name => "penalty-scan";

    /// <inheritdoc />
    public TimeSpan Interval => settings.Interval;

    /// <inheritdoc />
    public async Task<JobRunResult> RunAsync(CancellationToken ct = default)
    {
        var created = await penalties.ScanMissedLessonsAsync(ct).ConfigureAwait(false);

        return created == 0
            ? JobRunResult.Nothing
            : new JobRunResult(Processed: created, Skipped: 0, Note: "o'tilmagan dars");
    }
}

/// <summary>Jarima skaneri sozlamasi.</summary>
/// <param name="Interval">Ikki yurish orasidagi masofa.</param>
public sealed record PenaltyScanSettings(TimeSpan Interval);
