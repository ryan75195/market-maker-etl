using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Interfaces;

public interface IEbaySearchUrlService
{
    Marketplace Marketplace { get; }

    bool SupportsPagination { get; }

    string BuildSearch(string searchTerm, bool sold, int page);
}
