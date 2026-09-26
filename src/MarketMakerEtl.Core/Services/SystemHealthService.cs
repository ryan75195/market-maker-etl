using System.Data.Common;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Health;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Services;

public sealed class SystemHealthService : ISystemHealthService
{
    private readonly IJobHealthService _jobHealth;
    private readonly IFamilyBacklogHealthService _familyHealth;
    private readonly IListingClassifierClient _classifierClient;

    public SystemHealthService(
        IJobHealthService jobHealth,
        IFamilyBacklogHealthService familyHealth,
        IListingClassifierClient classifierClient)
    {
        _jobHealth = jobHealth;
        _familyHealth = familyHealth;
        _classifierClient = classifierClient;
    }

    public async Task<SystemHealthResponse> GetHealth(CancellationToken ct)
    {
        var database = await LoadDatabaseHealth(ct);
        var classifier = await _classifierClient.CheckHealth(ct);
        var status = ComputeStatus(database, classifier);

        return new SystemHealthResponse(
            status,
            database.Reachable,
            database.Jobs,
            classifier,
            new SystemHealthBacklogs(BuildClassificationBacklogs(database.Families), database.PendingDetailFetch),
            BuildReview(database.Families));
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
        DatabaseHealthSnapshot database, ClassifierHealthCheckResult classifier)
    {
        if (!database.Reachable)
        {
            return SystemHealthStatus.Degraded;
        }

        if (!classifier.Reachable && database.Families.Count > 0)
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

    private sealed record DatabaseHealthSnapshot(
        bool Reachable,
        IReadOnlyList<JobHealthView> Jobs,
        IReadOnlyList<FamilyBacklogHealthView> Families,
        int PendingDetailFetch);
}
