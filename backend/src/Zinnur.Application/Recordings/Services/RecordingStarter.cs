using Microsoft.Extensions.Logging;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// Bitta yozuv URINISHI: Egress'ga murojaat va natijani qatorga yozish.
///
/// ★ NIMA UCHUN ALOHIDA: bu qadamni IKKI joy bajaradi — ustoz tugmasi
/// (<see cref="RecordingService"/>) va watchdog. Ikki nusxa bo'lsa
/// ularning biri urinishlar sanog'ini oshirishni yoki xatoni yozishni
/// unutardi, va nosozlik faqat "watchdog cheksiz uriniyapti" ko'rinishida
/// bilinardi.
///
/// ⚠️ <c>SaveChanges</c> BU YERDA CHAQIRILMAYDI — chaqiruvchi o'z
/// tranzaksiyasida saqlaydi (loyihaning umumiy qoidasi).
/// </summary>
internal static class RecordingStarter
{
    public static async Task<bool> TryAsync(
        ILiveKitEgress egress,
        SessionRecording recording,
        string roomName,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken ct)
    {
        // Urinish OLDIN sanaladi: Egress javobi kelmasa ham (timeout,
        // jarayon qulashi) urinish "bo'lmagan" deb qolmasin — aks holda
        // watchdog cheksiz qayta urardi.
        recording.BeginAttempt(now);

        var result = await egress
            .StartRoomRecordingAsync(new EgressStartRequest(roomName, recording.ObjectKey), ct)
            .ConfigureAwait(false);

        if (result.Succeeded && !string.IsNullOrWhiteSpace(result.EgressId))
        {
            recording.MarkStarting(result.EgressId, now);
            RecordingLog.Started(logger, recording.Id, recording.SessionId, result.EgressId);

            return true;
        }

        var reason = string.IsNullOrWhiteSpace(result.Error)
            ? "Yozuv xizmati javob bermadi."
            : result.Error;

        // Holat `Requested` bo'lib QOLADI — bu yakuniy xato emas, watchdog
        // qayta uradi (chegara: `RecordingWatchdogSettings.MaxAttempts`).
        recording.RecordAttemptError(reason, now);

        RecordingLog.StartFailed(
            logger, recording.Id, recording.SessionId, recording.Attempts, reason);

        return false;
    }
}
