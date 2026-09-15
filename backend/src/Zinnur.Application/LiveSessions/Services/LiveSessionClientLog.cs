using Microsoft.Extensions.Logging;

namespace Zinnur.Application.LiveSessions.Services;

/// <summary>
/// Brauzer yuborgan jonli dars diagnostikasining manba-generatsiyali log
/// metodlari (<c>POST /live-sessions/{id}/client-events</c>).
///
/// ★ NIMA UCHUN <c>LiveSessionLog</c> GA QO'SHILMADI: bu qatorlar SERVER
///   hodisasi emas, KLIENT hodisasi — ya'ni ichidagi har satr foydalanuvchi
///   yuborgan (tozalangan) ma'lumot. Alohida sinf va alohida EventId
///   ularni server loglari bilan aralashtirmasdan ajratib olish imkonini
///   beradi:
///       docker logs zinnur-v2-api | jq 'select(.EventId.Id == 6700)'
///
/// ★ MAYDON NOMLARI <c>ApiLog.SessionJoined</c> BILAN AYNI
///   (<c>SessionId</c>, <c>UserId</c>) — hub'ning "darsga qo'shildi"
///   qatorlari bilan bitta so'rovda birlashtirib o'qish uchun.
///
/// EventId makoni: <c>6700–6709</c>.
/// </summary>
internal static partial class LiveSessionClientLog
{
    /// <summary>
    /// Bitta klient hodisasi — paketdagi HAR hodisa uchun bitta qator.
    ///
    /// ★ <c>Information</c>: bu nosozlik emas, O'LCHOV. <c>Warning</c> bo'lsa
    ///   har uzilish haqiqiy server ogohlantirishlari orasiga aralashardi.
    ///
    /// ★ IKKI VAQT: <c>ClientAt</c> — telefon soati (noto'g'ri bo'lishi
    ///   mumkin), <c>ServerAt</c> — paket qabul qilingan payt. Hodisalar
    ///   ketma-ketligi <c>ClientAt</c> bo'yicha, boshqa loglar bilan
    ///   solishtirish <c>ServerAt</c> bo'yicha o'qiladi.
    ///
    /// ⚠️ <c>UserAgent</c> OXIRIDA — eng uzun maydon; matnli (dev) logda
    ///    qolgan maydonlarni ekrandan surib chiqarmasin.
    /// </summary>
    [LoggerMessage(
        EventId = 6700,
        Level = LogLevel.Information,
        Message = "Jonli dars klient hodisasi: sessiya={SessionId} foydalanuvchi={UserId} "
                  + "rol={Role} host={IsHost} tur={EventType} sabab={Reason} tafsilot={Detail} "
                  + "ko'rinish={Visibility} onlayn={Online} tarmoq={Network} urinish={Attempt} "
                  + "mikrofon={MicOn} kamera={CameraOn} klient_vaqti={ClientAt} "
                  + "server_vaqti={ServerAt} telegram={Telegram} platforma={Platform} "
                  + "brauzer={UserAgent}")]
    internal static partial void ClientEvent(
        ILogger logger,
        long sessionId,
        long userId,
        string role,
        bool isHost,
        string eventType,
        string? reason,
        string? detail,
        string? visibility,
        bool? online,
        string? network,
        int? attempt,
        bool? micOn,
        bool? cameraOn,
        DateTimeOffset clientAt,
        DateTimeOffset serverAt,
        bool? telegram,
        string? platform,
        string? userAgent);
}
