using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Health;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class JobHealthService : IJobHealthService
{
    private const int DetailBacklogHealthQueryLimit = 100_000;

    private readonly IJobStore _jobs;
    private readonly IScrapeRunReportStore _runReports;
    private readonly TimeProvider _timeProvider;
    private readonly IItemDetailStore _detailStore;
    private readonly DetailBacklogOptions _detailOptions;

    public JobHealthService(
        IJobStore jobs,
        IScrapeRunReportStore runReports,
        TimeProvider timeProvider,
        IItemDetailStore detailStore,
        DetailBacklogOptions detailOptions)
    {
        _jobs = jobs;
        _runReports = runReports;
        _timeProvider = timeProvider;
        _detailStore = detailStore;
        _detailOptions = detailOptions;
    }

    public async Task<IReadOnlyList<JobHealthView>> GetJobHealth(CancellationToken ct)
    {
        var jobs = await _jobs.GetJobs(ct);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var views = new List<JobHealthView>(jobs.Count);

        foreach (var job in jobs)
        {
            views.Add(await BuildJobHealth(job, nowUtc, ct));
        }

        return views;
    }

    public async Task<int> GetPendingDetailFetchCount(CancellationToken ct)
    {
        var eligibleJobIds = await GetEligibleJobIds(ct);
        if (eligibleJobIds.Count == 0)
        {
            return 0;
        }

        var pending = await _detailStore.GetBacklogListingsNeedingDetail(
            eligibleJobIds, DetailBacklogHealthQueryLimit, _detailOptions.MaxDetailFetchAttempts, ct);
        return pending.Count;
    }

    private async Task<JobHealthView> BuildJobHealth(JobView job, DateTime nowUtc, CancellationToken ct)
    {
        var lastRun = await _runReports.GetLastRun(job.Id, ct);
        var isStale = JobStalenessRule.IsStale(job.IsEnabled, lastRun?.LastCompletedRunUtc, job.IntervalHours, nowUtc);

        return new JobHealthView(
            job.Id,
            job.SearchTerm,
            job.IsEnabled,
            lastRun?.Status,
            lastRun?.StartedUtc,
            lastRun?.CompletedUtc,
            isStale);
    }

    private async Task<IReadOnlyList<int>> GetEligibleJobIds(CancellationToken ct)
    {
        var jobs = await _jobs.GetEffectivelyEnabledJobs(ct);
        var eligible = new List<int>();

        foreach (var job in jobs)
        {
            if (!await _jobs.HasQueuedOrRunningRun(job.Id, ct))
            {
                eligible.Add(job.Id);
            }
        }

        return eligible;
    }
}
