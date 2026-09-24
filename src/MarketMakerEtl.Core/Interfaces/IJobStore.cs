using MarketMakerEtl.Core.Models.Jobs;

namespace MarketMakerEtl.Core.Interfaces;

public interface IJobStore
{
    Task<JobView> CreateJob(JobDetails details, CancellationToken ct);

    Task<IReadOnlyList<JobView>> GetJobs(CancellationToken ct);

    Task<JobView?> GetJob(int jobId, CancellationToken ct);

    Task<JobView?> UpdateJob(int jobId, JobDetails details, CancellationToken ct);

    Task<bool> DeleteJob(int jobId, CancellationToken ct);

    Task<JobView?> SetJobEnabled(int jobId, bool isEnabled, CancellationToken ct);

    Task<JobView?> SetJobCategories(int jobId, IReadOnlyList<int> categoryIds, CancellationToken ct);

    Task<JobView?> MarkQueued(int jobId, DateTime queuedUtc, CancellationToken ct);

    Task<IReadOnlyList<JobView>> GetEffectivelyEnabledJobs(CancellationToken ct);

    Task<bool> HasQueuedOrRunningRun(int jobId, CancellationToken ct);
}
