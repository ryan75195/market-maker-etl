using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Interfaces;

public interface ISearchPageParser
{
    Marketplace Marketplace { get; }

    bool ContainsListingMarkup(string html);

    SearchPageResult Parse(string html);
}
