using Microsoft.Extensions.Logging;

namespace Zinnur.Application.Recordings;

/// <summary>
/// Yozuv modulining manba-generatsiyali log metodlari.
///
/// ★ NIMA UCHUN ALOHIDA SINF: oddiy <c>logger.LogInformation("…", a, b)</c>
/// har chaqiruvda <c>object[]</c> ajratadi va qiymatlarni bokslaydi
/// (CA1848). Bundan tashqari modulning barcha xabarlari va ularning
/// EventId'lari BIR JOYDA turadi — <c>ApiLog</c> ga tegilmaydi.
///
/// EventId makoni: <c>6500–6549</c> (yozuv), <c>6550–6599</c> (watchdog).
/// </summary>
internal static partial class RecordingLog
{
    // ================================================================= webhook

    [LoggerMessage(
        EventId = 6500,
        Level = LogLevel.Warning,
        Message = "LiveKit webhook: tanani o'qib bo'lmadi (uzunlik={Length}).")]
    internal static partial void WebhookMalformed(ILogger logger, int length);

    [LoggerMessage(
        EventId = 6501,
        Level = LogLevel.Debug,
        Message = "LiveKit webhook: takroriy hodisa e'tiborsiz qoldirildi. "
                  + "event={EventName} id={EventId}")]
    internal static partial void WebhookDuplicate(ILogger logger, string eventName, string eventId);

    [LoggerMessage(
        EventId = 6502,
        Level = LogLevel.Warning,
        Message = "LiveKit webhook: noma'lum egress. event={EventName} egress={EgressId}")]
    internal static partial void WebhookUnknownEgress(ILogger logger, string eventName, string egressId);

    [LoggerMessage(
        EventId = 6503,
        Level = LogLevel.Information,
        Message = "Dars yozuvi yangilandi: event={EventName} egress={EgressId} "
                  + "yozuv={RecordingId} holat={Status}")]
    internal static partial void WebhookApplied(
        ILogger logger, string eventName, string egressId, long recordingId, string status);

    // ================================================================= boshlash/to'xtatish

    [LoggerMessage(
        EventId = 6510,
        Level = LogLevel.Information,
        Message = "Dars yozuvi boshlandi: yozuv={RecordingId} dars={SessionId} egress={EgressId}")]
    internal static partial void Started(
        ILogger logger, long recordingId, long sessionId, string egressId);

    [LoggerMessage(
        EventId = 6511,
        Level = LogLevel.Error,
        Message = "Dars yozuvini boshlab bo'lmadi: yozuv={RecordingId} dars={SessionId} "
                  + "urinish={Attempts} sabab={Reason}")]
    internal static partial void StartFailed(
        ILogger logger, long recordingId, long sessionId, int attempts, string reason);

    [LoggerMessage(
        EventId = 6512,
        Level = LogLevel.Information,
        Message = "Dars yozuvini to'xtatish so'raldi: yozuv={RecordingId} egress={EgressId} "
                  + "qabul={Accepted}")]
    internal static partial void StopRequested(
        ILogger logger, long recordingId, string egressId, bool accepted);

    // ================================================================= avtomatik yozuv

    [LoggerMessage(
        EventId = 6520,
        Level = LogLevel.Information,
        Message = "Avtomatik yozuv navbatga qo'yildi (guruh sozlamasi). dars={SessionId}")]
    internal static partial void AutoQueued(ILogger logger, long sessionId);

    /// <summary>
    /// ★ NIMA UCHUN <c>Warning</c>, <c>Debug</c> EMAS: guruhda yozuv
    /// YOQILGAN, lekin dars YOZILMAYDI — ya'ni o'quv bo'limi kutgan narsa
    /// bo'lmaydi va buni faqat dars tugagach, yozuv yo'qligidan bilishardi.
    /// Bu logdagi yagona ogohlantirish shu holatga oid.
    /// </summary>
    [LoggerMessage(
        EventId = 6521,
        Level = LogLevel.Warning,
        Message = "Avtomatik yozuv o'tkazib yuborildi: LiveKit yoki ombor sozlanmagan. "
                  + "dars={SessionId}")]
    internal static partial void AutoSkippedNotConfigured(ILogger logger, long sessionId);

    // ================================================================= watchdog

    [LoggerMessage(
        EventId = 6550,
        Level = LogLevel.Warning,
        Message = "Watchdog: yozuv boshlanmagan, qayta urinamiz. "
                  + "yozuv={RecordingId} urinish={Attempts}")]
    internal static partial void WatchdogRetry(ILogger logger, long recordingId, int attempts);

    [LoggerMessage(
        EventId = 6551,
        Level = LogLevel.Error,
        Message = "Watchdog: yozuv yakuniy XATO deb belgilandi. "
                  + "yozuv={RecordingId} sabab={Reason}")]
    internal static partial void WatchdogGaveUp(ILogger logger, long recordingId, string reason);

    [LoggerMessage(
        EventId = 6552,
        Level = LogLevel.Information,
        Message = "Watchdog: fayl ombordan topildi, yozuv yakunlandi. "
                  + "yozuv={RecordingId} kalit={ObjectKey} hajm={SizeBytes}")]
    internal static partial void WatchdogRecovered(
        ILogger logger, long recordingId, string objectKey, long? sizeBytes);

    [LoggerMessage(
        EventId = 6553,
        Level = LogLevel.Warning,
        Message = "Watchdog: ombor javob bermadi, yozuv keyingi yurishga qoldirildi. "
                  + "yozuv={RecordingId}")]
    internal static partial void WatchdogStorageUnavailable(
        ILogger logger, Exception exception, long recordingId);

    /// <summary>
    /// ★ `Debug` — bu NOSOZLIK EMAS, ODATIY KUTISH. Dars boshlangandan
    /// keyin xona bir necha soniya bo'sh turishi normal holat. `Warning`
    /// bo'lsa har bir dars boshida log axlat bilan to'lardi va haqiqiy
    /// ogohlantirishlar ko'rinmay qolardi.
    /// </summary>
    [LoggerMessage(
        EventId = 6554,
        Level = LogLevel.Debug,
        Message = "Watchdog: xona hali bo'sh, yozuv kutilmoqda. yozuv={RecordingId}")]
    internal static partial void WatchdogWaitingForParticipants(
        ILogger logger, long recordingId);

    /// <summary>
    /// ⚠️ Presence o'qilmadi — yozuv BARIBIR boshlanadi (fail-open).
    /// Sabab: `RecordingWatchdogJob.RoomHasParticipantsAsync`.
    /// </summary>
    [LoggerMessage(
        EventId = 6555,
        Level = LogLevel.Warning,
        Message = "Watchdog: ishtirokchilar ro'yxati o'qilmadi, yozuv baribir "
                  + "boshlanadi. yozuv={RecordingId}")]
    internal static partial void WatchdogPresenceUnavailable(
        ILogger logger, Exception exception, long recordingId);
}
