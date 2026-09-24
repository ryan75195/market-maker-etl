using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Interfaces;

public interface IScrapeStore
{
    Task<int> EnsureJob(string searchTerm, CancellationToken ct, Marketplace marketplace = Marketplace.Ebay);

    Task<int> EnqueueRun(int jobId, string searchTerm, TriggerType trigger, CancellationToken ct);

    Task<ScrapeRunWork?> ClaimNextQueuedRun(CancellationToken ct);

    Task CompleteRun(int runId, RunCompletionCounts counts, CancellationToken ct);

    Task FailRun(int runId, string error, CancellationToken ct);

    Task<ListingUpsertSummary> UpsertListings(int jobId, IReadOnlyList<ListingSummary> listings, CancellationToken ct);

    Task<ScrapeRunView?> GetRun(int runId, CancellationToken ct);

    Task<IReadOnlyList<ListingSummary>> GetListings(int jobId, CancellationToken ct);

    Task<IReadOnlyList<ListingRefreshTarget>> GetActiveListings(CancellationToken ct);

    Task RecordStatusChange(int listingEntityId, ListingStatusObservation observation, CancellationToken ct);
}
