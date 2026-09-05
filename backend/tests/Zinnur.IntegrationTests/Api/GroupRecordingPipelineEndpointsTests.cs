using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// GURUHNING YOZUV MEXANIZMI — SHARTNOMA VA RUXSAT
/// ════════════════════════════════════════════════════════════════════════
///
/// <c>Group.RecordingPipeline</c> guruhning darslari QAYSI yo'l bilan
/// yozilishini hal qiladi (SPEC-RECORDING-V2 §2.6). Ya'ni bu maydonni
/// yozish — o'nlab soatlik dars yozuvining taqdirini hal qilish, va u
/// aynan shuning uchun o'quv bo'limi/administrator darvozasi ortida
/// turishi kerak.
///
/// ── UCHTA NARSA QULFLANADI ──────────────────────────────────────────────
///
///  1) SIM USTIDAGI SHAKL — SATR (<c>"RoomComposite"</c> /
///     <c>"TrackComposition"</c>), enum raqami emas. Frontend tipi
///     (`shared/types/api.ts`) QO'LDA yoziladi va u AYNAN shu satrlarni
///     kutadi.
///  2) 🔴 RUXSAT — o'quvchi ham, ustoz ham bu maydonni yoza olmaydi.
///  3) 🔴 PUT SEMANTIKASI — maydon yuborilmasa qiymat ESKI yo'lga
///     (<c>RoomComposite</c>) qaytadi. Bu tuzoq loyihada allaqachon bir
///     marta ishlagan (`categoryId`), shuning uchun u yerda ham, bu yerda
///     ham test bilan yozib qo'yilgan: standart ATAYLAB bugungi
///     xatti-harakat, ya'ni maydonni bilmaydigan eski klient guruhni
///     tajriba quvuriga o'tkazib yubora OLMAYDI.
/// </summary>
public sealed class GroupRecordingPipelineEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    private const int DurationMinutes = 80;
    private const int CourseMonths = 8;
    private const string StartTime = "19:00:00";

    private static readonly string[] MondayWednesday = ["Monday", "Wednesday"];

    // ================================================================= shartnoma

    /// <summary>
    /// Standart — <c>RoomComposite</c>: maydonsiz yaratilgan guruh
    /// BUGUNGI yo'lda qoladi.
    /// </summary>
    [Fact]
    public async Task Create_WithoutTheField_DefaultsToRoomComposite()
    {
        using var admin = await AdminClientAsync();

        var created = await CreateAsync(admin, Payload("IT-quvur-standart"));

        created.Group.RecordingPipeline.Should().Be(
            "RoomComposite", "maydonsiz so'rov hech narsani o'zgartirmasin");

        (await PipelineInDbAsync(created.Group.Id)).Should().Be(RecordingPipeline.RoomComposite);
    }

    /// <summary>
    /// Yangi quvurni ATAYLAB tanlash ishlaydi va u BAZAGA yetib boradi.
    ///
    /// ★ Baza ustuni ALOHIDA tekshiriladi: DTO to'g'ri, ustun esa
    ///   yozilmagan holat "ishladi" bo'lib ko'rinardi va faqat birinchi
    ///   darsda ochilardi.
    /// </summary>
    [Fact]
    public async Task Create_WithTrackComposition_IsStoredAndReturned()
    {
        using var admin = await AdminClientAsync();

        var payload = Payload("IT-quvur-yangi", recordingPipeline: "TrackComposition");
        var created = await CreateAsync(admin, payload);

        created.Group.RecordingPipeline.Should().Be("TrackComposition");

        (await PipelineInDbAsync(created.Group.Id)).Should().Be(RecordingPipeline.TrackComposition);

        // Ro'yxat/kartochka o'qish yo'li ham AYNI qiymatni beradi.
        var card = await admin.GetFromJsonAsync<GroupView>(
            $"/api/v1/groups/{created.Group.Id}");

        card!.RecordingPipeline.Should().Be("TrackComposition");
    }

    /// <summary>
    /// 🔴 PUT = TO'LIQ ALMASHTIRISH: maydon yuborilmasa guruh ESKI yo'lga
    /// qaytadi. Bu — xatti-harakat, sirpanish emas; frontend joriy
    /// qiymatni yuklab, qaytarib yuborishi shart (`buildPayload` naqshi).
    /// </summary>
    [Fact]
    public async Task Update_WithoutTheField_FallsBackToRoomComposite()
    {
        using var admin = await AdminClientAsync();

        var created = await CreateAsync(
            admin, Payload("IT-quvur-put", recordingPipeline: "TrackComposition"));

        (await PipelineInDbAsync(created.Group.Id)).Should().Be(RecordingPipeline.TrackComposition);

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/groups/{created.Group.Id}", Payload("IT-quvur-put"));

        response.StatusCode.Should().Be(
            HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        (await PipelineInDbAsync(created.Group.Id)).Should().Be(
            RecordingPipeline.RoomComposite,
            "standart ATAYLAB bugungi xatti-harakat — eski klient tajribaga o'tkaza olmaydi");
    }

    /// <summary>Noma'lum qiymat 400 — enum satri jimgina standartga tushmaydi.</summary>
    [Fact]
    public async Task Create_WithUnknownPipeline_ReturnsBadRequest()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/groups", Payload("IT-quvur-xato", recordingPipeline: "Kechqurun"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ================================================================= 🔴 ruxsat

    /// <summary>
    /// 🔴 USTOZ BU MAYDONNI YOZA OLMAYDI. Guruh tahriri
    /// <c>Academic,Admin</c> darvozasi ortida va yangi maydon o'sha
    /// darvozani KENGAYTIRMAYDI.
    /// </summary>
    [Fact]
    public async Task Update_AsTeacher_IsForbidden()
    {
        using var admin = await AdminClientAsync();

        var created = await CreateAsync(admin, Payload("IT-quvur-ustoz"));

        using var teacher = await StaffClientAsync(admin, UserRole.Teacher);

        var response = await teacher.PutAsJsonAsync(
            $"/api/v1/groups/{created.Group.Id}",
            Payload("IT-quvur-ustoz", recordingPipeline: "TrackComposition"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await PipelineInDbAsync(created.Group.Id)).Should().Be(
            RecordingPipeline.RoomComposite, "rad etilgan so'rov bazaga TEGMASIN");
    }

    /// <summary>
    /// 🔴 O'QUVCHI GURUH TAHRIRIGA UMUMAN YAQINLASHMAYDI (sinf darajasidagi
    /// rol darvozasi).
    /// </summary>
    [Fact]
    public async Task Update_AsStudent_IsForbidden()
    {
        using var admin = await AdminClientAsync();

        var created = await CreateAsync(admin, Payload("IT-quvur-oquvchi"));

        var student = await WorldBuilder.CreateUserAsync(admin, UserRole.Student, "quvur");

        using var client = await WorldBuilder.ClientAsync(factory, student);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/groups/{created.Group.Id}",
            Payload("IT-quvur-oquvchi", recordingPipeline: "TrackComposition"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await PipelineInDbAsync(created.Group.Id)).Should().Be(RecordingPipeline.RoomComposite);
    }

    // ================================================================= yordamchilar

    private Task<RecordingPipeline> PipelineInDbAsync(long groupId) =>
        factory.WithDbAsync(db => db.Groups
            .AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => g.RecordingPipeline)
            .FirstAsync());

    /// <summary>
    /// So'rov tanasi. <paramref name="recordingPipeline"/> <c>null</c>
    /// bo'lsa maydon UMUMAN yuborilmaydi.
    ///
    /// ⚠️ LUG'AT, ANONIM TUR EMAS — VA BU SHARTNOMANING BIR QISMI:
    /// <c>"recordingPipeline": null</c> yuborish 400 beradi (maydon
    /// nullable EMAS). "Yubormaslik" va "null yuborish" — ikki BOSHQA
    /// narsa; frontend maydonni bilmasa uni umuman qo'ymasligi kerak.
    /// </summary>
    private static Dictionary<string, object?> Payload(
        string name, string? recordingPipeline = null)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = name + "-" + Guid.NewGuid().ToString("N")[..6],
            ["startDate"] = FutureMonday().ToString("O", CultureInfo.InvariantCulture),
            ["weekdays"] = MondayWednesday,
            ["startTime"] = StartTime,
            ["type"] = nameof(GroupType.Group),
            ["durationMinutes"] = DurationMinutes,
            ["courseMonths"] = CourseMonths,
            ["isActive"] = true,
        };

        if (recordingPipeline is not null)
            payload["recordingPipeline"] = recordingPipeline;

        return payload;
    }

    private static DateOnly FutureMonday()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

        while (date.DayOfWeek != DayOfWeek.Monday)
            date = date.AddDays(1);

        return date;
    }

    private static async Task<CreateGroupView> CreateAsync(
        HttpClient client, Dictionary<string, object?> payload)
    {
        var response = await client.PostAsJsonAsync("/api/v1/groups", payload);

        response.StatusCode.Should().Be(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CreateGroupView>())!;
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();

        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    /// <summary>Yangi xodim + uning nomidan ishlaydigan klient.</summary>
    private async Task<HttpClient> StaffClientAsync(HttpClient admin, UserRole role)
    {
        var user = await WorldBuilder.CreateUserAsync(admin, role, "quvur");

        return await WorldBuilder.ClientAsync(factory, user);
    }

    /// <summary>
    /// ★ DTO'lar TESTDA QAYTA E'LON QILINADI (loyihaning umumiy uslubi):
    /// server shaklini o'zgartirsa test KOMPILYATSIYADA emas, TASDIQDA
    /// yiqilishi kerak — ya'ni shartnoma buzilgani ko'rinsin. Aynan shu
    /// sabab <c>RecordingPipeline</c> bu yerda <c>string</c>.
    /// </summary>
    private sealed record GroupView(long Id, string Name, string RecordingPipeline);

    private sealed record CreateGroupView(GroupView Group, int SessionsCreated);
}
