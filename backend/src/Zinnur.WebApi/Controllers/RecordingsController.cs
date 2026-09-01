using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Recordings.Dtos;
using Zinnur.Application.Recordings.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Dars yozuvlari (FAZA 5.3): boshlash/to'xtatish, ro'yxat va ko'rish
/// havolasi.
///
/// Controller YUPQA: <c>[Authorize(Roles=…)]</c> — faqat DARVOZA
/// ("umuman kira oladimi"). Haqiqiy qoida ("faqat SHU darsning hosti",
/// "faqat SHU guruh a'zosi", to'lov darvozasi) <see cref="IRecordingService"/>
/// ICHIDA — aks holda yangi endpoint qo'shilganda uni takrorlash unutilardi.
/// </summary>
[ApiController]
[Route("api/v1/recordings")]
[Authorize]
[Produces("application/json")]
public sealed class RecordingsController(IRecordingService recordings) : ControllerBase
{
    /// <summary>Yozuvni BOSHLASHGA umuman urina oladigan rollar.</summary>
    /// <remarks>
    /// O'quvchi bu yerga UMUMAN kira olmaydi — "faqat host" qoidasi esa
    /// servisda: o'quv bo'limi xodimi ham boshqa guruhning darsini yoza
    /// olmasligi kerak, va buni rol atributi ifodalay olmaydi.
    /// </remarks>
    private const string HostRoles = "Teacher,Assistant,Academic,Admin";

    // ================================================================= dars ichidan

    /*
       ════════════════════════════════════════════════════════════════════
       🔴 QO'LDA BOSHLASH/TO'XTATISH OLIB TASHLANDI (2026-09-01)
       ════════════════════════════════════════════════════════════════════

       Bu yerda `POST .../recordings/start` va `.../stop` yo'llari turardi.
       Loyiha egasining qarori: "qo'lda yozuvni boshlash ham to'xtatish ham
       mumkin bo'lmasin — guruhga yozish-yozmaslik faqat tizimda GURUH
       DARAJASIDA boshqarilsa yetadi".

       ★ NEGA BU TO'G'RI: IKKI MANBA BITTA SAVOLGA JAVOB BERARDI.
         `Group.RecordEnabled` "bu guruh yoziladimi" degan sozlama edi,
         tugma esa uni chetlab o'tardi. Oqibatlari:
           • yozuvi ATAYLAB o'chirilgan guruhda dars yozib olinishi mumkin
             edi (2026-09-01 sinovida aynan shu yuz berdi — tugma bosildi
             va yozuv qatori yaratildi);
           • yozuvi yoqilgan guruhda ustoz uni to'xtatib qo'yishi va buni
             hech kim sezmasligi mumkin edi.
         Endi qaror BITTA joyda — guruh kartochkasida.

       ★ YOZUV TO'LIQ AVTOMATIK: dars `Live` ga o'tganda
         `AutoRecordingScheduler` navbatga qator qo'yadi, `RecordingWatchdogJob`
         uni bo'shatadi. Ikkala qadam ham `Group.RecordEnabled` ni hurmat
         qiladi, ya'ni sozlama yagona haqiqat.

       ⚠️ INDIKATOR QOLDI (`recording-status`, quyida): xonadagi HAR KIM
          yozuv ketayotganini ko'rishi kerak — bu rozilik masalasi va u
          boshqaruv tugmasidan mustaqil.
    */

    /// <summary>
    /// ══════════════════════════════════════════════════════════════════
    /// 🔴 "HOZIR YOZIB OLINYAPTIMI" — JONLI XONADAGI INDIKATOR UCHUN
    /// ══════════════════════════════════════════════════════════════════
    ///
    /// ★ BU YAGONA YOZUV ENDPOINTI KI, UNDA <c>[Authorize(Roles = …)]</c>
    /// ATAYLAB YO'Q. Sinf darajasidagi <c>[Authorize]</c> qoladi (tizimga
    /// kirgan bo'lish shart), lekin ROL DARVOZASI QO'YILMAYDI: indikatorni
    /// aynan O'QUVCHI ko'rishi kerak — avtomatik yozuv qarorining shartli
    /// qismi shu (izoh: <see cref="IRecordingService"/>, 1-dalil).
    ///
    /// Ruxsat servisda va u DARSGA bog'liq: guruhda bo'lmagan o'quvchi
    /// baribir 403 oladi (<c>ILiveSessionService.GetAsync</c>).
    ///
    /// ⚠️ FRONTEND UCHUN: klient bu manzilni xonada bo'lgan VAQTNING
    /// HAMMASIDA so'rab turadi, faqat yozuv ketayotganda emas — aks holda
    /// yozuvning BOSHLANISHINI hech qachon sezmasdi.
    /// </summary>
    /// <response code="200">Holat (yozuv ketyaptimi va qachondan beri).</response>
    /// <response code="403">Bu darsni ko'rish huquqi yo'q.</response>
    [HttpGet("~/api/v1/live-sessions/{sessionId:long}/recording-status")]
    [ProducesResponseType<RecordingLiveStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecordingLiveStatusDto>> LiveStatus(
        long sessionId, CancellationToken ct) =>
        Ok(await recordings.GetLiveStatusAsync(sessionId, CurrentUserId, ct));

    /// <summary>
    /// Darsning yozuv urinishlari (yangisi birinchi).
    ///
    /// O'quvchi faqat TAYYOR yozuvlarni ko'radi; xodim — barchasini, xato
    /// sababi bilan (sabab servisda).
    ///
    /// ⚠️ SHUNING UCHUN BU ENDPOINT INDIKATOR UCHUN YARAMAYDI: ketayotgan
    /// yozuv o'quvchiga umuman ko'rinmaydi. Indikator yuqoridagi
    /// <c>recording-status</c> dan oziqlanadi.
    /// </summary>
    [HttpGet("~/api/v1/live-sessions/{sessionId:long}/recordings")]
    [ProducesResponseType<IReadOnlyList<RecordingDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RecordingDto>>> ForSession(
        long sessionId, CancellationToken ct) =>
        Ok(await recordings.ListForSessionAsync(sessionId, CurrentUserId, ct));

    // ================================================================= "Dars yozuvlari" bo'limi

    /// <summary>
    /// Sana oralig'idagi yozuvlar — dizayn-parite bo'shlig'i #4 ("Dars
    /// yozuvlari" bo'limi).
    ///
    /// Qamrov KALENDAR orqali olinadi, ya'ni har rol o'zi ko'ra oladigan
    /// darslarning yozuvini ko'radi (izoh: <see cref="IRecordingService"/>).
    /// </summary>
    /// <param name="from">Mahalliy (markaz vaqti) sana — KIRADI.</param>
    /// <param name="to">Mahalliy sana — KIRADI.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RecordingListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<RecordingListItemDto>>> List(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Ok(await recordings.ListAsync(CurrentUserId, from, to, ct));

    /// <summary>
    /// "Dars yozuvlari bo'limi menga ochiqmi" (R5).
    ///
    /// ★ ROL DARVOZASI ATAYLAB YO'Q (sinf darajasidagi <c>[Authorize]</c>
    /// qoladi): javob aynan O'QUVCHIGA kerak — u "O'quv" ekranidagi
    /// kirish kartochkasini chizish yoki chizmaslikni shu javobga qarab
    /// hal qiladi. Xodimga esa har doim <c>true</c> qaytadi.
    ///
    /// ⚠️ BU RUXSAT ENDPOINTI EMAS, INTERFEYS UCHUN MASLAHAT. Haqiqiy
    /// chegara ro'yxat va havola yo'llarida (servisda) va u bu javobdan
    /// MUSTAQIL tekshiriladi — klient <c>true</c> deb ishonsa ham
    /// yopilgan yozuvni ocha olmaydi.
    /// </summary>
    /// <response code="200">Bo'lim ochiqmi.</response>
    [HttpGet("section")]
    [ProducesResponseType<RecordingSectionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RecordingSectionDto>> Section(CancellationToken ct) =>
        Ok(await recordings.GetSectionAsync(CurrentUserId, ct));

    /// <summary>
    /// Yozuvni o'quvchilarga ochadi yoki yashiradi (R5).
    ///
    /// ★ ROLLAR <c>HostRoles</c> BILAN AYNI: talab ko'rinishni "o'quv
    /// bo'limi VA teacher" boshqarishini aytadi, ya'ni darvoza yozuvni
    /// boshlash darvozasi bilan bir xil. Haqiqiy qoida esa servisda:
    /// begona guruh darsiga tegib bo'lmaydi va o'quv bo'limi yopgan
    /// yozuvni ustoz qayta ocha olmaydi (<see cref="IRecordingService.SetVisibilityAsync"/>).
    /// </summary>
    /// <response code="200">Yangi holat.</response>
    /// <response code="403">Ruxsat yo'q yoki yopganini ochishga urinish.</response>
    /// <response code="409">Tayyor bo'lmagan yozuvni ochishga urinish.</response>
    [HttpPatch("{id:long}/visibility")]
    [Authorize(Roles = HostRoles)]
    [ProducesResponseType<RecordingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecordingDto>> SetVisibility(
        long id, [FromBody] UpdateRecordingVisibilityRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Ok(await recordings.SetVisibilityAsync(id, request.Visible, CurrentUserId, ct));
    }

    /// <summary>
    /// Ko'rish uchun MUDDATLI imzolangan havola.
    ///
    /// ══════════════════════════════════════════════════════════════════
    /// ★ NIMA UCHUN FAYL API ORQALI OQIM QILINMAYDI (vazifa fayllaridan
    /// FARQLI): sabab <see cref="IRecordingStorage"/> izohida — ikkala
    /// tomon ham o'sha yerda yozilgan. Qisqasi: bir yozuv ~0.5 GB va uni
    /// proxy qilish jonli darsning O'ZI foydalanadigan tarmoq kanalini
    /// yeb qo'yardi; bundan tashqari videoda oldinga o'tish (<c>Range</c>)
    /// uchun butunlay yangi yo'l yozish kerak bo'lardi.
    ///
    /// ⚠️ FRONTEND UCHUN: javobdagi <c>expiresAt</c> — SHARTNOMANING BIR
    /// QISMI. Pleyer havola muddati tugashidan oldin yangisini so'rab,
    /// ko'rish o'rnini (<c>currentTime</c>) saqlab qolishi kerak; busiz
    /// uzun video o'rtada "sababsiz" to'xtardi.
    /// ══════════════════════════════════════════════════════════════════
    /// </summary>
    /// <response code="200">Havola va uning muddati.</response>
    /// <response code="403">Ruxsat yo'q yoki to'lov qarzi (`Video` qamrovi).</response>
    /// <response code="409">Yozuv hali tayyor emas yoki chiqmagan.</response>
    /// <response code="503">Ombor sozlanmagan.</response>
    [HttpGet("{id:long}/link")]
    [ProducesResponseType<RecordingLinkDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RecordingLinkDto>> Link(long id, CancellationToken ct)
    {
        var link = await recordings.CreateViewLinkAsync(id, CurrentUserId, ct);

        // 🔴 HAVOLA HECH QAYERDA KESHLANMASIN: uning ichida imzo bor va
        // oraliq proksi (yoki umumiy kompyuterdagi brauzer) uni saqlab
        // qolsa, keyingi foydalanuvchi yozuvni ochib olardi. Ruxsat esa
        // HAR so'rovda qaytadan tekshiriladi — kesh bu qoidani chetlab
        // o'tardi.
        Response.Headers.CacheControl = "no-store";

        return Ok(link);
    }

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
