using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class DetailBacklogService : IDetailBacklogService
{
    private static readonly DetailBacklogTickResult EmptyResult = new(0, 0, 0, []);

    private readonly IJobStore _jobs;
    private readonly IItemDetailStore _detailStore;
    private readonly IItemDetailFetchService _detailFetch;
    private readonly DetailBacklogOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly object _budgetLock = new();
    private readonly List<DateTime> _recentFetchTimestampsUtc = [];

    public DetailBacklogService(
        IJobStore jobs,
        IItemDetailStore detailStore,
        IItemDetailFetchService detailFetch,
        DetailBacklogOptions options,
        TimeProvider timeProvider)
    {
        _jobs = jobs;
        _detailStore = detailStore;
        _detailFetch = detailFetch;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<DetailBacklogTickResult> RunTick(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            return EmptyResult;
        }

        var maxThisTick = Math.Min(_options.MaxFetchesPerTick, RemainingHourlyBudget());
        if (maxThisTick <= 0)
        {
            return EmptyResult;
        }

        var eligibleJobIds = await GetEligibleJobIds(ct);
        if (eligibleJobIds.Count == 0)
        {
            return EmptyResult;
        }

        var targets = await _detailStore.GetBacklogListingsNeedingDetail(
            eligibleJobIds, maxThisTick, _options.MaxDetailFetchAttempts, ct);

        return await FetchTargets(targets, ct);
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

    private async Task<DetailBacklogTickResult> FetchTargets(
        IReadOnlyList<ListingDetailTarget> targets, CancellationToken ct)
    {
        var succeeded = 0;
        var failures = new List<ScrapeRunIssueDetails>();

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            RecordFetchAttempt();
            var issue = await _detailFetch.FetchListingDetail(target, ct);

            if (issue is null)
            {
                succeeded++;
                continue;
            }

            failures.Add(issue);
            if (succeeded == 0)
            {
                break;
            }
        }

        return new DetailBacklogTickResult(targets.Count, succeeded + failures.Count, succeeded, failures);
    }

    private int RemainingHourlyBudget()
    {
        var cutoffUtc = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-1);

        lock (_budgetLock)
        {
            _recentFetchTimestampsUtc.RemoveAll(timestamp => timestamp <= cutoffUtc);
            return Math.Max(0, _options.MaxFetchesPerHour - _recentFetchTimestampsUtc.Count);
        }
    }

    private void RecordFetchAttempt()
    {
        lock (_budgetLock)
        {
            _recentFetchTimestampsUtc.Add(_timeProvider.GetUtcNow().UtcDateTime);
        }
    }
}
