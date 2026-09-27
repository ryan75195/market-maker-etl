using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Runs;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class DetailBacklogService : IDetailBacklogService
{
    private static readonly DetailBacklogTickResult EmptyResult = new(0, 0, 0, []);

    private readonly IJobStore _jobs;
    private readonly IDetailBacklogStore _backlog;
    private readonly IItemDetailFetchService _detailFetch;
    private readonly DetailBacklogOptions _options;
    private readonly IDetailBacklogThrottleService _throttle;

    public DetailBacklogService(
        IJobStore jobs,
        IDetailBacklogStore backlog,
        IItemDetailFetchService detailFetch,
        DetailBacklogOptions options,
        IDetailBacklogThrottleService throttle)
    {
        _jobs = jobs;
        _backlog = backlog;
        _detailFetch = detailFetch;
        _options = options;
        _throttle = throttle;
    }

    public async Task<DetailBacklogTickResult> RunTick(CancellationToken ct)
    {
        if (!_options.Enabled || _throttle.IsBackingOffInfrastructureFailures())
        {
            return EmptyResult;
        }

        var eligibleJobs = await GetEligibleJobs(ct);
        if (eligibleJobs.Count == 0)
        {
            return EmptyResult;
        }

        var familyJobIds = eligibleJobs
            .Where(job => job.ProductFamilyId is not null)
            .Select(job => job.Id)
            .ToList();
        var allJobIds = eligibleJobs.Select(job => job.Id).ToList();

        var familyResult = await RunFamilyPass(familyJobIds, ct);
        var generalResult = await RunGeneralPass(allJobIds, ct);
        var familyRemaining = await _backlog.CountFamilyListingsNeedingDetail(
            familyJobIds, _options.MaxDetailFetchAttempts, ct);
        var familyInScopeRemaining = _options.FamilyInScopeOnly
            ? await _backlog.CountFamilyInScopeListingsNeedingDetail(
                familyJobIds, _options.MaxDetailFetchAttempts, ct)
            : 0;

        var combined = Combine(familyResult, generalResult, familyRemaining, familyInScopeRemaining);
        _throttle.ObserveTickResult(combined);
        return combined;
    }

    private async Task<IReadOnlyList<JobView>> GetEligibleJobs(CancellationToken ct)
    {
        var jobs = await _jobs.GetEffectivelyEnabledJobs(ct);
        var eligible = new List<JobView>();

        foreach (var job in jobs)
        {
            if (!await _jobs.HasQueuedOrRunningRun(job.Id, ct))
            {
                eligible.Add(job);
            }
        }

        return eligible;
    }

    private async Task<DetailBacklogTickResult> RunFamilyPass(IReadOnlyList<int> familyJobIds, CancellationToken ct)
    {
        if (familyJobIds.Count == 0)
        {
            return EmptyResult;
        }

        var maxThisPass = Math.Min(_options.FamilyDetailFetchesPerTick, _throttle.RemainingHourlyBudget());
        if (maxThisPass <= 0)
        {
            return EmptyResult;
        }

        if (!_options.FamilyInScopeOnly)
        {
            var targets = await _backlog.GetFamilyBacklogListingsNeedingDetail(
                familyJobIds, maxThisPass, _options.MaxDetailFetchAttempts, ct);
            return await FetchTargets(targets, ct);
        }

        return await RunFamilyPassInScope(familyJobIds, maxThisPass, ct);
    }

    private async Task<DetailBacklogTickResult> RunFamilyPassInScope(
        IReadOnlyList<int> familyJobIds, int maxThisPass, CancellationToken ct)
    {
        var inScope = await _backlog.GetFamilyInScopeListingsNeedingDetail(
            familyJobIds, maxThisPass, _options.MaxDetailFetchAttempts, ct);
        var remainingBudget = maxThisPass - inScope.Count;
        IReadOnlyList<ListingDetailTarget> unclassified = [];
        if (remainingBudget > 0)
        {
            unclassified = await _backlog.GetFamilyUnclassifiedListingsNeedingDetail(
                familyJobIds, remainingBudget, _options.MaxDetailFetchAttempts, ct);
        }

        var result = await FetchTargets([.. inScope, .. unclassified], ct);
        return result with { FamilyInScopeFetched = inScope.Count, FamilyUnclassifiedFetched = unclassified.Count };
    }

    private async Task<DetailBacklogTickResult> RunGeneralPass(IReadOnlyList<int> allJobIds, CancellationToken ct)
    {
        var maxThisPass = Math.Min(_options.MaxFetchesPerTick, _throttle.RemainingHourlyBudget());
        if (maxThisPass <= 0)
        {
            return EmptyResult;
        }

        var targets = await _backlog.GetBacklogListingsNeedingDetail(
            allJobIds, maxThisPass, _options.MaxDetailFetchAttempts, ct);

        return await FetchTargets(targets, ct);
    }

    private static DetailBacklogTickResult Combine(
        DetailBacklogTickResult family,
        DetailBacklogTickResult general,
        int familyRemaining,
        int familyInScopeRemaining) =>
        new(
            family.Selected + general.Selected,
            family.Attempted + general.Attempted,
            family.Succeeded + general.Succeeded,
            [.. family.Failures, .. general.Failures],
            family.Attempted,
            familyRemaining,
            family.FamilyInScopeFetched,
            family.FamilyUnclassifiedFetched,
            familyInScopeRemaining);

    private async Task<DetailBacklogTickResult> FetchTargets(
        IReadOnlyList<ListingDetailTarget> targets, CancellationToken ct)
    {
        if (targets.Count == 0)
        {
            return EmptyResult;
        }

        var concurrency = Math.Max(1, _options.MaxConcurrentDetailFetches);
        var initialWindowSize = Math.Min(concurrency, targets.Count);
        var succeeded = 0;
        var failures = new List<ScrapeRunIssueDetails>();

        var initialWindowIssues = await Task.WhenAll(
            targets.Take(initialWindowSize).Select(target => FetchOne(target, ct)));
        Collect(initialWindowIssues, ref succeeded, failures);

        var remaining = targets.Skip(initialWindowSize).ToList();
        if (succeeded > 0 && remaining.Count > 0)
        {
            var remainingIssues = await FetchWithSlidingWindow(remaining, concurrency, ct);
            Collect(remainingIssues, ref succeeded, failures);
        }

        return new DetailBacklogTickResult(targets.Count, succeeded + failures.Count, succeeded, failures);
    }

    private static void Collect(
        IReadOnlyList<ScrapeRunIssueDetails?> issues, ref int succeeded, List<ScrapeRunIssueDetails> failures)
    {
        foreach (var issue in issues)
        {
            if (issue is null)
            {
                succeeded++;
            }
            else
            {
                failures.Add(issue);
            }
        }
    }

    private async Task<IReadOnlyList<ScrapeRunIssueDetails?>> FetchWithSlidingWindow(
        IReadOnlyList<ListingDetailTarget> targets, int concurrency, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(concurrency);
        return await Task.WhenAll(targets.Select(target => FetchThrottled(target, gate, ct)));
    }

    private async Task<ScrapeRunIssueDetails?> FetchThrottled(
        ListingDetailTarget target, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await FetchOne(target, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private Task<ScrapeRunIssueDetails?> FetchOne(ListingDetailTarget target, CancellationToken ct)
    {
        _throttle.RecordFetchAttempt();
        return _detailFetch.FetchListingDetail(target, ct);
    }
}
