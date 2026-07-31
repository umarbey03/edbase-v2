using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// <see cref="IRecordingWebhookHandler"/> ning amalga oshirilishi.
///
/// ── OQIM ────────────────────────────────────────────────────────────────
///   1) JSON o'qiladi (<see cref="LiveKitWebhookParser"/>);
///   2) yozuvga aloqasi bo'lmagan hodisa DARHOL chetlanadi — takror
///      jurnaliga ham yozilmaydi (jadval bekorga o'smasin: bitta darsda
///      `participant_joined` yuzlab marta keladi);
///   3) takror jurnali (<see cref="ILiveKitWebhookLog"/>);
///   4) <c>EgressId</c> bo'yicha qator topiladi;
///   5) holat Domain metodlari orqali o'zgartiriladi;
///   6) BITTA <c>SaveChanges</c> — jurnal yozuvi va holat o'zgarishi AYNI
///      tranzaksiyada. Aks holda "takror deb belgilandi, lekin holat
///      o'zgarmadi" degan yo'qotish mumkin bo'lardi.
///
/// ── NIMA UCHUN `EgressId` BO'YICHA, XONA NOMI BO'YICHA EMAS ─────────────
///
/// Bitta xonada ketma-ket bir necha yozuv urinishi bo'lishi mumkin
/// (birinchisi yiqilib, watchdog qaytadan boshlagan). Xona nomi bo'yicha
/// qidirsak, kech kelgan ESKI hodisa YANGI yozuvni "tugadi" deb belgilab
/// qo'yardi. <c>EgressId</c> esa aynan bitta urinishga tegishli.
/// </summary>
public sealed class RecordingWebhookHandler(
    IApplicationDbContext db,
    ILiveKitWebhookLog log,
    TimeProvider clock,
    ILogger<RecordingWebhookHandler> logger) : IRecordingWebhookHandler
{
    /// <inheritdoc />
    public async Task<RecordingWebhookOutcome> HandleAsync(
        ReadOnlyMemory<byte> body, CancellationToken ct = default)
    {
        var evt = LiveKitWebhookParser.Parse(body.Span);

        if (evt is null)
        {
            RecordingLog.WebhookMalformed(logger, body.Length);
            return RecordingWebhookOutcome.Malformed;
        }

        // Bizga faqat egress hodisalari kerak. `room_started`,
        // `room_finished`, `participant_*` — chetlanadi. Xona yopilganda
        // Egress O'ZI to'xtaydi, uni "qo'lda" to'xtatish esa watchdog'ning
        // ishi (webhook ichida tashqi chaqiruv qilinmaydi).
        if (string.IsNullOrWhiteSpace(evt.EgressId))
            return RecordingWebhookOutcome.Ignored;

        if (!await log.TryBeginAsync(evt.EventId, ct).ConfigureAwait(false))
        {
            RecordingLog.WebhookDuplicate(logger, evt.EventName, evt.EventId);
            return RecordingWebhookOutcome.Duplicate;
        }

        var recording = await db.SessionRecordings
            .AsTracking()
            .FirstOrDefaultAsync(r => r.EgressId == evt.EgressId, ct)
            .ConfigureAwait(false);

        if (recording is null)
        {
            // Bizda yo'q egress. Ikki sabab bo'lishi mumkin: (a) yozuv
            // boshqa muhitdan boshlangan (bitta LiveKit'ni dev va staging
            // baham ko'rsa), (b) qatorimiz o'chirilgan. Ikkalasi ham XATO
            // EMAS — lekin jurnal yozuvi SAQLANADI: takror hodisa yana
            // bazani bekorga qidirmasin.
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            RecordingLog.WebhookUnknownEgress(logger, evt.EventName, evt.EgressId!);
            return RecordingWebhookOutcome.Unknown;
        }

        var now = clock.GetUtcNow();
        var outcome = Apply(recording, evt, now);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        RecordingLog.WebhookApplied(
            logger, evt.EventName, evt.EgressId!, recording.Id, recording.Status.ToString());

        return outcome;
    }

    /// <summary>
    /// Hodisani holat o'zgarishiga aylantiradi.
    ///
    /// ★ QAROR AVVAL <c>status</c> MAYDONIGA, KEYIN hodisa NOMIGA qaraydi.
    /// Sabab: LiveKit ayni holatni turli nomlar bilan yuborishi mumkin
    /// (<c>egress_updated</c> ichida ham <c>EGRESS_COMPLETE</c> kelishi
    /// mumkin). Holat maydoni esa protokolning O'ZIDA aniqlangan qiymat —
    /// unga tayanish barqarorroq.
    /// </summary>
    private static RecordingWebhookOutcome Apply(
        SessionRecording recording, LiveKitWebhookEventDto evt, DateTimeOffset now)
    {
        var status = evt.EgressStatus?.Trim().ToUpperInvariant();

        return status switch
        {
            EgressActive => Activate(recording, evt, now),
            EgressComplete => Complete(recording, evt, now),
            EgressFailed or EgressAborted or EgressLimitReached => Fail(recording, evt, now, status),

            // `EGRESS_STARTING` / `EGRESS_ENDING` — oraliq holatlar,
            // ularga tegilmaydi: qator allaqachon `Starting`/`Active` va
            // orqaga qaytarish faqat chalkashlik bo'lardi.
            EgressStarting or EgressEnding => RecordingWebhookOutcome.Ignored,

            // Holat maydoni umuman bo'lmasa — hodisa nomiga qaraymiz.
            _ => ApplyByName(recording, evt, now),
        };
    }

    private static RecordingWebhookOutcome ApplyByName(
        SessionRecording recording, LiveKitWebhookEventDto evt, DateTimeOffset now) =>
        evt.EventName switch
        {
            "egress_started" => Activate(recording, evt, now),

            // Nomi "tugadi", lekin holat noma'lum. Fayl kaliti kelgan
            // bo'lsa — muvaffaqiyat, aks holda xato: "fayl yo'q, lekin
            // tugadi" degan holatni MUVAFFAQIYAT deb belgilash eng yomon
            // variant bo'lardi (o'quvchi bosgan havola 404 berardi).
            "egress_ended" => string.IsNullOrWhiteSpace(evt.ObjectKey)
                ? Fail(recording, evt, now, "egress_ended")
                : Complete(recording, evt, now),

            _ => RecordingWebhookOutcome.Ignored,
        };

    private static RecordingWebhookOutcome Activate(
        SessionRecording recording, LiveKitWebhookEventDto evt, DateTimeOffset now)
    {
        recording.MarkActive(evt.StartedAt ?? now, now);
        return RecordingWebhookOutcome.Started;
    }

    private static RecordingWebhookOutcome Complete(
        SessionRecording recording, LiveKitWebhookEventDto evt, DateTimeOffset now)
    {
        recording.MarkCompleted(
            evt.ObjectKey, evt.FileSizeBytes, evt.DurationSeconds, evt.EndedAt ?? now, now);

        return RecordingWebhookOutcome.Completed;
    }

    private static RecordingWebhookOutcome Fail(
        SessionRecording recording, LiveKitWebhookEventDto evt, DateTimeOffset now, string? source)
    {
        var reason = string.IsNullOrWhiteSpace(evt.Error)
            ? $"LiveKit yozuvni yakunlay olmadi ({source ?? evt.EventName})."
            : evt.Error;

        recording.MarkFailed(reason, now);
        return RecordingWebhookOutcome.Failed;
    }

    // LiveKit `EgressStatus` enum nomlari (protojson ularni SATR yuboradi).
    private const string EgressStarting = "EGRESS_STARTING";
    private const string EgressActive = "EGRESS_ACTIVE";
    private const string EgressEnding = "EGRESS_ENDING";
    private const string EgressComplete = "EGRESS_COMPLETE";
    private const string EgressFailed = "EGRESS_FAILED";
    private const string EgressAborted = "EGRESS_ABORTED";
    private const string EgressLimitReached = "EGRESS_LIMIT_REACHED";
}
