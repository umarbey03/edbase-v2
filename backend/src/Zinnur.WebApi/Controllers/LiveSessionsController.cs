using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.LiveSessions.Dtos;
using Zinnur.Application.LiveSessions.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>Jonli darslar: ro'yxat, boshlash/yakunlash, LiveKit tokeni va davomat.</summary>
[ApiController]
[Route("api/v1/live-sessions")]
[Authorize]
[Produces("application/json")]
public sealed class LiveSessionsController(
    ILiveSessionService sessions,
    IAttendanceService attendance) : ControllerBase
{
    /// <summary>Foydalanuvchining yaqin darslari (roli bo'yicha filtrlanadi).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LiveSessionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LiveSessionDto>>> List(CancellationToken ct) =>
        Ok(await sessions.ListForUserAsync(CurrentUserId, ct));

    /// <summary>
    /// KALENDAR: sana oralig'idagi darslar (bekor qilinganlari ham) va
    /// o'quvchining har darsdagi davomati.
    ///
    /// ★ Yuqoridagi <c>GET /live-sessions</c> shartnomasi O'ZGARMADI —
    /// u "yaqin darslar" uchun qoladi. Kalendar boshqa savolga javob
    /// beradi, shuning uchun alohida yo'l va alohida DTO.
    ///
    /// Marshrut <c>{id:long}</c> bilan to'qnashmaydi: "calendar" son emas.
    /// </summary>
    /// <param name="from">Mahalliy sana <c>YYYY-MM-DD</c>, KIRADI.</param>
    /// <param name="to">Mahalliy sana, KIRADI. Oraliq 92 kundan oshmasin.</param>
    [HttpGet("calendar")]
    [ProducesResponseType<IReadOnlyList<CalendarSessionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<CalendarSessionDto>>> Calendar(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Ok(await sessions.GetCalendarAsync(CurrentUserId, from, to, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<LiveSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LiveSessionDto>> Get(long id, CancellationToken ct) =>
        Ok(await sessions.GetAsync(id, CurrentUserId, ct));

    /// <summary>Darsni boshlash (faqat host).</summary>
    [HttpPost("{id:long}/start")]
    [Authorize(Roles = "Teacher,Assistant,Academic,Admin")]
    [ProducesResponseType<LiveSessionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LiveSessionDto>> Start(long id, CancellationToken ct) =>
        Ok(await sessions.StartAsync(id, CurrentUserId, ct));

    /// <summary>Darsni yakunlash (faqat host). Davomat ham yakunlanadi.</summary>
    [HttpPost("{id:long}/end")]
    [Authorize(Roles = "Teacher,Assistant,Academic,Admin")]
    [ProducesResponseType<LiveSessionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LiveSessionDto>> End(long id, CancellationToken ct) =>
        Ok(await sessions.EndAsync(id, CurrentUserId, ct));

    /// <summary>
    /// LiveKit'ga ulanish uchun token.
    /// Ruxsat (a'zolik/host) va dars holati servis ichida tekshiriladi.
    /// </summary>
    [HttpPost("{id:long}/token")]
    [ProducesResponseType<LiveKitJoinDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LiveKitJoinDto>> CreateToken(long id, CancellationToken ct) =>
        Ok(await sessions.CreateJoinTokenAsync(id, CurrentUserId, ct));

    // ================================================================= DAVOMAT

    /// <summary>
    /// Dars bo'yicha DAVOMAT VARAG'I: guruhning har bir o'quvchisi bitta
    /// qator. Yozuvi yo'q o'quvchi ham qaytadi (<c>status: null</c>) —
    /// aks holda uni belgilashning umuman yo'li bo'lmasdi.
    ///
    /// RUXSAT: o'quv bo'limi/admin, guruh ustozi/kuratori, bog'langan
    /// kurator guruhi xodimi, darsning hosti. O'QUVCHI — 403.
    ///
    /// Atributdagi rollar faqat DARVOZA; "aynan SHU guruh" tekshiruvi
    /// servis ichida (ustoz begona guruh darsini so'rasa 403).
    /// </summary>
    [HttpGet("{id:long}/attendance")]
    [Authorize(Roles = AttendanceRoles)]
    [ProducesResponseType<SessionAttendanceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SessionAttendanceDto>> Attendance(
        long id, CancellationToken ct) =>
        Ok(await attendance.GetSessionAttendanceAsync(id, CurrentUserId, ct));

    /// <summary>
    /// Bitta o'quvchining shu darsdagi davomatini QO'LDA tuzatadi.
    ///
    /// ★ PUT — TO'LIQ ALMASHTIRISH: <c>reason</c> yuborilmasa avvalgi
    /// sabab O'CHADI. Qator hali bo'lmasa YARATILADI.
    ///
    /// Vaqt o'lchovlari (kirish/chiqish/davomiylik) O'ZGARMAYDI — ular
    /// o'lchov, baho emas. Tuzatilgan qator dars yakunlanganda qayta
    /// hisoblanmaydi (<c>isManual = true</c>).
    ///
    /// Har chaqiruv AUDIT izi qoldiradi (kim, qachon, nimadan-nimaga).
    /// </summary>
    /// <response code="400">Holat berilmagan/noma'lum yoki sabab 300 belgidan uzun.</response>
    /// <response code="404">Dars yo'q yoki o'quvchi bu darsning guruhiga tegishli emas.</response>
    /// <response code="409">Dars bekor qilingan yoki qator bir vaqtda ikki joydan o'zgardi.</response>
    [HttpPut("{id:long}/attendance/{studentId:long}")]
    [Authorize(Roles = AttendanceRoles)]
    [ProducesResponseType<AttendanceRowDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AttendanceRowDto>> UpdateAttendance(
        long id,
        long studentId,
        [FromBody] UpdateAttendanceRequest request,
        CancellationToken ct) =>
        Ok(await attendance.UpdateAsync(id, studentId, request, CurrentUserId, ct));

    /// <summary>Chatning oxirgi xabarlari (sahifa ochilganda bir marta yuklanadi).</summary>
    [HttpGet("{id:long}/messages")]
    [ProducesResponseType<IReadOnlyList<ChatMessageDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> Messages(
        long id, [FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(await sessions.GetRecentMessagesAsync(id, CurrentUserId, take, ct));

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);

    /// <summary>
    /// Davomat varag'iga umuman kira oladigan rollar (DARVOZA).
    ///
    /// ★ `Student` ATAYLAB YO'Q: o'quvchi o'z davomatini o'zgartira olsa,
    /// davomat foizi, reyting va ogohlantirishlar ma'nosini yo'qotardi.
    /// U o'z davomatini `GET /api/v1/progress/attendance` va kalendar
    /// orqali FAQAT KO'RADI.
    /// </summary>
    private const string AttendanceRoles = "Teacher,Assistant,Academic,Admin";
}
