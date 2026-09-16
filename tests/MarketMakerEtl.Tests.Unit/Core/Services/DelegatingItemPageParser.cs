using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

internal sealed class DelegatingItemPageParser : IItemPageParser
{
    public Marketplace Marketplace => Marketplace.Ebay;

    public ItemPageListing? Parse(string html) => EbayItemPageParser.Parse(html);
}
