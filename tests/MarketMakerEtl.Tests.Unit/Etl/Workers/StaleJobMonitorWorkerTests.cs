using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Health;
using MarketMakerEtl.Etl.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Etl.Workers;

[TestFixture]
public class StaleJobMonitorWorkerTests
{
    [Test]
    public async Task Should_check_job_health_once_per_run()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([BuildJob(isStale: false)]);
        var worker = CreateWorker(jobHealth);

        await worker.RunOnce(CancellationToken.None);

        await jobHealth.Received(1).GetJobHealth(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Should_not_throw_when_the_health_check_fails()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<JobHealthView>>>(_ => throw new InvalidOperationException("boom"));
        var worker = CreateWorker(jobHealth);

        Assert.That(async () => await worker.RunOnce(CancellationToken.None), Throws.Nothing);
    }

    [Test]
    public async Task Should_complete_without_throwing_when_stale_jobs_are_found()
    {
        var jobHealth = Substitute.For<IJobHealthService>();
        jobHealth.GetJobHealth(Arg.Any<CancellationToken>()).Returns([BuildJob(isStale: true)]);
        var worker = CreateWorker(jobHealth);

        Assert.That(async () => await worker.RunOnce(CancellationToken.None), Throws.Nothing);
        await jobHealth.Received(1).GetJobHealth(Arg.Any<CancellationToken>());
    }

    private static StaleJobMonitorWorker CreateWorker(IJobHealthService jobHealth) =>
        new(jobHealth, TimeProvider.System, NullLogger<StaleJobMonitorWorker>.Instance);

    private static JobHealthView BuildJob(bool isStale) =>
        new(1, "ps5 controller", true, null, null, null, isStale);
}
