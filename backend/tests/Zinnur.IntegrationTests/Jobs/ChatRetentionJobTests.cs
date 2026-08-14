using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Api;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// GURUH CHATI TARIXINI AVTOMATIK TOZALASH (WAVE 3)
/// ════════════════════════════════════════════════════════════════════════
///
/// 🔴 BU VAZIFA MA'LUMOTNI DOIMIY O'CHIRADI VA TIKLASH YO'LI YO'Q. Shuning
/// uchun bu yerda "o'chirdimi" degan savoldan KO'RA "ORTIQCHASINI o'chirib
/// yubormadimi" degan savol muhimroq — testlarning yarmi aynan shu tomonni
/// qulflaydi:
///
///   • kesimdan YANGI xabar hech qachon o'chmaydi;
///   • sozlama o'chiq bo'lsa HECH NIMA o'chmaydi (panel — yagona kalit);
///   • ikkinchi yurish qo'shimcha zarar keltirmaydi (idempotentlik);
///   • ★ o'chirilgandan KEYIN o'qilmaganlar sanog'i buzilmaydi — bu eng
///     nozik joy, chunki `GroupChatReads.LastReadMessageId` endi MAVJUD
///     BO'LMAGAN qatorga ishora qiladi.
///
/// ── NIMA UCHUN VAZIFA QO'LDA CHAQIRILADI ───────────────────────────────
/// Rejalashtiruvchi testlarda o'chiq (`Jobs:Enabled=false`) — sabab
/// <see cref="JobFactory"/> izohida. Vazifa <see cref="IJobRunner"/> orqali,
/// ya'ni HAQIQIY qulf ostida yurgiziladi.
/// </summary>
public sealed class ChatRetentionJobTests(ChatRetentionFactory factory)
    : IClassFixture<ChatRetentionFactory>
{
    /// <summary>Kesimdan ANIQ eski (fixture'da 3 oy belgilangan).</summary>
    private static readonly TimeSpan LongAgo = TimeSpan.FromDays(200);

    /// <summary>Kesimdan ANIQ yangi.</summary>
    private static readonly TimeSpan Recently = TimeSpan.FromDays(3);

    /// <summary>
    /// ⚠️ HAR TEST BAZANI "QURUQ" HOLATDAN BOSHLAYDI.
    ///
    /// Sinf ichidagi testlar BITTA bazani baham ko'radi, tozalash esa
    /// GLOBAL — u guruh tanlamaydi (talab aynan shunday: hamma guruhda,
    /// arxivlanganida ham). Ya'ni oldingi test qoldirgan eski qator
    /// keyingi testning <c>Processed</c> hisobiga qo'shilib ketardi va
    /// natija test TARTIBIGA bog'liq bo'lardi (xUnit tartibni
    /// kafolatlamaydi) — ya'ni yashil natija hech nima isbotlamasdi.
    ///
    /// Shuning uchun har test O'Z ma'lumotini yaratishdan OLDIN vazifani
    /// "hech nima topmaydigan" holatga keltiradi. Sikl bir marta emas:
    /// paket chegarasi tufayli katta qoldiq bir necha yurishda tozalanadi.
    /// </summary>
    private async Task DrainAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if ((await factory.RunChatRetentionJobAsync()).Processed == 0)
                return;
        }

        throw new InvalidOperationException(
            "Tozalash barqarorlashmadi — vazifa idempotent emas.");
    }

    // ================================================================= 1) tegilmaydi

    /// <summary>
    /// 🔴 ENG MUHIM XAVFSIZLIK TESTI: OYNA ICHIDAGI xabarga tegilmaydi.
    ///
    /// Bu qoida buzilsa o'quvchining kecha yozgan savoli yo'q bo'lardi va
    /// buni hech kim payqamasdi — chat "shunchaki bo'sh" ko'rinardi.
    /// </summary>
    [Fact]
    public async Task MessagesInsideWindow_AreNeverDeleted()
    {
        await DrainAsync();

        var world = await WorldBuilder.CreateAsync(factory, "retkeep");

        var ids = await SeedAsync(world, count: 4, age: Recently);

        var result = await factory.RunChatRetentionJobAsync();

        result.Processed.Should().Be(0, "kesimdan yangi xabar o'chirilmasligi kerak");
        (await SurvivorsAsync(ids)).Should().BeEquivalentTo(ids);
    }

    // ================================================================= 2) o'chiriladi

    /// <summary>
    /// Kesimdan eski xabarlar DOIMIY o'chiriladi, yangilari esa AYNI
    /// oqimda o'z joyida qoladi.
    ///
    /// ★ Ikkala blok ham BITTA guruhda: chegara guruh bo'yicha emas, VAQT
    /// bo'yicha o'tishi kerak. Ilgari muhokama qilingan "faqat arxiv
    /// guruhlar" varianti aynan shu tasdiqda ko'rinib qolardi.
    /// </summary>
    [Fact]
    public async Task MessagesOlderThanWindow_ArePermanentlyDeleted()
    {
        await DrainAsync();

        var world = await WorldBuilder.CreateAsync(factory, "retold");

        var old = await SeedAsync(world, count: 5, age: LongAgo);
        var fresh = await SeedAsync(world, count: 3, age: Recently);

        var result = await factory.RunChatRetentionJobAsync();

        result.Processed.Should().Be(5);
        result.Note.Should().Contain("3 oy", "log'da qaysi muddat qo'llangani ko'rinishi shart");

        (await SurvivorsAsync(old)).Should().BeEmpty("eski xabarlar QATTIQ o'chiriladi");
        (await SurvivorsAsync(fresh)).Should().BeEquivalentTo(fresh);
    }

    // ================================================================= 3) idempotentlik

    /// <summary>
    /// ★ IDEMPOTENTLIK — <c>IScheduledJob</c> ning QAT'IY shartnomasi:
    /// instance ish o'rtasida qulashi va vazifa qaytadan boshlanishi mumkin.
    ///
    /// Ikkinchi yurish HECH NIMA topmasligi kerak — ya'ni vazifa hech qanday
    /// tashqi holatga (oxirgi yurish vaqti, kursor) tayanmaydi.
    /// </summary>
    [Fact]
    public async Task SecondRun_DeletesNothingMore()
    {
        await DrainAsync();

        var world = await WorldBuilder.CreateAsync(factory, "retidem");

        var old = await SeedAsync(world, count: 4, age: LongAgo);
        var fresh = await SeedAsync(world, count: 2, age: Recently);

        (await factory.RunChatRetentionJobAsync()).Processed.Should().Be(4);

        var second = await factory.RunChatRetentionJobAsync();

        second.Processed.Should().Be(0, "takroriy yurish qo'shimcha zarar keltirmasin");
        (await SurvivorsAsync(old)).Should().BeEmpty();
        (await SurvivorsAsync(fresh)).Should().BeEquivalentTo(
            fresh, "ikkinchi yurish OMON QOLGANLARGA ham tegmasligi kerak");
    }

    // ================================================================= 4) panel kaliti

    /// <summary>
    /// 🔴 SOZLAMA O'CHIQ BO'LSA HECH NIMA O'CHMAYDI — va kalit AYNAN
    /// PANELDAN o'chiriladi (muhit o'zgaruvchisidan emas).
    ///
    /// ★ BU TESTNING BUTUN MA'NOSI SHU: vazifa `JobsSetup` da SHARTSIZ
    /// ro'yxatdan o'tadi va sozlamani `RunAsync` ICHIDA o'qiydi. Qiymat
    /// konstruktorga uzatilganda bu test yashil bo'lardi-yu, ishlab
    /// chiqarishda panel "o'chirdim" degan bilan vazifa o'chirishda davom
    /// etardi — jimgina yolg'on.
    ///
    /// ⚠️ Test o'zidan keyin holatni TIKLAYDI: sinfdagi qolgan testlar
    /// yoqilgan kalitni kutadi.
    /// </summary>
    [Fact]
    public async Task DisabledFromPanel_DeletesNothing()
    {
        await DrainAsync();

        var world = await WorldBuilder.CreateAsync(factory, "retoff");

        var old = await SeedAsync(world, count: 3, age: LongAgo);

        using var admin = await AdminAsync();

        try
        {
            var saved = await admin.PutAsJsonAsync(
                KeyUri("chat.retention_enabled"), new { value = "false" });

            saved.StatusCode.Should().Be(HttpStatusCode.OK, await Body(saved));

            var result = await factory.RunChatRetentionJobAsync();

            result.Processed.Should().Be(0);
            (await SurvivorsAsync(old)).Should().BeEquivalentTo(
                old, "o'chiq sozlama bilan bitta ham qator yo'qolmasligi kerak");
        }
        finally
        {
            // Bazadagi qator o'chiriladi -> qiymat muhitdagi `true` ga qaytadi.
            var reset = await admin.PostAsJsonAsync(ResetUri("chat.retention_enabled"), new { });
            reset.StatusCode.Should().Be(HttpStatusCode.OK, await Body(reset));
        }

        // Kalit qaytgach, AYNI qatorlar endi o'chadi — ya'ni yuqoridagi
        // natija "o'chiradigan narsa yo'q edi" degani emas.
        (await factory.RunChatRetentionJobAsync()).Processed.Should().Be(3);
    }

    // ================================================================= 5) o'qilmaganlar

    /// <summary>
    /// ★★★ ENG NOZIK JOY: TOZALASHDAN KEYIN O'QILMAGANLAR SANOG'I.
    ///
    /// Holat ataylab eng noqulay tarzda quriladi:
    ///   1) ustoz 3 ta xabar yozadi -> ular ESKI qilinadi;
    ///   2) o'quvchi ULARNI O'QIYDI — ya'ni belgi (`LastReadMessageId`)
    ///      aynan KEYINCHALIK O'CHIRILADIGAN qatorga ishora qiladi;
    ///   3) ustoz yana 2 ta yozadi (o'qilmagan = 2);
    ///   4) tozalash yuradi.
    ///
    /// Shundan keyin ham o'qilmaganlar 2 bo'lib qolishi SHART. Sanoq
    /// `Id > lastRead` bo'yicha MAVJUD qatorlar ustida hisoblanadi, ya'ni
    /// belgining "yetim" qolishi zararsiz — bu xatti-harakat shu yerda
    /// QULFLANADI, chunki uni buzish oson: masalan tozalashda belgini
    /// nolga tushirish "tozalik" uchun mantiqiy ko'rinadi, lekin o'quvchiga
    /// allaqachon o'qilgan yozishmani qaytadan "o'qilmagan" qilib
    /// ko'rsatardi.
    /// </summary>
    [Fact]
    public async Task UnreadCount_StaysCorrect_AfterPurge()
    {
        await DrainAsync();

        var world = await WorldBuilder.CreateAsync(factory, "retunread");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);
        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var old = new List<long>();

        for (var i = 0; i < 3; i++)
        {
            var sent = await GroupChatApi.SendAsync(
                teacher, world.GroupId, $"Eski xabar {i}", GroupChatChannel.Teacher);
            old.Add(sent.Id);
        }

        // O'quvchi ESKI bloknigacha o'qidi — belgi o'chiriladigan qatorda.
        var read = await GroupChatApi.MarkReadAsync(
            student, world.GroupId, GroupChatChannel.Teacher, old[^1]);

        read.LastReadMessageId.Should().Be(old[^1]);
        read.UnreadCount.Should().Be(0);

        await BackdateAsync(old, LongAgo);

        var fresh = new List<long>();

        for (var i = 0; i < 2; i++)
        {
            var sent = await GroupChatApi.SendAsync(
                teacher, world.GroupId, $"Yangi xabar {i}", GroupChatChannel.Teacher);
            fresh.Add(sent.Id);
        }

        var before = await GroupChatApi.MessagesAsync(
            student, world.GroupId, GroupChatChannel.Teacher);

        before.UnreadCount.Should().Be(2, "sinov tozalashdan OLDIN 2 dan boshlanadi");

        // ── TOZALASH ────────────────────────────────────────────────────
        (await factory.RunChatRetentionJobAsync()).Processed.Should().Be(3);

        var after = await GroupChatApi.MessagesAsync(
            student, world.GroupId, GroupChatChannel.Teacher);

        after.UnreadCount.Should().Be(
            2, "o'chirilgan xabarlar sanoqqa oldin ham, keyin ham kirmaydi");

        after.Items.Select(m => m.Id).Should().BeEquivalentTo(
            fresh, "oqimda faqat oyna ichidagi xabarlar qolishi kerak");

        // ★ BELGI ATAYLAB QOLDIRILADI va ENDI MAVJUD BO'LMAGAN Id ga
        // ishora qiladi. Bu hujjatlashtirilgan xatti-harakat: jadvallar
        // orasida FK yo'q, ya'ni "yetim" belgi hech narsani buzmaydi.
        var marker = await MarkerAsync(world.GroupId, world.Student.Id);

        marker.Should().Be(old[^1]);
        (await SurvivorsAsync([old[^1]])).Should().BeEmpty();
    }

    /// <summary>
    /// TO'LIQ BO'SHAGAN oqim: hamma xabar o'chdi, belgi qoldi.
    ///
    /// Tekshiriladigan ikki narsa:
    ///   • bo'sh oqimda o'qilmaganlar `0` va "o'qildi" so'rovi yiqilmaydi
    ///     (`MarkReadAsync` oqim oxiriga — endi `0` ga — qirqadi, `Advance`
    ///     esa ORQAGA ketmaydi, ya'ni belgi joyida qoladi);
    ///   • ★ eng muhimi: BUNDAN KEYIN kelgan xabar to'g'ri "o'qilmagan"
    ///     bo'lib ko'rinadi. Bu `Id` ketma-ketligining global o'suvchi
    ///     ekaniga tayanadi — yangi raqam eski belgidan HAR DOIM katta.
    ///     Aks holda bo'shagan guruh o'quvchi uchun abadiy "jim" bo'lib
    ///     qolardi va bu JIMGINA nosozlik bo'lardi.
    /// </summary>
    [Fact]
    public async Task EmptiedThread_KeepsMarker_AndStillFlagsNewMessages()
    {
        await DrainAsync();

        var world = await WorldBuilder.CreateAsync(factory, "retempty");

        using var teacher = await WorldBuilder.ClientAsync(factory, world.Teacher);
        using var student = await WorldBuilder.ClientAsync(factory, world.Student);

        var old = new List<long>();

        for (var i = 0; i < 2; i++)
        {
            var sent = await GroupChatApi.SendAsync(
                teacher, world.GroupId, $"Bo'shatiladigan {i}", GroupChatChannel.Teacher);
            old.Add(sent.Id);
        }

        await GroupChatApi.MarkReadAsync(
            student, world.GroupId, GroupChatChannel.Teacher, old[^1]);

        await BackdateAsync(old, LongAgo);

        (await factory.RunChatRetentionJobAsync()).Processed.Should().Be(2);

        var emptied = await GroupChatApi.MessagesAsync(
            student, world.GroupId, GroupChatChannel.Teacher);

        emptied.Items.Should().BeEmpty();
        emptied.UnreadCount.Should().Be(0);

        // Bo'sh oqimda "o'qildi" so'rovi ham ishlashi kerak (400/409 emas).
        var read = await GroupChatApi.MarkReadAsync(
            student, world.GroupId, GroupChatChannel.Teacher);

        read.Changed.Should().BeFalse("belgi ORQAGA ketmaydi");
        read.LastReadMessageId.Should().Be(old[^1], "yetim belgi o'z qiymatida qoladi");

        // ── VA ENDI ENG MUHIMI ──────────────────────────────────────────
        await GroupChatApi.SendAsync(
            teacher, world.GroupId, "Bo'shatilgandan keyingi savol", GroupChatChannel.Teacher);

        var revived = await GroupChatApi.MessagesAsync(
            student, world.GroupId, GroupChatChannel.Teacher);

        revived.UnreadCount.Should().Be(
            1, "yetim belgi yangi xabarni yashirib qo'ymasligi kerak");
    }

    // ================================================================= yordamchilar

    /// <summary>
    /// Xabarlarni TO'G'RIDAN-TO'G'RI bazaga yozadi va darhol eskirtiradi.
    ///
    /// ★ NIMA UCHUN API ORQALI EMAS: `SentAt` ni server qo'yadi (joriy on),
    /// ya'ni "3 oy oldingi xabar" ni API orqali yaratib bo'lmaydi. Soatni
    /// surish (`MutableTimeProvider`) ham mos kelmasdi — u butun ilovani,
    /// jumladan token muddatini ham surardi.
    /// </summary>
    private async Task<IReadOnlyList<long>> SeedAsync(
        StudentWorld world, int count, TimeSpan age)
    {
        var sentAt = DateTimeOffset.UtcNow - age;

        return await factory.WithDbAsync(async db =>
        {
            var ids = new List<long>(count);

            for (var i = 0; i < count; i++)
            {
                var message = Domain.Entities.GroupChatMessage.Create(
                    world.GroupId,
                    GroupChatChannel.Teacher,
                    world.Teacher.Id,
                    "Ustoz",
                    UserRole.Teacher,
                    $"Sinov xabari {i}",
                    sentAt);

                db.GroupChatMessages.Add(message);
                await db.SaveChangesAsync();

                ids.Add(message.Id);
            }

            return (IReadOnlyList<long>)ids;
        });
    }

    /// <summary>
    /// Mavjud xabarlarni "eski" qilib qo'yadi (API orqali yozilganlar uchun).
    /// </summary>
    /// <returns>
    /// O'zgartirilgan qatorlar soni. ★ Tur ATAYLAB <c>Task&lt;int&gt;</c>
    /// (CA1859): <c>ExecuteUpdateAsync</c> baribir shu qiymatni qaytaradi va
    /// uni <c>Task</c> ga "yashirish" bekorga qoplama yaratardi.
    /// </returns>
    private Task<int> BackdateAsync(IReadOnlyCollection<long> ids, TimeSpan age)
    {
        var sentAt = DateTimeOffset.UtcNow - age;

        return factory.WithDbAsync(db => db.GroupChatMessages
            .Where(m => ids.Contains(m.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.SentAt, sentAt)));
    }

    /// <summary>Berilgan Id'lardan qaysilari HAMON bazada.</summary>
    private Task<List<long>> SurvivorsAsync(IReadOnlyCollection<long> ids) =>
        factory.WithDbAsync(db => db.GroupChatMessages.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync());

    private Task<long> MarkerAsync(long groupId, long userId) =>
        factory.WithDbAsync(db => db.GroupChatReads.AsNoTracking()
            .Where(r => r.GroupId == groupId
                     && r.UserId == userId
                     && r.Channel == GroupChatChannel.Teacher)
            .Select(r => r.LastReadMessageId)
            .FirstAsync());

    private static Uri KeyUri(string key) => new($"/api/v1/settings/{key}", UriKind.Relative);

    private static Uri ResetUri(string key) =>
        new($"/api/v1/settings/{key}/reset", UriKind.Relative);

    private async Task<HttpClient> AdminAsync()
    {
        var tokens = await factory.LoginAsAdminAsync();
        return factory.CreateAuthorizedClient(tokens.AccessToken);
    }

    private static async Task<string> Body(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();
}

/// <summary>
/// Tozalash YOQILGAN holdagi fixture.
///
/// ★ NIMA UCHUN KALIT MUHITDAN BERILADI: `chat.retention_enabled` —
/// <c>SettingSource.Database</c> kaliti, ya'ni muhit qiymati faqat
/// BOSHLANG'ICH (bazada qator bo'lmasa). Shu tufayli sinf boshida u
/// yoqilgan bo'ladi, "o'chiq" testi esa uni PANEL orqali (baza qatori
/// bilan) bekor qiladi — aynan ishlab chiqarishdagi yo'l.
///
/// ⚠️ Standart qiymat (registrda) — O'CHIQ. Boshqa test sinflari bu
/// fixture'ni ishlatmaydi, ya'ni ular uchun tozalash o'z-o'zidan yurmaydi.
/// </summary>
public sealed class ChatRetentionFactory : JobFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("Chat:RetentionEnabled", "true");
        builder.UseSetting("Chat:RetentionMonths", "3");
    }
}
