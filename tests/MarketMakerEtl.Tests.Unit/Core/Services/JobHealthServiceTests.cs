using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class JobHealthServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Should_report_a_job_as_not_stale_when_it_has_a_recent_completed_run()
    {
        var jobs = Substitute.For<IJobStore>();
        var runReports = Substitute.For<IScrapeRunReportStore>();
        var detailStore = Substitute.For<IItemDetailStore>();
        jobs.GetJobs(Arg.Any<CancellationToken>()).Returns([BuildJob(1)]);
        runReports.GetLastRun(1, Arg.Any<CancellationToken>()).Returns(
            new JobLastRunView(ScrapeRunStatus.Completed, NowUtc.AddHours(-1), NowUtc.AddHours(-1), NowUtc.AddHours(-1)));
        var service = CreateService(jobs, runReports, detailStore);

        var health = await service.GetJobHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Single().IsStale, Is.False);
            Assert.That(health.Single().LastRunStatus, Is.EqualTo(ScrapeRunStatus.Completed));
        });
    }

    [Test]
    public async Task Should_report_a_job_as_stale_when_it_has_no_completed_run()
    {
        var jobs = Substitute.For<IJobStore>();
        var runReports = Substitute.For<IScrapeRunReportStore>();
        var detailStore = Substitute.For<IItemDetailStore>();
        jobs.GetJobs(Arg.Any<CancellationToken>()).Returns([BuildJob(1)]);
        runReports.GetLastRun(1, Arg.Any<CancellationToken>()).Returns((JobLastRunView?)null);
        var service = CreateService(jobs, runReports, detailStore);

        var health = await service.GetJobHealth(CancellationToken.None);

        Assert.That(health.Single().IsStale, Is.True);
    }

    [Test]
    public async Task Should_count_pending_detail_fetches_only_for_eligible_jobs()
    {
        var jobs = Substitute.For<IJobStore>();
        var runReports = Substitute.For<IScrapeRunReportStore>();
        var detailStore = Substitute.For<IItemDetailStore>();
        jobs.GetEffectivelyEnabledJobs(Arg.Any<CancellationToken>()).Returns([BuildJob(1), BuildJob(2)]);
        jobs.HasQueuedOrRunningRun(1, Arg.Any<CancellationToken>()).Returns(false);
        jobs.HasQueuedOrRunningRun(2, Arg.Any<CancellationToken>()).Returns(true);
        detailStore.GetBacklogListingsNeedingDetail(
                Arg.Is<IReadOnlyCollection<int>>(ids => ids.Single() == 1), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new ListingDetailTarget(1, "listing-1", null, null, Marketplace.Mercari)]);
        var service = CreateService(jobs, runReports, detailStore);

        var pendingCount = await service.GetPendingDetailFetchCount(CancellationToken.None);

        Assert.That(pendingCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Should_return_zero_pending_detail_fetches_when_no_jobs_are_eligible()
    {
        var jobs = Substitute.For<IJobStore>();
        var runReports = Substitute.For<IScrapeRunReportStore>();
        var detailStore = Substitute.For<IItemDetailStore>();
        jobs.GetEffectivelyEnabledJobs(Arg.Any<CancellationToken>()).Returns([]);
        var service = CreateService(jobs, runReports, detailStore);

        var pendingCount = await service.GetPendingDetailFetchCount(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(pendingCount, Is.EqualTo(0));
        });
        await detailStore.DidNotReceive().GetBacklogListingsNeedingDetail(
            Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static JobHealthService CreateService(
        IJobStore jobs, IScrapeRunReportStore runReports, IItemDetailStore detailStore) =>
        new(
            jobs,
            runReports,
            new FakeTimeProvider(NowUtc),
            detailStore,
            new DetailBacklogOptions(Enabled: true, TickMinutes: 5, MaxFetchesPerTick: 30, MaxFetchesPerHour: 300, MaxDetailFetchAttempts: 3));

    private static JobView BuildJob(int id) =>
        new(id, "ps5 controller", Marketplace.Mercari, null, 24, true, null, null, DateTime.UtcNow, [], null);
}
