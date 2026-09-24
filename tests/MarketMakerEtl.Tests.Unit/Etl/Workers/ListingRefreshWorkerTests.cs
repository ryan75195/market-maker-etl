using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scheduling;
using MarketMakerEtl.Etl.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Etl.Workers;

[TestFixture]
public class ListingRefreshWorkerTests
{
    [Test]
    public async Task Should_run_the_refresh_check_without_throwing()
    {
        var listingRefreshScheduling = Substitute.For<IListingRefreshSchedulingService>();
        var worker = CreateWorker(listingRefreshScheduling);

        await worker.RunOnce(CancellationToken.None);

        await listingRefreshScheduling.Received(1).RefreshListingsIfDue(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Should_not_throw_when_the_refresh_check_fails()
    {
        var listingRefreshScheduling = Substitute.For<IListingRefreshSchedulingService>();
        listingRefreshScheduling.RefreshListingsIfDue(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("refresh failed"));
        var worker = CreateWorker(listingRefreshScheduling);

        Assert.That(async () => await worker.RunOnce(CancellationToken.None), Throws.Nothing);
    }

    [Test]
    public async Task Should_let_job_queueing_proceed_while_a_refresh_is_still_in_progress()
    {
        var refreshStarted = new TaskCompletionSource();
        var releaseRefresh = new TaskCompletionSource();
        var listingRefreshScheduling = Substitute.For<IListingRefreshSchedulingService>();
        listingRefreshScheduling.RefreshListingsIfDue(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                refreshStarted.SetResult();
                await releaseRefresh.Task;
            });
        var refreshWorker = CreateWorker(listingRefreshScheduling);

        var refreshRun = refreshWorker.RunOnce(CancellationToken.None);
        await refreshStarted.Task;

        var jobScheduling = Substitute.For<IJobSchedulingService>();
        jobScheduling.QueueDueJobs(Arg.Any<CancellationToken>()).Returns(1);
        var queueingWorker = new JobQueueingWorker(
            jobScheduling,
            new ScheduleOptions(TickMinutes: 5, RefreshIntervalHours: 24),
            TimeProvider.System,
            NullLogger<JobQueueingWorker>.Instance);
        var queued = await queueingWorker.RunOnce(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(queued, Is.EqualTo(1));
            Assert.That(refreshRun.IsCompleted, Is.False);
        });

        releaseRefresh.SetResult();
        await refreshRun;
    }

    private static ListingRefreshWorker CreateWorker(IListingRefreshSchedulingService listingRefreshScheduling) =>
        new(
            listingRefreshScheduling,
            new ScheduleOptions(TickMinutes: 5, RefreshIntervalHours: 24),
            TimeProvider.System,
            NullLogger<ListingRefreshWorker>.Instance);
}
