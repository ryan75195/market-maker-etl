using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Services;

public sealed class MercariSearchUrlService : IEbaySearchUrlService
{
    public Marketplace Marketplace => Marketplace.Mercari;

    public bool SupportsPagination => false;

    public string BuildSearch(string searchTerm, bool sold, int page) =>
        throw new NotImplementedException();

    public string BuildSearch(MercariSearchRequest request) =>
        throw new NotImplementedException();
}
