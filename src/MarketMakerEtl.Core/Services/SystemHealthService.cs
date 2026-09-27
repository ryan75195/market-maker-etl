using System.Data.Common;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Health;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Services;

public sealed class SystemHealthService : ISystemHealthService
{
    private readonly IJobHealthService _jobHealth;
    private readonly IFamilyBacklogHealthService _familyHealth;
    private readonly IFetcherHealthService _fetcherHealth;
    private readonly ILlmHealthService _llmHealth;

    public SystemHealthService(
        IJobHealthService jobHealth,
        IFamilyBacklogHealthService familyHealth,
        IFetcherHealthService fetcherHealth,
        ILlmHealthService llmHealth)
    {
        _jobHealth = jobHealth;
        _familyHealth = familyHealth;
        _fetcherHealth = fetcherHealth;
        _llmHealth = llmHealth;
    }

    public async Task<SystemHealthResponse> GetHealth(CancellationToken ct)
    {
        var database = await LoadDatabaseHealth(ct);
        var fetcher = await _fetcherHealth.GetFetcherHealth(ct);
        var llm = await _llmHealth.GetLlmHealth(ct);
        var status = ComputeStatus(database, fetcher, llm);

        return new SystemHealthResponse(
            status,
            database.Reachable,
            database.Jobs,
            fetcher,
            llm,
            new SystemHealthBacklogs(BuildClassificationBacklogs(database.Families), database.PendingDetailFetch),
            BuildReview(database.Families),
            BuildFamilyStates(database.Families));
    }

    private async Task<DatabaseHealthSnapshot> LoadDatabaseHealth(CancellationToken ct)
    {
        try
        {
            var jobs = await _jobHealth.GetJobHealth(ct);
            var families = await _familyHealth.GetFamilyBacklogHealth(ct);
            var pendingDetailFetch = await _jobHealth.GetPendingDetailFetchCount(ct);
            return new DatabaseHealthSnapshot(true, jobs, families, pendingDetailFetch);
        }
        catch (DbException)
        {
            return new DatabaseHealthSnapshot(false, [], [], 0);
        }
    }

    private static SystemHealthStatus ComputeStatus(
        DatabaseHealthSnapshot database, FetcherHealthView fetcher, LlmHealthView llm)
    {
        if (!database.Reachable)
        {
            return SystemHealthStatus.Degraded;
        }

        if (fetcher.IsDegraded)
        {
            return SystemHealthStatus.Degraded;
        }

        if (llm.Degraded && database.Families.Count > 0)
        {
            return SystemHealthStatus.Degraded;
        }

        if (llm.BudgetExhausted)
        {
            return SystemHealthStatus.Degraded;
        }

        if (database.Families.Any(family => family.State == FamilyState.Draft && family.HasEnabledScrapeJob))
        {
            return SystemHealthStatus.Degraded;
        }

        var hasUnhealthyJob = database.Jobs.Any(
            job => job.IsStale || job.LastRunStatus == ScrapeRunStatus.Failed);

        return hasUnhealthyJob ? SystemHealthStatus.Degraded : SystemHealthStatus.Ok;
    }

    private static IReadOnlyList<ClassificationBacklogView> BuildClassificationBacklogs(
        IReadOnlyList<FamilyBacklogHealthView> families) =>
        families
            .Select(f => new ClassificationBacklogView(f.FamilyId, f.FamilyKey, f.PendingClassificationCount))
            .ToList();

    private static IReadOnlyList<FamilyReviewView> BuildReview(IReadOnlyList<FamilyBacklogHealthView> families) =>
        families
            .Select(f => new FamilyReviewView(f.FamilyId, f.FamilyKey, f.NeedsReviewCount))
            .ToList();

    private static IReadOnlyList<FamilyStateView> BuildFamilyStates(IReadOnlyList<FamilyBacklogHealthView> families) =>
        families
            .Select(f => new FamilyStateView(f.FamilyId, f.FamilyKey, f.State))
            .ToList();

    private sealed record DatabaseHealthSnapshot(
        bool Reachable,
        IReadOnlyList<JobHealthView> Jobs,
        IReadOnlyList<FamilyBacklogHealthView> Families,
        int PendingDetailFetch);
}
