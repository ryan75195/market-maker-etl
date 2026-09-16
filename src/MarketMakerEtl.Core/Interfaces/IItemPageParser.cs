using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Interfaces;

public interface IItemPageParser
{
    Marketplace Marketplace { get; }

    ItemPageListing? Parse(string html);
}
