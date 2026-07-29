namespace Zinnur.WebApi;

/// <summary>
/// Manba-generatsiyali (source-generated) log metodlari.
///
/// NIMA UCHUN ODDIY <c>logger.LogInformation("...", args)</c> EMAS:
/// oddiy chaqiruv har safar `params object[]` massivini ajratadi va qiymat
/// tiplarini box qiladi — hatto log darajasi o'chiq bo'lsa ham. Jonli darsda
/// hub sekundiga o'nlab marta log yozadi (200 kishi kirib-chiqadi), shuning
/// uchun bu sezilarli axlat (GC) hosil qiladi.
///
/// <c>[LoggerMessage]</c> kompilyatsiya vaqtida ajratmasiz (allocation-free)
/// kod generatsiya qiladi va darajani oldindan tekshiradi.
/// Analizator ham buni talab qiladi (CA1848).
/// </summary>
internal static partial class ApiLog
{
    // ---------------------------------------------------------------- startup

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "ZIN-NUR API ishga tushdi. Muhit: {Environment}")]
    public static partial void ApiStarted(ILogger logger, string environment);

    // ---------------------------------------------------------------- hub

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Darsga qo'shildi: session={SessionId} user={UserId} jami={Count}")]
    public static partial void SessionJoined(ILogger logger, long sessionId, long userId, int count);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Uzilishda tozalash xatosi: session={SessionId}")]
    public static partial void DisconnectCleanupFailed(ILogger logger, Exception exception, long sessionId);

    // ---------------------------------------------------------------- chat writer

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Debug,
        Message = "Chat paketi yozildi: {Count} xabar")]
    public static partial void ChatBatchWritten(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Error,
        Message = "Chat paketini yozishda xato ({Count} xabar)")]
    public static partial void ChatBatchFailed(ILogger logger, Exception exception, int count);

    // ---------------------------------------------------------------- xatolar

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Error,
        Message = "Ishlov berilmagan xato. traceId={TraceId}")]
    public static partial void UnhandledError(ILogger logger, Exception exception, string traceId);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "So'rov rad etildi ({Status}): {Reason}. traceId={TraceId}")]
    public static partial void RequestRejected(ILogger logger, int status, string reason, string traceId);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Warning,
        Message = "Javob allaqachon boshlangan — xato javobi yuborilmadi. traceId={TraceId}")]
    public static partial void ResponseAlreadyStarted(ILogger logger, string traceId);
}
