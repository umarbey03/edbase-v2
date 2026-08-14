using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zinnur.Application.Common.Models;
using Zinnur.Application.StudentNotes.Dtos;
using Zinnur.Application.StudentNotes.Services;
using Zinnur.Application.Users.Dtos;
using Zinnur.Application.Users.Services;

namespace Zinnur.WebApi.Controllers;

/// <summary>
/// Foydalanuvchilarni boshqarish (o'quv bo'limi / admin paneli) va o'quvchi
/// profili.
///
/// Controller YUPQA: <c>[Authorize(Roles=...)]</c> — bu faqat DARVOZA
/// ("umuman kira oladimi"). "Kim kimni tahrirlay oladi" va "kim kimning
/// ma'lumotini qancha ko'radi" degan asosiy qoidalar SERVIS ICHIDA
/// (<see cref="IUserService"/>, <c>StudentAccess</c>) — aks holda yangi
/// endpoint qo'shilganda ularni takrorlash unutilardi (eski tizim zaifligi X-4).
///
/// ════════════════════════════════════════════════════════════════════════
/// ★ RUXSAT IKKI QATLAMLI (naqsh <c>PaymentsController</c> dan olingan)
///
/// Sinf darajasida faqat <c>[Authorize]</c> ("tizimga kirgan bo'l"), rol
/// filtri esa HAR endpointda alohida.
///
/// NIMA UCHUN SHUNDAY O'ZGARTIRILDI: avval sinf darajasida
/// <c>[Authorize(Roles="Academic,Admin")]</c> turardi. ASP.NET Core
/// atributlarni VA (AND) bilan birlashtiradi, ya'ni endpointga
/// <c>[Authorize(Roles="Teacher")]</c> qo'shilsa shart "(Academic yoki Admin)
/// VA Teacher" bo'lib, HECH KIM o'tolmasdi. Profil va izohlar esa ustoz,
/// kurator va o'quvchiga ham kerak.
///
/// ⚠️ Bahosi: yangi BOSHQARUV endpointi qo'shilganda
/// <c>[Authorize(Roles = ManageRoles)]</c> ni unutish mumkin. Shu sababli
/// integratsiya testlarida ustoz uchun 403 tekshiruvi bor
/// (<c>UserProfileEndpointsTests</c>), va yangi endpoint qo'shgan odam shu
/// naqshni ko'radi.
/// ════════════════════════════════════════════════════════════════════════
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize]
[Produces("application/json")]
public sealed class UsersController(
    IUserService users,
    IUserProfileService profiles,
    IStudentNoteService notes) : ControllerBase
{
    /// <summary>Foydalanuvchilarni BOSHQARISH huquqi bo'lgan rollar.</summary>
    private const string ManageRoles = "Academic,Admin";

    // ================================================================= boshqaruv

    /// <summary>
    /// Ro'yxat: qidiruv, rol/faollik/guruh/Telegram/telefon filtri, sahifalash.
    ///
    /// <c>groupId</c> — shu guruhda FAOL a'zo bo'lganlar;
    /// <c>telegramLinked</c> — Telegram bog'langan/bog'lanmaganlar;
    /// <c>phoneMissing</c> — normalizatsiyalangan telefoni YO'Q'lar.
    /// (Semantikasi <see cref="UserListQuery"/> da.)
    ///
    /// ══════════════════════════════════════════════════════════════════
    /// 🔴 KIRISHGA TAYYORLIK HISOBOTI — CUTOVER'DAN OLDIN MAJBURIY
    ///
    /// 2026-08-13 dan kirish faqat telefon + Telegram orqali. Ikkita
    /// so'rov butun tayyorlik manzarasini beradi (har rol uchun alohida
    /// yurgiziladi: <c>Admin</c>, <c>Academic</c>, <c>Teacher</c>,
    /// <c>Assistant</c>):
    ///
    ///   GET /api/v1/users?role=Teacher&amp;phoneMissing=true&amp;isActive=true
    ///       -> raqami YO'Q (yoki normalizatsiyadan o'tmagan) xodimlar.
    ///          Bular hatto BOG'LANA ham olmaydi.
    ///
    ///   GET /api/v1/users?role=Teacher&amp;telegramLinked=false&amp;isActive=true
    ///       -> raqami bor, lekin botga hali ulanmaganlar.
    ///
    /// ★ IKKALASI HAM KERAK va tartib SHU: birinchisi ikkinchisining
    ///   qism-to'plami emas — telefonsiz odam "bog'lanmagan" ro'yxatida
    ///   ham turadi, lekin uning muammosi BOSHQA va yechimi ham boshqa
    ///   (birinchisiga raqam kiritish kerak, ikkinchisiga esa faqat
    ///   botga bir marta kirish).
    /// ══════════════════════════════════════════════════════════════════
    /// </summary>
    [HttpGet]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<PagedResult<UserDetailsDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserDetailsDto>>> List(
        [FromQuery] UserListQuery query, CancellationToken ct) =>
        Ok(await users.ListAsync(query, CurrentUserId, ct));

    [HttpGet("{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDetailsDto>> Get(long id, CancellationToken ct) =>
        Ok(await users.GetAsync(id, CurrentUserId, ct));

    /// <summary>Yangi foydalanuvchi. Parol berilmasa server generatsiya qiladi.</summary>
    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<CreateUserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateUserResponse>> Create(
        [FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var created = await users.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(Get), new { id = created.User.Id }, created);
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDetailsDto>> Update(
        long id, [FromBody] UpdateUserRequest request, CancellationToken ct) =>
        Ok(await users.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>Profilni o'chirish — barcha sessiyalari darhol bekor qilinadi.</summary>
    [HttpPost("{id:long}/deactivate")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDetailsDto>> Deactivate(long id, CancellationToken ct) =>
        Ok(await users.SetActiveAsync(id, isActive: false, CurrentUserId, ct));

    [HttpPost("{id:long}/activate")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDetailsDto>> Activate(long id, CancellationToken ct) =>
        Ok(await users.SetActiveAsync(id, isActive: true, CurrentUserId, ct));

    // ⚠️ `POST /{id}/reset-password` OLIB TASHLANDI (2026-08-13).
    //
    //    Parol bilan kirish yo'q, ya'ni endpoint hech qayerda ishlamaydigan
    //    satr qaytarardi va xodim uni foydalanuvchiga "kirish paroli" deb
    //    uzatardi. Sessiyani majburan yopish uchun:
    //      • `POST /{id}/deactivate`      — profilni yopadi;
    //      • `POST /{id}/telegram/unlink` — kirish imkoniyatini ham yopadi
    //                                        va audit iziga yozadi.

    /// <summary>CSV import: <c>full_name,phone,email,role</c>. Xato qatorlar hisobotda qaytadi.</summary>
    [HttpPost("import")]
    [Authorize(Roles = ManageRoles)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    [ProducesResponseType<UserImportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserImportResponse>> Import(IFormFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);

        await using var stream = file.OpenReadStream();
        return Ok(await users.ImportCsvAsync(stream, CurrentUserId, ct));
    }

    // ================================================================= Telegram

    /// <summary>
    /// 🔴 Telegram bog'lanishini UZADI.
    ///
    /// Uzilgandan keyin o'quvchi platformaga KIRA OLMAYDI: barcha sessiyalari
    /// darhol bekor qilinadi (mavjud kirish tokeni ham). Amal audit iziga
    /// tushadi: kim, qachon, qaysi hisobni va nima sababdan uzgan.
    ///
    /// Tana IXTIYORIY: <c>{ "reason": "..." }</c>.
    ///
    /// Javob: <c>{ "telegramId": null, "telegramUsername": null }</c> ·
    /// <c>404</c> — foydalanuvchi yo'q · <c>409</c> — allaqachon bog'lanmagan ·
    /// <c>403</c> — nishon himoyalangan rol egasi (faqat Admin uzadi).
    /// </summary>
    [HttpPost("{id:long}/telegram/unlink")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<TelegramUnlinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TelegramUnlinkResponse>> UnlinkTelegram(
        long id, [FromBody] TelegramUnlinkRequest? request, CancellationToken ct) =>
        Ok(await users.UnlinkTelegramAsync(
            id, request ?? new TelegramUnlinkRequest(), CurrentUserId, ct));

    // ================================================================= profil

    /// <summary>
    /// O'quvchi profilining BUTUN mazmuni — bitta so'rovda (drawer uchun).
    ///
    /// ★ ROL FILTRI ATRIBUTDA ATAYLAB YO'Q: o'quvchi O'Z profilini, ustoz va
    /// kurator esa O'Z guruhidagi o'quvchini ko'radi — bu shartlarni atribut
    /// bilan ifodalash mumkin emas. Tekshiruv servisda (<c>StudentAccess</c>):
    /// begona profil so'ralsa <c>403</c>.
    ///
    /// 🔴 Ustoz/kurator javobida <c>finance</c> bloki <c>null</c>, o'quvchi
    /// javobida <c>notes</c> va <c>finance.transactions</c> <c>null</c>.
    /// </summary>
    [HttpGet("{id:long}/profile")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileDto>> Profile(long id, CancellationToken ct) =>
        Ok(await profiles.GetAsync(id, CurrentUserId, ct));

    // ================================================================= izohlar

    /// <summary>
    /// O'quvchi haqidagi ICHKI izohlar (yangisidan eskisiga).
    ///
    /// 🔴 <c>Student</c> roli uchun <c>403</c> — bu xodimlarning o'zaro
    /// yozuvi ("kech qoladi", "otasi bilan gaplashildi").
    /// Ustoz/kurator faqat o'z guruhidagi o'quvchi izohlarini ko'radi.
    /// </summary>
    [HttpGet("{id:long}/notes")]
    [ProducesResponseType<IReadOnlyList<StudentNoteDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<StudentNoteDto>>> Notes(
        long id, CancellationToken ct) =>
        Ok(await notes.ListAsync(id, CurrentUserId, ct));

    /// <summary>Yangi izoh. <c>groupId</c> — ixtiyoriy kontekst.</summary>
    [HttpPost("{id:long}/notes")]
    [ProducesResponseType<StudentNoteDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StudentNoteDto>> CreateNote(
        long id, [FromBody] CreateStudentNoteRequest request, CancellationToken ct)
    {
        var created = await notes.CreateAsync(id, request, CurrentUserId, ct);

        // `Location` — izohlar RO'YXATI: bitta izohni alohida o'qish
        // endpointi ataylab yo'q (drawer doim ro'yxatni oladi).
        return CreatedAtAction(nameof(Notes), new { id }, created);
    }

    /// <summary>
    /// Izoh matnini o'zgartiradi.
    ///
    /// Ustoz/kurator faqat O'Z izohini tahrirlaydi (begona izoh —
    /// <c>403</c>); o'quv bo'limi va admin hammasini.
    /// </summary>
    [HttpPut("{id:long}/notes/{noteId:long}")]
    [ProducesResponseType<StudentNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StudentNoteDto>> UpdateNote(
        long id, long noteId, [FromBody] UpdateStudentNoteRequest request, CancellationToken ct) =>
        Ok(await notes.UpdateAsync(id, noteId, request, CurrentUserId, ct));

    /// <summary>Izohni o'chiradi (ruxsat — tahrirlash bilan bir xil).</summary>
    [HttpDelete("{id:long}/notes/{noteId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteNote(long id, long noteId, CancellationToken ct)
    {
        await notes.DeleteAsync(id, noteId, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>Yuklash chegarasi servisdagi chegara bilan bir xil (2 MB).</summary>
    private const long MaxUploadBytes = 2 * 1024 * 1024;

    private long CurrentUserId =>
        long.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            CultureInfo.InvariantCulture);
}
