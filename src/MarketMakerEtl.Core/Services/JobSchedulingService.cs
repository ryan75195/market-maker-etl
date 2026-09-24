using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Services;

public sealed class JobSchedulingService : IJobSchedulingService
{
    private readonly IJobStore _jobs;
    private readonly IScrapeStore _scrapeStore;
    private readonly TimeProvider _timeProvider;

    public JobSchedulingService(IJobStore jobs, IScrapeStore scrapeStore, TimeProvider timeProvider)
    {
        _jobs = jobs;
        _scrapeStore = scrapeStore;
        _timeProvider = timeProvider;
    }

    public async Task<int> QueueDueJobs(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var jobs = await _jobs.GetEffectivelyEnabledJobs(ct);
        var queuedCount = 0;

        foreach (var job in jobs)
        {
            if (!IsDue(job, now))
            {
                continue;
            }

            if (await _jobs.HasQueuedOrRunningRun(job.Id, ct))
            {
                continue;
            }

            await _scrapeStore.EnqueueRun(job.Id, job.SearchTerm, TriggerType.Scheduled, ct);
            await _jobs.MarkQueued(job.Id, now, ct);
            queuedCount++;
        }

        return queuedCount;
    }

    private static bool IsDue(JobView job, DateTime now) =>
        job.LastQueuedUtc is null || job.LastQueuedUtc.Value.AddHours(job.IntervalHours) <= now;
}
