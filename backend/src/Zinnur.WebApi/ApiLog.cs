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

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Kuzatuv: Sentry={SentryState}, log={LogFormat}, reliz={Release}")]
    public static partial void ObservabilityConfigured(
        ILogger logger,
        string sentryState,
        string logFormat,
        string release);

    // ------------------------------------------------- sinov uchun kirish
    //
    // 🔴 UCHALA SATR HAM `Warning` (yoki undan yuqori) — ATAYLAB.
    // Bular autentifikatsiyani chetlab o'tish bilan bog'liq YAGONA
    // ko'rinadigan signal. `Information` bo'lsa ular boshqa yuzlab satr
    // orasida yo'qolardi va noto'g'ri sozlangan server jimgina ishlab
    // ketaverardi.

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "🔴 SINOV UCHUN KIRISH YOQILGAN (`POST /api/v1/auth/dev/quick-login`): "
                  + "namunaviy hisoblarga PAROLSIZ va KODSIZ kirish mumkin. "
                  + "Muhit={Environment}. O'chirish uchun `{Key}` kalitini olib tashlang. "
                  + "⚠️ Bu satr ishlab chiqarish serverida CHIQMASLIGI kerak.")]
    public static partial void DevQuickLoginEnabled(ILogger logger, string environment, string key);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "`{Key}` yoqilgan, LEKIN muhit {Environment} — sinov uchun kirish RAD ETILDI "
                  + "va endpoint 404 qaytaradi. Bu himoya ataylab: kalit tasodifan prod'ga "
                  + "o'tib ketsa ham eshik ochilmaydi.")]
    public static partial void DevQuickLoginRefused(ILogger logger, string environment, string key);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Sinov uchun kirish ishlatildi: rol={Role}, foydalanuvchi={UserId}. "
                  + "(Namunaviy hisob — haqiqiy foydalanuvchi emas.)")]
    public static partial void DevQuickLoginUsed(ILogger logger, string role, long userId);

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

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Dars yakunlandi, xona xabardor qilindi: session={SessionId}")]
    public static partial void SessionEndBroadcast(ILogger logger, long sessionId);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "Dars yakunlandi, lekin xabar yuborilmadi: session={SessionId}")]
    public static partial void SessionEndBroadcastFailed(
        ILogger logger, Exception exception, long sessionId);

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
