using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Services;

public sealed class EbayItemPageParserService : IItemPageParser
{
    public Marketplace Marketplace => Marketplace.Ebay;

    public ItemPageListing? Parse(string html) => EbayItemPageParser.Parse(html);
}
