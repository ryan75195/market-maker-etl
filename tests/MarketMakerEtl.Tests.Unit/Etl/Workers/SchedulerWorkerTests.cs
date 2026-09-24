using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scheduling;
using MarketMakerEtl.Etl.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Etl.Workers;

[TestFixture]
public class SchedulerWorkerTests
{
    [Test]
    public async Task Should_queue_due_jobs_and_report_the_queued_count()
    {
        var jobScheduling = Substitute.For<IJobSchedulingService>();
        jobScheduling.QueueDueJobs(Arg.Any<CancellationToken>()).Returns(3);
        var listingRefreshScheduling = Substitute.For<IListingRefreshSchedulingService>();
        var worker = CreateWorker(jobScheduling, listingRefreshScheduling);

        var queued = await worker.RunOnce(CancellationToken.None);

        await listingRefreshScheduling.Received(1).RefreshListingsIfDue(Arg.Any<CancellationToken>());
        Assert.That(queued, Is.EqualTo(3));
    }

    [Test]
    public async Task Should_still_queue_jobs_and_not_throw_when_listing_refresh_fails()
    {
        var jobScheduling = Substitute.For<IJobSchedulingService>();
        jobScheduling.QueueDueJobs(Arg.Any<CancellationToken>()).Returns(1);
        var listingRefreshScheduling = Substitute.For<IListingRefreshSchedulingService>();
        listingRefreshScheduling.RefreshListingsIfDue(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("refresh failed"));
        var worker = CreateWorker(jobScheduling, listingRefreshScheduling);

        var queued = await worker.RunOnce(CancellationToken.None);

        await jobScheduling.Received(1).QueueDueJobs(Arg.Any<CancellationToken>());
        Assert.That(queued, Is.EqualTo(1));
    }

    private static SchedulerWorker CreateWorker(
        IJobSchedulingService jobScheduling,
        IListingRefreshSchedulingService listingRefreshScheduling) =>
        new(
            jobScheduling,
            listingRefreshScheduling,
            new ScheduleOptions(TickMinutes: 5, RefreshIntervalHours: 24),
            TimeProvider.System,
            NullLogger<SchedulerWorker>.Instance);
}
