using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Interfaces;

public interface IScrapeStore
{
    Task<int> EnsureJob(string searchTerm, CancellationToken ct);

    Task<int> EnqueueRun(int jobId, string searchTerm, CancellationToken ct);

    Task<ScrapeRunWork?> ClaimNextQueuedRun(CancellationToken ct);

    Task CompleteRun(int runId, CancellationToken ct);

    Task FailRun(int runId, string error, CancellationToken ct);

    Task UpsertListings(int jobId, IReadOnlyList<ListingSummary> listings, CancellationToken ct);

    Task<ScrapeRunView?> GetRun(int runId, CancellationToken ct);

    Task<IReadOnlyList<ListingSummary>> GetListings(int jobId, CancellationToken ct);
}
