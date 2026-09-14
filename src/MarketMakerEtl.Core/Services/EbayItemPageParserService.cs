using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

public sealed class EbayItemPageParserService : IItemPageParser
{
    public ItemPageListing? Parse(string html) => EbayItemPageParser.Parse(html);
}
