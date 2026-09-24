namespace MarketMakerEtl.Core.Interfaces;

public interface IJobSchedulingService
{
    Task<int> QueueDueJobs(CancellationToken ct);
}
