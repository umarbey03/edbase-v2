using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Auth.Dtos;
using Zinnur.WebApi.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// ⚠️ FAQAT SINOV UCHUN — BIR BOSISHDA ROL BO'YICHA KIRISH
/// ════════════════════════════════════════════════════════════════════════
///
/// <c>/api/v1/auth/dev/*</c> — HAQIQIY kirish yo'llaridan
/// (<c>/auth/phone/*</c>, Mini App) ATAYLAB ALOHIDA marshrutda va
/// ALOHIDA faylda. Sabab uch xil:
///
///   1) O'CHIRISH ARZON. Xususiyat keraksiz bo'lsa — bu fayl,
///      `DevQuickLogin*` ikkitasi va `Program.cs` dagi ikki qator
///      o'chiriladi. Haqiqiy `AuthController` ga TEGILMAYDI.
///
///   2) KO'RINADI. Marshrutdagi <c>dev</c> so'zi log'da, tarmoq
///      panelida va OpenAPI ro'yxatida darrov ko'zga tashlanadi.
///
///   3) HAQIQIY OQIM TOZA QOLADI. `AuthController` — kirish
///      shartnomasining kanonik yozuvi; unga "sinov uchun" shoxi
///      qo'shilsa, keyingi o'quvchi qaysi yo'l asosiy ekanini
///      darrov ayta olmasdi.
///
/// 🔴 DARVOZALAR (uchtasi, hammasi majburiy) — <see cref="DevQuickLoginGate"/>
///    va <see cref="DevQuickLoginService"/> izohlarida. Qisqasi:
///    oshkor kalit + muhit <c>Production</c> emas + faqat
///    `DemoDataSeeder` yozgan hisoblar.
///
/// ★ RATE-LIMIT ATAYLAB QO'YILMADI. Bu endpoint hech nima "topmaydi"
///   (parol ham, kod ham yo'q), ya'ni uni ko'p marta urishdan hech
///   qanday foyda yo'q — chegara faqat tugmani ketma-ket bosgan
///   tekshiruvchini 429 bilan to'xtatardi. Haqiqiy chegara — darvozalar.
/// </summary>
[ApiController]
[Route("api/v1/auth/dev")]
[Produces("application/json")]
[AllowAnonymous]
public sealed class DevAuthController(
    DevQuickLoginGate gate,
    DevQuickLoginService quickLogin) : ControllerBase
{
    /// <summary>
    /// ⚠️ SINOV UCHUN. Bir bosishda kirish mumkin bo'lgan namunaviy
    /// hisoblar (har rolga bittadan).
    ///
    /// <c>200</c> — ro'yxat (bo'sh bo'lishi ham mumkin: kalit yoqilgan,
    /// lekin bazada namunaviy ma'lumot yo'q) ·
    /// <c>404</c> — xususiyat o'chiq yoki muhit <c>Production</c>.
    ///
    /// ★ KLIENT UCHUN SHARTNOMA: 404 ham, bo'sh ro'yxat ham AYNI
    ///   ma'noni beradi — "hech nima ko'rsatma". Interfeys ikki holatni
    ///   ajratmasligi kerak, aks holda kalit o'chirilgan serverda
    ///   "xatolik" ko'rinardi.
    /// </summary>
    [HttpGet("quick-login")]
    [ProducesResponseType<DevQuickLoginList>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DevQuickLoginList>> ListQuickLoginAccounts(CancellationToken ct)
    {
        if (!gate.IsEnabled) return Disabled();

        return Ok(new DevQuickLoginList(
            DevQuickLoginService.WarningText,
            gate.EnvironmentName,
            await quickLogin.ListAsync(ct)));
    }

    /// <summary>
    /// ⚠️ SINOV UCHUN. Namunaviy hisob nomidan sessiya ochadi — telefon
    /// kodisiz.
    ///
    /// Tana: <c>{ "role": "Teacher" }</c> yoki <c>{ "phone": "+998901110011" }</c>.
    /// Javob — AYNI <see cref="AuthResponse"/> (telefon oqimi va Mini App
    /// oqimi bilan bir xil), ya'ni klient tokenlarni odatdagidek saqlaydi
    /// va <c>refresh</c>/<c>logout</c> hech qanday o'zgarishsiz ishlaydi.
    ///
    /// <c>200</c> — sessiya ochildi ·
    /// <c>400</c> — na rol, na raqam berilgan ·
    /// <c>403</c> — so'ralgan hisob NAMUNAVIY EMAS (yoki umuman yo'q) ·
    /// <c>404</c> — xususiyat o'chiq yoki muhit <c>Production</c>.
    /// </summary>
    [HttpPost("quick-login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuthResponse>> QuickLogin(
        [FromBody] DevQuickLoginRequest request, CancellationToken ct)
    {
        // 🔴 DARVOZA — ENG BIRINCHI QATOR, bazaga tegishdan OLDIN.
        if (!gate.IsEnabled) return Disabled();

        return Ok(await quickLogin.LoginAsync(request, ct));
    }

    /// <summary>
    /// O'chiq holatdagi javob — <c>404</c>, <c>403</c> EMAS.
    ///
    /// ★ NIMA UCHUN 404: 403 "bu yerda nimadir bor, lekin sizga
    ///   ruxsat yo'q" degani bo'lardi va prod'da endpointning MAVJUDLIGINI
    ///   tasdiqlardi. 404 esa "bunday yo'l yo'q" — bu haqiqatga ham
    ///   yaqinroq: darvoza yopiq bo'lganda bu yo'l ishlamaydi.
    ///
    /// ★ Matn baribir tushuntiradi — u DEV mashinasida kalitni yoqishni
    ///   unutgan dasturchi uchun (prod'da bu javobni ko'radigan odam
    ///   yo'q, chunki u endpointni qidirmaydi).
    /// </summary>
    private ObjectResult Disabled() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Topilmadi",
        detail: "Sinov uchun kirish (`quick-login`) o'chirilgan. "
                + $"U `{DevQuickLoginGate.EnabledKey}` kaliti bilan va FAQAT "
                + "`Production` bo'lmagan muhitda yoqiladi.");
}
