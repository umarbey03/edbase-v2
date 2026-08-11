using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// XODIM IZOHLARI — <c>/api/v1/users/{id}/notes</c>
/// ========================================================================
///
/// Eski tizimdagi <c>student_notes</c> ning o'rnini bosadi. Bu ICHKI yozuv
/// ("kech qoladi", "otasi bilan gaplashildi"), shuning uchun testlar ikki
/// narsani qo'riqlaydi:
///
///   🔴 O'QUVCHI UMUMAN KO'RMAYDI. Ko'rsa xodimlar bunday yozuvni yozmay
///      qo'yadi va vosita o'lik bo'ladi — ya'ni bu funksional talab emas,
///      vositaning ISHLASH SHARTI.
///
///   🔴 USTOZ FAQAT O'Z IZOHINI tahrirlaydi. Aks holda bir ustoz
///      boshqasining kuzatuvini o'zgartirib yoki o'chirib qo'yishi mumkin
///      bo'lardi va izohlar ishonchini yo'qotardi.
/// </summary>
public sealed class StudentNoteEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ================================================================= 1) CRUD

    [Fact]
    public async Task Note_CreatedByTeacher_AppearsInListWithAuthorAndCanEdit()
    {
        var world = await WorldBuilder.CreateAsync(factory, "izoh-crud");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var created = await CreateAsync(teacher, world.Student.Id, "Darsga kech qoldi.", world.GroupId);

        created.Body.Should().Be("Darsga kech qoldi.");
        created.AuthorId.Should().Be(world.Teacher.Id);
        created.AuthorName.Should().NotBeNullOrEmpty();
        created.GroupId.Should().Be(world.GroupId);
        created.GroupName.Should().Be(world.GroupName);
        created.CanEdit.Should().BeTrue("muallif o'z izohini tahrirlay oladi");

        var list = await ListAsync(teacher, world.Student.Id);

        list.Should().ContainSingle();
        list[0].Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task Note_UpdatedByAuthor_ChangesBodyAndStampsUpdatedAt()
    {
        var world = await WorldBuilder.CreateAsync(factory, "izoh-tahrir");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var created = await CreateAsync(teacher, world.Student.Id, "Birinchi variant");

        var response = await teacher.PutAsJsonAsync(
            NoteUri(world.Student.Id, created.Id), new { body = "Tuzatilgan variant" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        var updated = (await response.Content.ReadFromJsonAsync<NoteResponse>())!;

        updated.Body.Should().Be("Tuzatilgan variant");
        updated.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Note_DeletedByAuthor_DisappearsFromList()
    {
        var world = await WorldBuilder.CreateAsync(factory, "izoh-ochir");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var created = await CreateAsync(teacher, world.Student.Id, "O'chiriladi");

        var response = await teacher.DeleteAsync(NoteUri(world.Student.Id, created.Id));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await WorldBuilder.Body(response));

        (await ListAsync(teacher, world.Student.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Note_WithEmptyBody_IsRejected()
    {
        var world = await WorldBuilder.CreateAsync(factory, "izoh-bosh");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.PostAsJsonAsync(
            NotesUri(world.Student.Id), new { body = "   " });

        // Bo'sh matn — Domain qoidasi (`DomainException` -> 409).
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await WorldBuilder.Body(response));
    }

    /// <summary>
    /// Begona guruh Id'si bilan izoh yozib bo'lmaydi: aks holda ro'yxatda
    /// o'quvchi hech qachon o'qimagan guruh nomi ko'rinardi.
    /// </summary>
    [Fact]
    public async Task Note_WithForeignGroupContext_IsRejected()
    {
        var world = await WorldBuilder.CreateAsync(factory, "izoh-guruh");
        var other = await WorldBuilder.CreateAsync(factory, "izoh-begona");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.PostAsJsonAsync(
            NotesUri(world.Student.Id), new { body = "Izoh", groupId = other.GroupId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await WorldBuilder.Body(response));
    }

    // ================================================================= 2) RUXSAT

    /// <summary>🔴 O'QUVCHI O'Z IZOHLARINI KO'RMAYDI.</summary>
    [Fact]
    public async Task Notes_ForStudentThemselves_AreForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "izoh-oquvchi");

        using var admin = await WorldBuilder.AdminClientAsync(factory);
        await CreateAsync(admin, world.Student.Id, "Ichki eslatma");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var read = await student.GetAsync(new Uri(NotesUri(world.Student.Id), UriKind.Relative));
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(read));

        // Yozish ham mumkin emas — o'quvchi o'ziga "izoh" qo'shib
        // xodimlarni chalg'itmasin.
        var write = await student.PostAsJsonAsync(
            NotesUri(world.Student.Id), new { body = "Men yaxshi o'qiyman" });

        write.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(write));
    }

    /// <summary>Begona guruh ustozi izohlarni ko'ra ham, yoza ham olmaydi.</summary>
    [Fact]
    public async Task Notes_ForForeignTeacher_AreForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "izoh-oz");
        var other = await WorldBuilder.CreateAsync(factory, "izoh-chet");

        using var foreignTeacher = await WorldBuilder.ClientAsync(factory, other.Teacher);

        var read = await foreignTeacher.GetAsync(
            new Uri(NotesUri(world.Student.Id), UriKind.Relative));
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(read));

        var write = await foreignTeacher.PostAsJsonAsync(
            NotesUri(world.Student.Id), new { body = "Begona izoh" });

        write.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(write));
    }

    /// <summary>
    /// 🔴 BEGONA IZOHNI TAHRIRLASH VA O'CHIRISH — 403.
    ///
    /// Ikki xodim AYNI o'quvchiga ruxsatli (ustoz va kurator bitta
    /// guruhda), ya'ni "ko'rish huquqi bor" degani "tahrirlash huquqi bor"
    /// degani EMAS — aynan shu chegara tekshiriladi.
    /// </summary>
    [Fact]
    public async Task Note_OfAnotherStaffMember_CannotBeEditedOrDeleted()
    {
        var world = await WorldBuilder.CreateAsync(factory, "izoh-begona-tahrir");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);
        using var curator = await WorldBuilder.ClientAsync(factory, world.Curator);

        var teacherNote = await CreateAsync(teacher, world.Student.Id, "Ustozning kuzatuvi");

        // Kurator izohni KO'RADI, lekin `canEdit` — `false`.
        var seenByCurator = await ListAsync(curator, world.Student.Id);
        seenByCurator.Should().ContainSingle();
        seenByCurator[0].CanEdit.Should().BeFalse("begona izohda tahrirlash tugmasi bo'lmasin");

        var edit = await curator.PutAsJsonAsync(
            NoteUri(world.Student.Id, teacherNote.Id), new { body = "O'zgartirib qo'yaman" });

        edit.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(edit));

        var delete = await curator.DeleteAsync(NoteUri(world.Student.Id, teacherNote.Id));

        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden, await WorldBuilder.Body(delete));

        // Izoh joyida qolgan.
        (await ListAsync(teacher, world.Student.Id)).Should().ContainSingle();
    }

    /// <summary>O'quv bo'limi BEGONA izohni ham boshqara oladi (xodim ishdan ketsa kerak).</summary>
    [Fact]
    public async Task Note_OfTeacher_CanBeManagedByAcademic()
    {
        var world = await WorldBuilder.CreateAsync(factory, "izoh-akademik");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var note = await CreateAsync(teacher, world.Student.Id, "Ustoz yozdi");

        var seenByAdmin = await ListAsync(admin, world.Student.Id);
        seenByAdmin[0].CanEdit.Should().BeTrue("o'quv bo'limi hamma izohni boshqaradi");

        var delete = await admin.DeleteAsync(NoteUri(world.Student.Id, note.Id));
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent, await WorldBuilder.Body(delete));
    }

    /// <summary>
    /// Yo'ldagi <c>studentId</c> — BEZAK EMAS: boshqa o'quvchining izohi
    /// Id'si berilsa 404, ya'ni o'z guruhidagi o'quvchi orqali begona
    /// izohga tegib bo'lmaydi.
    /// </summary>
    [Fact]
    public async Task Note_UnderWrongStudent_IsNotFound()
    {
        var world = await WorldBuilder.CreateAsync(factory, "izoh-yol");
        var classmate = await WorldBuilder.AddStudentAsync(factory, world.GroupId, "izoh-sinf");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var note = await CreateAsync(admin, world.Student.Id, "Birinchi o'quvchi izohi");

        var response = await admin.PutAsJsonAsync(
            NoteUri(classmate.Id, note.Id), new { body = "Boshqa o'quvchi orqali" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await WorldBuilder.Body(response));
    }

    // ================================================================= yordamchi

    private static string NotesUri(long studentId) =>
        "/api/v1/users/" + studentId.ToString(CultureInfo.InvariantCulture) + "/notes";

    private static Uri NoteUri(long studentId, long noteId) =>
        new(NotesUri(studentId) + "/" + noteId.ToString(CultureInfo.InvariantCulture),
            UriKind.Relative);

    private static async Task<NoteResponse> CreateAsync(
        HttpClient client, long studentId, string body, long? groupId = null)
    {
        var response = await client.PostAsJsonAsync(
            NotesUri(studentId), new { body, groupId });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<NoteResponse>())!;
    }

    private static async Task<List<NoteResponse>> ListAsync(HttpClient client, long studentId)
    {
        var response = await client.GetAsync(new Uri(NotesUri(studentId), UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(response));

        return (await response.Content.ReadFromJsonAsync<List<NoteResponse>>())!;
    }
}
