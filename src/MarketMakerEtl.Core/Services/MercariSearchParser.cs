using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Services;

public sealed class MercariSearchParser : ISearchPageParser
{
    public Marketplace Marketplace => Marketplace.Mercari;

    public bool ContainsListingMarkup(string html) =>
        throw new NotImplementedException();

    public IReadOnlyList<ListingSummary> Parse(string html) =>
        throw new NotImplementedException();
}
