using System.Net;
using Microsoft.EntityFrameworkCore;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// O'QUVCHI CHEGARALARI (2026-10-23 xavfsizlik auditi).
///
/// Audit ikki joyni topdi: avatar endpoint'i har qanday kirgan
/// foydalanuvchiga istalgan odamning rasmini berardi (Id'ni sanab butun
/// bazani yig'ish mumkin edi), kutubxona esa o'quvchiga PDF'larni
/// (arxivdagilarni ham) berardi. Ikkalasi ham faqat rol bo'yicha ochiq edi.
/// </summary>
public sealed class StudentDataBoundaryTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    [Fact]
    public async Task Student_cannot_read_someone_elses_avatar()
    {
        var world = await WorldBuilder.CreateAsync(factory, "boundary-avatar");

        // Ustozda rasm BOR deb belgilaymiz — javob "rasm yo'q" bilan bir xil
        // bo'lishi kerak, aks holda kimning rasmi borligi sezilardi.
        await factory.WithDbAsync(async db =>
        {
            var teacher = await db.Users.FirstAsync(u => u.Id == world.Teacher.Id);
            teacher.SetAvatar("avatars/boundary-test.jpg", DateTimeOffset.UtcNow);
            return await db.SaveChangesAsync();
        });

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await student.GetAsync($"/api/v1/profile/avatar/{world.Teacher.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Student_cannot_list_or_open_library_books()
    {
        var world = await WorldBuilder.CreateAsync(factory, "boundary-books");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var list = await student.GetAsync("/api/v1/books");
        list.StatusCode.Should().Be(HttpStatusCode.NotFound, await WorldBuilder.Body(list));

        var ticket = await student.GetAsync("/api/v1/books/1/ticket");
        ticket.StatusCode.Should().Be(HttpStatusCode.NotFound, await WorldBuilder.Body(ticket));
    }

    [Fact]
    public async Task Teacher_still_sees_the_library()
    {
        var world = await WorldBuilder.CreateAsync(factory, "boundary-books-staff");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var list = await teacher.GetAsync("/api/v1/books");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(list));
    }
}
