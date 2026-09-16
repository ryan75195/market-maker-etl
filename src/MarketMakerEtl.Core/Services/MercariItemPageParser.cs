using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Services;

public sealed class MercariItemPageParser : IItemPageParser
{
    public Marketplace Marketplace => Marketplace.Mercari;

    public ItemPageListing? Parse(string html) => throw new NotImplementedException(nameof(html));
}
