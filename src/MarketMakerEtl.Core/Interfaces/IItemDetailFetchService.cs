using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Core.Interfaces;

public interface IItemDetailFetchService
{
    Task<IReadOnlyList<ScrapeRunIssueDetails>> FetchDetails(int jobId, CancellationToken ct);

    Task<ScrapeRunIssueDetails?> FetchListingDetail(ListingDetailTarget target, CancellationToken ct);

    Task ApplyBackfilledDetails(
        int jobId, IReadOnlyDictionary<string, ItemPageListing> detailsByListingId, CancellationToken ct);
}
