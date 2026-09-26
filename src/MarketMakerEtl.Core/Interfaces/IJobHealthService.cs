using MarketMakerEtl.Core.Models.Health;

namespace MarketMakerEtl.Core.Interfaces;

public interface IJobHealthService
{
    Task<IReadOnlyList<JobHealthView>> GetJobHealth(CancellationToken ct);

    Task<int> GetPendingDetailFetchCount(CancellationToken ct);
}
