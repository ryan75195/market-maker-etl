using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Core.Models.Scraper;

public sealed record MarketplaceAdapters(
    IEnumerable<IEbaySearchUrlService> UrlServices,
    IEnumerable<ISearchPageParser> SearchParsers,
    IEnumerable<IItemPageParser> ItemParsers);
