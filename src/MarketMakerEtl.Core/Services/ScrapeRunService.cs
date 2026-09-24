using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Services;

public sealed class ScrapeRunService : IScrapeRunService
{
    private readonly ISearchPageService _search;
    private readonly IScrapeStore _store;
    private readonly IItemDetailFetchService _detailFetch;

    public ScrapeRunService(ISearchPageService search, IScrapeStore store, IItemDetailFetchService detailFetch)
    {
        _search = search;
        _store = store;
        _detailFetch = detailFetch;
    }

    public async Task Run(ScrapeRunWork work, CancellationToken ct)
    {
        try
        {
            var existingListings = await _store.GetListings(work.JobId, ct);
            var knownSoldListingIds = existingListings
                .Where(listing => listing.IsSold)
                .Select(listing => listing.ListingId)
                .ToHashSet(StringComparer.Ordinal);
            var listings = await _search.Collect(work.SearchTerm, work.Marketplace, knownSoldListingIds, ct);
            await _store.UpsertListings(work.JobId, listings, ct);
            await _detailFetch.FetchDetails(work.JobId, ct);
            await _store.CompleteRun(work.RunId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _store.FailRun(work.RunId, ex.Message, ct);
        }
    }
}
