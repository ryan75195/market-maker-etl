using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Interfaces;

public interface IItemPageParser
{
    ItemPageListing? Parse(string html);
}
