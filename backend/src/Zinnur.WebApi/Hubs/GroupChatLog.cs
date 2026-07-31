namespace Zinnur.WebApi.Hubs;

/// <summary>
/// Guruh chati modulining loglari.
///
/// ★ NIMA UCHUN <c>ApiLog</c> GA QO'SHILMADI: u butun WebApi uchun umumiy
/// fayl va unga bir vaqtda bir necha modul yozadi. Har modul o'z log
/// sinfini saqlaganda EventId oralig'i ham ajralgan bo'ladi va ikki modul
/// bir xil raqamni band qilib qo'ymaydi.
///
/// ★ NIMA UCHUN <c>[LoggerMessage]</c>: manba generatori kompilyatsiya
/// paytida tez, ajratmasiz (allocation-free) kod yozadi. Oddiy
/// <c>logger.LogInformation($"...")</c> har chaqiruvda satr yig'adi —
/// hatto log darajasi o'chiq bo'lsa ham (CA1848).
/// </summary>
internal static partial class GroupChatLog
{
    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Debug,
        Message = "Guruh chatiga obuna: group={GroupId} kanal={Channel} user={UserId}")]
    public static partial void ThreadJoined(
        ILogger logger, long groupId, string channel, long userId);

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Warning,
        Message = "Guruh chati xabari saqlandi, lekin tarqatilmadi: group={GroupId} kanal={Channel}")]
    public static partial void BroadcastFailed(
        ILogger logger, Exception exception, long groupId, string channel);
}
