using System.Net;
using System.Net.Http.Json;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Api;

/// <summary>
/// ========================================================================
/// GURUH CHATI — RUXSAT MATRITSASI VA KANAL IZOLYATSIYASI
/// ========================================================================
///
/// Bu modulning eng qimmat xatosi RUXSATDA bo'ladi va u ikki xil:
///
///  1) BEGONA odam guruh chatini ochib qo'yishi — klassik va oson
///     sezilarli xato;
///  2) ★ KANAL IZOLYATSIYASINING buzilishi — ustoz o'quvchining KURATORGA
///     atalgan savolini ko'rib qolishi. Bu JIMGINA buziladi: hech kim
///     xato ko'rmaydi, sahifa ochiladi, ma'lumot chiqadi — faqat u
///     boshqa odamning yozishmasi bo'ladi. Aynan shuning uchun bu yerdagi
///     testlarning yarmi shu qoidani qulflaydi.
///
/// Testlar HAQIQIY Postgres va HAQIQIY API bilan ishlaydi (mock yo'q) —
/// sabab <see cref="ZinnurApiFactory"/> izohida.
/// </summary>
public sealed class GroupChatEndpointsTests(ZinnurApiFactory factory)
    : IClassFixture<ZinnurApiFactory>
{
    // ================================================================= ruxsat matritsasi

    /// <summary>
    /// Guruh a'zosi 200 oladi va IKKALA oqimni ko'radi — ular uning O'Z
    /// savollari (ustozga va kuratorga).
    /// </summary>
    [Fact]
    public async Task Messages_AsGroupMember_ReturnsBothChannels()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcmem");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var page = await GroupChatApi.MessagesAsync(student, world.GroupId);

        page.Channel.Should().Be(nameof(GroupChatChannel.Teacher),
            "kanal berilmasa o'quvchiga standart oqim — ustoz oqimi");
        page.AvailableChannels.Should().BeEquivalentTo(
            [nameof(GroupChatChannel.Teacher), nameof(GroupChatChannel.Curator)]);
        page.GroupName.Should().Be(world.GroupName);
        page.Items.Should().BeEmpty();
    }

    /// <summary>★ BEGONA o'quvchi — 403 (404 emas: guruh mavjud, ruxsat yo'q).</summary>
    [Fact]
    public async Task Messages_AsOutsiderStudent_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcout");
        var other = await WorldBuilder.CreateAsync(factory, "gcout2");

        using var stranger = await WorldBuilder.ClientAsync(factory, other.Student);

        var response = await stranger.GetAsync(GroupChatApi.MessagesUrl(world.GroupId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Guruh ustozi 200 oladi, lekin FAQAT o'z oqimini ko'radi.</summary>
    [Fact]
    public async Task Messages_AsTeacher_ReturnsOnlyTeacherChannel()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcteach");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var page = await GroupChatApi.MessagesAsync(teacher, world.GroupId);

        page.Channel.Should().Be(nameof(GroupChatChannel.Teacher));
        page.AvailableChannels.Should().Equal(nameof(GroupChatChannel.Teacher));
    }

    /// <summary>Kurator 200 oladi va standart oqimi — KURATOR oqimi.</summary>
    [Fact]
    public async Task Messages_AsCurator_ReturnsOnlyCuratorChannel()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gccur");

        using var curator = await WorldBuilder.ClientAsync(factory, world.Curator);

        var page = await GroupChatApi.MessagesAsync(curator, world.GroupId);

        page.Channel.Should().Be(nameof(GroupChatChannel.Curator));
        page.AvailableChannels.Should().Equal(nameof(GroupChatChannel.Curator));
    }

    /// <summary>
    /// Academic (o'quv bo'limi) — NAZORAT roli, ikkala oqimni ham ko'radi.
    ///
    /// ★ Kurator YOZISHMASIDAN farqi ataylab: u yerda admin ham kira
    /// olmaydi (ikki kishilik shaxsiy suhbat), bu yerda esa chat GURUHNING
    /// ommaviy maydoni va uni nazorat qilish o'quv bo'limining ishi.
    /// </summary>
    [Fact]
    public async Task Messages_AsAcademic_ReturnsBothChannels()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcacad");

        using var admin = await WorldBuilder.AdminClientAsync(factory);
        var academic = await WorldBuilder.CreateUserAsync(admin, UserRole.Academic, "gcacad");

        using var client = await WorldBuilder.ClientAsync(factory, academic);

        var page = await GroupChatApi.MessagesAsync(client, world.GroupId);

        page.AvailableChannels.Should().BeEquivalentTo(
            [nameof(GroupChatChannel.Teacher), nameof(GroupChatChannel.Curator)]);
    }

    [Fact]
    public async Task Messages_WithoutToken_ReturnsUnauthorized()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcanon");

        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(GroupChatApi.MessagesUrl(world.GroupId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Threads_WithoutToken_ReturnsUnauthorized()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(
            new Uri("/api/v1/group-chat/threads", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Mavjud bo'lmagan guruh — 404 (403 emas: yashiradigan narsa yo'q).</summary>
    [Fact]
    public async Task Messages_ForUnknownGroup_ReturnsNotFound()
    {
        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var response = await admin.GetAsync(GroupChatApi.MessagesUrl(999_999_999));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ================================================================= ★ KANAL IZOLYATSIYASI

    /// <summary>
    /// ★★★ MODULNING ENG MUHIM TESTI.
    ///
    /// O'quvchi ikkala oqimga ham yozadi. Keyin:
    ///   • ustoz FAQAT o'ziga atalganini ko'radi;
    ///   • kurator FAQAT o'ziga atalganini ko'radi;
    ///   • ustoz kurator oqimini SO'RASA 403 oladi (jimgina o'z oqimiga
    ///     almashtirilmaydi — eski tizim shunday qilardi va odam boshqa
    ///     narsani ko'rib turganini bilmasdi).
    /// </summary>
    [Fact]
    public async Task Messages_TeacherNeverSeesCuratorChannel()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gciso");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        await GroupChatApi.SendAsync(
            student, world.GroupId, "Ustozga savol", GroupChatChannel.Teacher);
        await GroupChatApi.SendAsync(
            student, world.GroupId, "Kuratorga maxfiy savol", GroupChatChannel.Curator);

        // --- USTOZ ---
        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var teacherPage = await GroupChatApi.MessagesAsync(teacher, world.GroupId);

        teacherPage.Items.Select(m => m.Body).Should().Equal("Ustozga savol");
        teacherPage.Items.Should().OnlyContain(
            m => m.Channel == nameof(GroupChatChannel.Teacher));

        // ★ Kurator oqimini ATAYLAB so'rasa — aniq rad javobi.
        var forbidden = await teacher.GetAsync(
            GroupChatApi.MessagesUrl(world.GroupId, GroupChatChannel.Curator));

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "ustoz o'quvchining kuratorga atalgan savolini ko'rmasligi kerak");

        // --- KURATOR ---
        using var curator = await WorldBuilder.ClientAsync(factory, world.Curator);

        var curatorPage = await GroupChatApi.MessagesAsync(curator, world.GroupId);

        curatorPage.Items.Select(m => m.Body).Should().Equal("Kuratorga maxfiy savol");

        var alsoForbidden = await curator.GetAsync(
            GroupChatApi.MessagesUrl(world.GroupId, GroupChatChannel.Teacher));

        alsoForbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "izolyatsiya IKKI tomonlama");
    }

    /// <summary>
    /// ★ YOZISH tomoni ham yopiq: ustoz kurator oqimiga xabar YUBORA olmaydi.
    ///
    /// O'qishni yopib, yozishni ochiq qoldirish tipik yarim tuzatish:
    /// ustoz kurator oqimiga yozib qo'yardi va o'quvchi javobni "kurator
    /// yozgan" deb tushunardi.
    /// </summary>
    [Fact]
    public async Task Send_TeacherToCuratorChannel_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcisow");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.PostAsJsonAsync(
            GroupChatApi.SendUrl(world.GroupId),
            new { channel = nameof(GroupChatChannel.Curator), body = "Kurator o'rniga" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// ★ "Chatlar" ro'yxatida ham izolyatsiya: ustozda KURATOR qatori
    /// UMUMAN ko'rinmaydi — o'qilmaganlar sanog'i va oxirgi xabar
    /// ko'chirmasi ham sizib chiqmasligi kerak.
    ///
    /// Bu alohida test, chunki ro'yxat va bitta guruh SARIQ so'rovlari
    /// har xil kod yo'llari: birida filtr unutilsa, ikkinchisi baribir
    /// yashil bo'lardi.
    /// </summary>
    [Fact]
    public async Task Threads_ForTeacher_HideCuratorChannelEntirely()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcisot");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        await GroupChatApi.SendAsync(
            student, world.GroupId, "Kuratorga maxfiy", GroupChatChannel.Curator);

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var threads = await GroupChatApi.ThreadsAsync(teacher);
        var mine = threads.Where(t => t.GroupId == world.GroupId).ToList();

        mine.Should().ContainSingle("ustozda bitta guruh = bitta oqim");
        mine[0].Channel.Should().Be(nameof(GroupChatChannel.Teacher));
        mine[0].LastMessageId.Should().BeNull("kurator oqimidagi xabar bu qatorga tegmaydi");
        mine[0].LastMessagePreview.Should().BeNull();
        mine[0].UnreadCount.Should().Be(0);
    }

    /// <summary>O'quvchida bitta guruh IKKI qator beradi — ikki xil suhbatdosh.</summary>
    [Fact]
    public async Task Threads_ForStudent_ListBothChannels()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcthr");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var threads = await GroupChatApi.ThreadsAsync(student);
        var mine = threads.Where(t => t.GroupId == world.GroupId).ToList();

        mine.Should().HaveCount(2);
        mine.Select(t => t.Channel).Should().BeEquivalentTo(
            [nameof(GroupChatChannel.Teacher), nameof(GroupChatChannel.Curator)]);
        mine.Should().OnlyContain(t => t.GroupName == world.GroupName);
    }

    // ================================================================= a'zolik holati

    /// <summary>
    /// ★ PAUZADAGI o'quvchi chatga KIRA OLMAYDI.
    ///
    /// Sabab loyihaning boshqa modullaridagi bilan bir xil: pauzadagi
    /// o'quvchi darsga kira olmaydi. Chatda yumshoqroq qoida qo'yilsa,
    /// u dars muhokamasini o'qib turardi — ya'ni "faol a'zolik" degan
    /// tushunchaning IKKINCHI, boshqacha ta'rifi paydo bo'lardi.
    /// </summary>
    [Fact]
    public async Task Messages_ForPausedStudent_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcpause");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        // Pauzadan OLDIN chat ochiq
        (await student.GetAsync(GroupChatApi.MessagesUrl(world.GroupId)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var paused = await admin.PostAsJsonAsync(
            $"/api/v1/groups/{world.GroupId}/members/{world.Student.Id}/pause",
            new { pausedUntil = (string?)null });

        paused.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(paused));

        // Pauzadan KEYIN yopiq — token o'zgarmagan bo'lsa ham
        var after = await student.GetAsync(GroupChatApi.MessagesUrl(world.GroupId));

        after.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Guruhdan chiqarilgan o'quvchi ham kira olmaydi (yumshoq o'chirish).</summary>
    [Fact]
    public async Task Messages_ForRemovedStudent_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcstop");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var removed = await admin.DeleteAsync(new Uri(
            $"/api/v1/groups/{world.GroupId}/members/{world.Student.Id}", UriKind.Relative));

        removed.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(removed));

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await student.GetAsync(GroupChatApi.MessagesUrl(world.GroupId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Arxivlangan guruhda chat YOPIQ — ustozga ham.</summary>
    [Fact]
    public async Task Messages_ForArchivedGroup_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcarch");

        using var admin = await WorldBuilder.AdminClientAsync(factory);

        var archived = await admin.PostAsync(
            new Uri($"/api/v1/groups/{world.GroupId}/archive", UriKind.Relative),
            content: null);

        archived.StatusCode.Should().Be(HttpStatusCode.OK, await WorldBuilder.Body(archived));

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var response = await teacher.GetAsync(GroupChatApi.MessagesUrl(world.GroupId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================= sahifalash

    /// <summary>
    /// ★ KURSORLI SAHIFALASH: takror ham, tushib qolish ham bo'lmaydi.
    ///
    /// Ofsetli sahifalashda bu test yiqilardi: sahifalar orasida yangi
    /// xabar kelsa oyna suriladi. Kursor esa <c>Id</c> ga bog'langan.
    /// </summary>
    [Fact]
    public async Task Messages_PaginateByCursor_WithoutDuplicatesOrGaps()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcpage");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        for (var i = 1; i <= 7; i++)
            await GroupChatApi.SendAsync(student, world.GroupId, "Xabar " + i);

        var first = await GroupChatApi.MessagesAsync(student, world.GroupId, take: 3);

        first.Items.Select(m => m.Body).Should().Equal("Xabar 5", "Xabar 6", "Xabar 7");
        first.HasMore.Should().BeTrue();
        first.NextBeforeId.Should().Be(first.Items[0].Id);

        var second = await GroupChatApi.MessagesAsync(
            student, world.GroupId, take: 3, beforeId: first.NextBeforeId);

        second.Items.Select(m => m.Body).Should().Equal("Xabar 2", "Xabar 3", "Xabar 4");
        second.HasMore.Should().BeTrue();

        var last = await GroupChatApi.MessagesAsync(
            student, world.GroupId, take: 3, beforeId: second.NextBeforeId);

        last.Items.Select(m => m.Body).Should().Equal("Xabar 1");
        last.HasMore.Should().BeFalse();
        last.NextBeforeId.Should().BeNull();

        // ★ YAXLITLIK: uch sahifa birgalikda AYNAN 7 ta xabarni, TAKRORSIZ
        // qamrab oladi. Off-by-one xatosi aynan shu yerda ushlanadi.
        var all = first.Items.Concat(second.Items).Concat(last.Items).ToList();

        all.Select(m => m.Id).Should().OnlyHaveUniqueItems();
        all.Should().HaveCount(7);
    }

    /// <summary>Sahifa hajmi yuqoridan cheklanadi — klient 10 000 so'ray olmaydi.</summary>
    [Fact]
    public async Task Messages_WithHugeTake_IsClamped()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gctake");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        await GroupChatApi.SendAsync(student, world.GroupId, "Bitta");

        var page = await GroupChatApi.MessagesAsync(student, world.GroupId, take: 100_000);

        page.Items.Should().ContainSingle("chegara so'rovni yiqitmaydi, faqat kesadi");
    }

    // ================================================================= o'qilmaganlar

    /// <summary>
    /// O'qilmaganlar sanog'ining to'liq oqimi:
    ///   • O'Z xabarim o'zimga o'qilmagan bo'lib QAYTMAYDI;
    ///   • GET holatni o'zgartirmaydi (sanoq turaveradi);
    ///   • "o'qildi" idempotent;
    ///   • boshqa OQIM sanog'i tegilmaydi.
    /// </summary>
    [Fact]
    public async Task Unread_CountsOnlyOtherPeopleMessages_AndMarkReadClearsThem()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcunr");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);
        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        // O'quvchi ikkala oqimga yozadi
        await GroupChatApi.SendAsync(student, world.GroupId, "Savol", GroupChatChannel.Teacher);
        await GroupChatApi.SendAsync(student, world.GroupId, "Kurator?", GroupChatChannel.Curator);

        // ★ O'z xabarim o'zimga o'qilmagan emas
        var mine = await GroupChatApi.MessagesAsync(student, world.GroupId);
        mine.UnreadCount.Should().Be(0);

        // Ustozda 1 ta o'qilmagan
        var teacherPage = await GroupChatApi.MessagesAsync(teacher, world.GroupId);
        teacherPage.UnreadCount.Should().Be(1);

        // ★ O'qish HOLATNI O'ZGARTIRMAYDI
        (await GroupChatApi.MessagesAsync(teacher, world.GroupId)).UnreadCount.Should().Be(1);

        // Aniq "o'qildi"
        var read = await GroupChatApi.MarkReadAsync(teacher, world.GroupId);
        read.Changed.Should().BeTrue();
        read.UnreadCount.Should().Be(0);
        read.LastReadMessageId.Should().Be(teacherPage.Items[^1].Id);

        // IDEMPOTENT
        var again = await GroupChatApi.MarkReadAsync(teacher, world.GroupId);
        again.Changed.Should().BeFalse();
        again.UnreadCount.Should().Be(0);

        // Ustoz javob yozadi -> endi O'QUVCHIDA o'qilmagan
        await GroupChatApi.SendAsync(teacher, world.GroupId, "Javob");

        var studentTeacherThread = await GroupChatApi.MessagesAsync(student, world.GroupId);
        studentTeacherThread.UnreadCount.Should().Be(1);

        // ★ KURATOR oqimi tegilmagan: o'quvchining o'z xabari — sanoq 0
        var studentCuratorThread = await GroupChatApi.MessagesAsync(
            student, world.GroupId, GroupChatChannel.Curator);

        studentCuratorThread.UnreadCount.Should().Be(0);
        studentCuratorThread.Items.Should().ContainSingle();
    }

    /// <summary>
    /// ★ "O'qildi" belgisi OQIM OXIRIGA qirqiladi va ORQAGA ketmaydi.
    ///
    /// Qirqmasak "9 999 999 gacha o'qidim" degan so'rov KELAJAKDAGI
    /// xabarlarni ham o'qilgan qilib qo'yardi — o'quvchi keyingi javobni
    /// umuman sezmasdi.
    /// </summary>
    [Fact]
    public async Task MarkRead_IsClampedToLastMessage_AndNeverMovesBackwards()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcmark");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);
        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var first = await GroupChatApi.SendAsync(student, world.GroupId, "Bir");
        var second = await GroupChatApi.SendAsync(student, world.GroupId, "Ikki");

        // Kelajakka ishora qiluvchi qiymat — oqim oxirigacha qirqiladi
        var clamped = await GroupChatApi.MarkReadAsync(
            teacher, world.GroupId, upToMessageId: 9_999_999);

        clamped.LastReadMessageId.Should().Be(second.Id);

        // Ustoz yangi xabar oladi — sanoq YANA ishlaydi (kelajak "o'qilgan" emas)
        await GroupChatApi.SendAsync(student, world.GroupId, "Uch");

        (await GroupChatApi.MessagesAsync(teacher, world.GroupId)).UnreadCount.Should().Be(1);

        // ★ Orqaga surish e'tiborsiz qoldiriladi
        var backwards = await GroupChatApi.MarkReadAsync(
            teacher, world.GroupId, upToMessageId: first.Id);

        backwards.Changed.Should().BeFalse();
        backwards.LastReadMessageId.Should().Be(second.Id);
        backwards.UnreadCount.Should().Be(1, "belgi orqaga ketmagani uchun sanoq oshmaydi");
    }

    /// <summary>Xabari yo'q oqimda "o'qildi" — xato emas, shunchaki bo'sh amal.</summary>
    [Fact]
    public async Task MarkRead_OnEmptyThread_IsNoOp()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcempty");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var result = await GroupChatApi.MarkReadAsync(teacher, world.GroupId);

        result.Changed.Should().BeFalse();
        result.LastReadMessageId.Should().Be(0);
        result.UnreadCount.Should().Be(0);
    }

    /// <summary>Begona odam "o'qildi" belgisini ham qo'ya olmaydi.</summary>
    [Fact]
    public async Task MarkRead_AsOutsider_ReturnsForbidden()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcmkout");
        var other = await WorldBuilder.CreateAsync(factory, "gcmkout2");

        using var stranger = await WorldBuilder.ClientAsync(factory, other.Student);

        var response = await stranger.PostAsJsonAsync(
            $"/api/v1/group-chat/groups/{world.GroupId}/read",
            new { channel = (string?)null, upToMessageId = (long?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================= matn yaxlitligi

    /// <summary>
    /// ★ UZUN MATN + EMOJI. Chegara AYNAN emojining o'rtasiga tushganda
    /// naif kesish yolg'iz surrogat qoldiradi va Postgres uni <c>U+FFFD</c>
    /// ga aylantiradi — ya'ni matn BUZILIB saqlanadi.
    ///
    /// Bu test to'liq yo'lni tekshiradi: HTTP -> Domain -> Postgres -> JSON.
    /// Domain unit testi kesishni qulflaydi, bu test esa saqlash va
    /// serializatsiya bosqichida matn buzilmasligini isbotlaydi.
    /// </summary>
    [Fact]
    public async Task Send_WithLongBodyEndingInEmoji_KeepsTextIntact()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcemoji");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        const int max = 2000;
        var body = new string('a', max - 1) + "\U0001F600";   // 2001 kod birligi

        var sent = await GroupChatApi.SendAsync(student, world.GroupId, body);

        sent.Body.Should().HaveLength(max - 1, "emoji butunligicha sig'maydi — tushiriladi");
        sent.Body.Should().NotContain("�");
        char.IsSurrogate(sent.Body[^1]).Should().BeFalse();

        // ★ Bazadan QAYTA o'qilganda ham aynan o'sha matn
        var page = await GroupChatApi.MessagesAsync(student, world.GroupId);

        page.Items.Should().ContainSingle();
        page.Items[0].Body.Should().Be(sent.Body);
    }

    /// <summary>
    /// Aralash emoji va ZWJ ketma-ketligi (oila emojisi) — chegaradan uzoq
    /// bo'lsa AYNAN o'zidek qaytadi.
    /// </summary>
    [Fact]
    public async Task Send_WithEmojiSequence_RoundTripsExactly()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcemoji2");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        const string body = "Salom \U0001F44B\U0001F3FD oila: \U0001F468‍\U0001F469‍"
                            + "\U0001F467‍\U0001F466 va \U0001F600 ❤️";

        var sent = await GroupChatApi.SendAsync(student, world.GroupId, body);

        sent.Body.Should().Be(body);

        var page = await GroupChatApi.MessagesAsync(student, world.GroupId);

        page.Items[0].Body.Should().Be(body, "Postgres UTF-8 va JSON hech nimani buzmasligi kerak");
    }

    [Fact]
    public async Task Send_WithEmptyBody_ReturnsConflict()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcblank");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var response = await student.PostAsJsonAsync(
            GroupChatApi.SendUrl(world.GroupId), new { body = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "bo'sh xabar Domain qoidasini buzadi");
    }

    /// <summary>
    /// Yuboruvchi yorlig'i (ism va rol) xabar bilan BIRGA saqlanadi —
    /// klient uni JOIN'siz oladi va xodim almashsa ham tarix o'zgarmaydi.
    /// </summary>
    [Fact]
    public async Task Send_StampsSenderNameAndRole()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcstamp");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);

        var sent = await GroupChatApi.SendAsync(teacher, world.GroupId, "Salom guruh");

        sent.SenderId.Should().Be(world.Teacher.Id);
        sent.SenderRole.Should().Be(nameof(UserRole.Teacher),
            "enum JSON'da SATR bo'lishi kerak");
        sent.SenderName.Should().NotBeNullOrWhiteSpace();
        sent.Channel.Should().Be(nameof(GroupChatChannel.Teacher));
    }

    // ================================================================= tezlik chegarasi

    /// <summary>
    /// ★ TEZLIK CHEGARASI SERVER TOMONDA. Klient tomonidagi bloklash
    /// himoya emas — uni ochib tashlash bir qator JavaScript.
    ///
    /// Chegara: 10 sekundlik oynada 10 ta xabar. 11-chisi 429 oladi va
    /// javobda <c>Retry-After</c> bo'ladi (klient qachon urinishni bilsin).
    /// </summary>
    [Fact]
    public async Task Send_ExceedingRateLimit_ReturnsTooManyRequests()
    {
        var world = await WorldBuilder.CreateAsync(factory, "gcrate");

        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        for (var i = 1; i <= 10; i++)
        {
            var ok = await student.PostAsJsonAsync(
                GroupChatApi.SendUrl(world.GroupId), new { body = "Xabar " + i });

            ok.StatusCode.Should().Be(HttpStatusCode.Created, await WorldBuilder.Body(ok));
        }

        var blocked = await student.PostAsJsonAsync(
            GroupChatApi.SendUrl(world.GroupId), new { body = "Ortiqcha" });

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.RetryAfter.Should().NotBeNull("klient qachon urinishni bilishi kerak");

        // ★ Chegara BOSHQA oqimni bloklamaydi: kalitga kanal kiradi.
        var otherChannel = await student.PostAsJsonAsync(
            GroupChatApi.SendUrl(world.GroupId),
            new { channel = nameof(GroupChatChannel.Curator), body = "Kuratorga" });

        otherChannel.StatusCode.Should().Be(HttpStatusCode.Created,
            "ustoz oqimidagi faollik kuratorga savol yozishni to'smasligi kerak");
    }
}
