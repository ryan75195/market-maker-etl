using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Interfaces;

public interface ISearchPageService
{
    Task<IReadOnlyList<ListingSummary>> Collect(string searchTerm, CancellationToken ct);
}
