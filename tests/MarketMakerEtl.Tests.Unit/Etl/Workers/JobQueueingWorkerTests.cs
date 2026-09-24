using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scheduling;
using MarketMakerEtl.Etl.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Etl.Workers;

[TestFixture]
public class JobQueueingWorkerTests
{
    [Test]
    public async Task Should_queue_due_jobs_and_report_the_queued_count()
    {
        var jobScheduling = Substitute.For<IJobSchedulingService>();
        jobScheduling.QueueDueJobs(Arg.Any<CancellationToken>()).Returns(3);
        var worker = CreateWorker(jobScheduling);

        var queued = await worker.RunOnce(CancellationToken.None);

        Assert.That(queued, Is.EqualTo(3));
    }

    [Test]
    public async Task Should_not_throw_and_report_zero_queued_when_queueing_fails()
    {
        var jobScheduling = Substitute.For<IJobSchedulingService>();
        jobScheduling.QueueDueJobs(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("database is locked"));
        var worker = CreateWorker(jobScheduling);

        var queued = await worker.RunOnce(CancellationToken.None);

        Assert.That(queued, Is.EqualTo(0));
    }

    [Test]
    public async Task Should_queue_normally_on_the_next_call_after_a_fault_clears()
    {
        var jobScheduling = Substitute.For<IJobSchedulingService>();
        jobScheduling.QueueDueJobs(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("database is locked"), _ => 2);
        var worker = CreateWorker(jobScheduling);

        var firstQueued = await worker.RunOnce(CancellationToken.None);
        var secondQueued = await worker.RunOnce(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(firstQueued, Is.EqualTo(0));
            Assert.That(secondQueued, Is.EqualTo(2));
        });
    }

    private static JobQueueingWorker CreateWorker(IJobSchedulingService jobScheduling) =>
        new(
            jobScheduling,
            new ScheduleOptions(TickMinutes: 5, RefreshIntervalHours: 24),
            TimeProvider.System,
            NullLogger<JobQueueingWorker>.Instance);
}
