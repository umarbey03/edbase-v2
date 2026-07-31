using Zinnur.Application.Jobs;

namespace Zinnur.IntegrationTests.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// LEADER LOCK — SHU BOSQICHNING ENG MUHIM TESTI
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN: platforma gorizontal masshtablanadi va fon
/// rejalashtiruvchisi HAR konteynerda ko'tariladi. Qulfsiz holatda oylik
/// to'lov yozuvlari ikki marta ochilardi, dars esa ikki marta yakunlanib,
/// o'quvchilarga ikkita "dars tugadi" xabari ketardi. Eski tizim
/// (<c>APScheduler</c>) aynan shu joyda sinardi.
///
/// Bu yerda mock YO'Q: haqiqiy Postgres va haqiqiy parallel ulanishlar.
/// Advisory lock SESSIYAGA bog'langan, ya'ni ikki mustaqil ulanish
/// Postgres uchun ikki mustaqil klient — jarayonlar soni ahamiyatsiz.
/// </summary>
public sealed class JobLockTests(JobFactory factory) : IClassFixture<JobFactory>
{
    /// <summary>
    /// ★★ ASOSIY TEST: ikki "instance" AYNI vazifani bir vaqtda ishga
    /// tushirsa, vazifa tanasi AYNAN BIR MARTA bajariladi.
    ///
    /// Test DETERMINISTIK: birinchi instance ish ICHIDA ushlab turiladi
    /// (<c>started</c> signali), ikkinchisi aynan o'sha paytda urinadi.
    /// Ya'ni "tasodifan tez bo'lib qoldi" degan holat mumkin emas.
    /// </summary>
    [Fact]
    public async Task TwoInstances_RunningTheSameJobAtOnce_ExecuteItExactlyOnce()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var job = new FakeJob(FakeJob.UniqueName("leader"), async _ =>
        {
            started.TrySetResult();
            await release.Task;
            return new JobRunResult(1, 0);
        });

        var firstInstance = factory.CreateIndependentRunner();
        var secondInstance = factory.CreateIndependentRunner();

        var firstRun = firstInstance.RunAsync(job);

        // Birinchi instance qulfni OLDI va hali ish ichida.
        // Kutish CHEKLANGAN: qulf olinmasa test mangu osilib qolmasin.
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var second = await secondInstance.RunAsync(job);

        release.TrySetResult();
        var first = await firstRun;

        job.Runs.Should().Be(1, "vazifa tanasi ikki instance'da ham bajarilmasligi kerak");
        first.Outcome.Should().Be(JobOutcome.Completed);
        second.Outcome.Should().Be(JobOutcome.SkippedLocked,
            "qulf boshqa instance'da — bu xato emas, leader lock'ning ishlayotgani");
    }

    /// <summary>Qulf band bo'lsa ikkinchi instance KUTMAYDI — darhol bo'sh qaytadi.</summary>
    [Fact]
    public async Task SecondInstance_CannotAcquire_WhileFirstHoldsTheLock()
    {
        var name = FakeJob.UniqueName("busy");
        var secondInstance = factory.CreateIndependentLock();

        await using var held = await factory.Lock.TryAcquireAsync(name);
        held.Should().NotBeNull();

        var blocked = await secondInstance.TryAcquireAsync(name);
        blocked.Should().BeNull("qulf allaqachon birinchi instance'da");
    }

    /// <summary>
    /// Qulf bo'shatilgach ikkinchi instance uni DARHOL egallaydi.
    ///
    /// ★ Bu advisory lock'ning qulf JADVALIDAN afzalligi: jadvalda "egasi +
    /// muddat" yozilardi va instance qulaganda tizim muddat tugagunicha
    /// kutardi. Bu yerda esa ulanish yopilishi = qulf bo'shashi.
    /// </summary>
    [Fact]
    public async Task Lock_IsReleased_WhenHandleIsDisposed()
    {
        var name = FakeJob.UniqueName("release");
        var secondInstance = factory.CreateIndependentLock();

        var first = await factory.Lock.TryAcquireAsync(name);
        first.Should().NotBeNull();
        await first!.DisposeAsync();

        await using var second = await secondInstance.TryAcquireAsync(name);
        second.Should().NotBeNull("qulf bo'shagandan keyin boshqa instance egallashi kerak");
    }

    /// <summary>
    /// Har vazifaga O'Z qulfi: uzoq davom etgan dars yakunlash oylik hisobni
    /// to'sib qo'ymasin va ikki instance turli vazifalarni PARALLEL bajara
    /// olsin.
    /// </summary>
    [Fact]
    public async Task DifferentJobs_DoNotBlockEachOther()
    {
        var secondInstance = factory.CreateIndependentLock();

        await using var first = await factory.Lock.TryAcquireAsync(FakeJob.UniqueName("job-a"));
        await using var second = await secondInstance.TryAcquireAsync(FakeJob.UniqueName("job-b"));

        first.Should().NotBeNull();
        second.Should().NotBeNull();
    }

    /// <summary>Qulf ushlab turilgan ekan, u BIZDA ekani tekshiriladi (heartbeat o'rni).</summary>
    [Fact]
    public async Task HeldLock_ReportsItselfAsAlive()
    {
        await using var handle = await factory.Lock.TryAcquireAsync(FakeJob.UniqueName("alive"));

        handle.Should().NotBeNull();
        (await handle!.IsHeldAsync()).Should().BeTrue();
    }
}
