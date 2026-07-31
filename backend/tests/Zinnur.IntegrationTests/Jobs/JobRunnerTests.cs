using Zinnur.Application.Jobs;

namespace Zinnur.IntegrationTests.Jobs;

/// <summary>
/// Yurgizuvchining IKKI KAFOLATI.
///
/// 1) Bitta vazifa yiqilsa ikkinchisi baribir bajariladi. Eski tizimda
///    aksincha edi: bitta xato rejalashtiruvchini jimgina o'ldirardi va
///    qolgan hamma vazifa haftalar davomida bajarilmasdi — buni hech kim
///    sezmasdi, chunki hech qanday xato ham chiqmasdi.
///
/// 2) Qulf HAR HOLDA bo'shatiladi — vazifa yiqilganda ham. Aks holda
///    birinchi yiqilishdan keyin vazifa abadiy "boshqa instance bajaryapti"
///    holatiga tushib qolardi.
/// </summary>
public sealed class JobRunnerTests(JobFactory factory) : IClassFixture<JobFactory>
{
    /// <summary>★ Yiqilgan vazifa keyingisini TO'XTATMAYDI.</summary>
    [Fact]
    public async Task RunAll_WhenOneJobThrows_StillRunsTheOthers()
    {
        var broken = FakeJob.Failing(FakeJob.UniqueName("broken"));
        var healthy = FakeJob.Succeeding(FakeJob.UniqueName("healthy"), processed: 3);

        var runner = factory.CreateIndependentRunner();

        var executions = await runner.RunAllAsync([broken, healthy]);

        executions.Should().HaveCount(2);

        executions[0].Outcome.Should().Be(JobOutcome.Failed);
        executions[0].ErrorMessage.Should().NotBeNullOrEmpty();

        executions[1].Outcome.Should().Be(JobOutcome.Completed,
            "bitta vazifadagi xato ikkinchisini to'xtatmasligi kerak");
        executions[1].Result.Processed.Should().Be(3);
        healthy.Runs.Should().Be(1);
    }

    /// <summary>Yiqilish ISTISNO sifatida chiqmaydi — natija sifatida qaytadi.</summary>
    [Fact]
    public async Task RunAsync_WhenJobThrows_DoesNotPropagateTheException()
    {
        var broken = FakeJob.Failing(FakeJob.UniqueName("silent"));
        var runner = factory.CreateIndependentRunner();

        var execution = await runner.RunAsync(broken);

        execution.Outcome.Should().Be(JobOutcome.Failed);
        execution.Name.Should().Be(broken.Name);
    }

    /// <summary>
    /// ★ Vazifa YIQILGANDAN KEYIN ham qulf bo'shaydi: aks holda birinchi
    /// xatodan so'ng vazifa boshqa hech qachon bajarilmasdi.
    /// </summary>
    [Fact]
    public async Task RunAsync_ReleasesTheLock_EvenWhenTheJobFailed()
    {
        var name = FakeJob.UniqueName("recover");
        var runner = factory.CreateIndependentRunner();

        await runner.RunAsync(FakeJob.Failing(name));

        var healthy = new FakeJob(name, _ => Task.FromResult(new JobRunResult(1, 0)));
        var second = await runner.RunAsync(healthy);

        second.Outcome.Should().Be(JobOutcome.Completed);
        healthy.Runs.Should().Be(1);
    }

    /// <summary>Har yurishning davomiyligi o'lchanadi (log uchun).</summary>
    [Fact]
    public async Task RunAsync_MeasuresDuration()
    {
        var runner = factory.CreateIndependentRunner();

        var execution = await runner.RunAsync(FakeJob.Succeeding(FakeJob.UniqueName("timed")));

        execution.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }
}
