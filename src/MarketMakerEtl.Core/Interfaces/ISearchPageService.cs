using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Interfaces;

public interface ISearchPageService
{
    Task<IReadOnlyList<ListingSummary>> Collect(
        string searchTerm,
        Marketplace marketplace,
        IReadOnlySet<string> knownSoldListingIds,
        CancellationToken ct);
}
