using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Services;

public sealed class MercariSearchParser : ISearchPageParser
{
    private const string UnrecognisedPayloadMessage = "Unrecognised search payload";

    public Marketplace Marketplace => Marketplace.Mercari;

    public bool ContainsListingMarkup(string html) =>
        MercariSearchPayloadParser.IsPayload(html) && !MercariSearchPayloadParser.IsEmptyResultSet(html);

    public SearchPageResult Parse(string html)
    {
        if (!MercariSearchPayloadParser.IsPayload(html) || !MercariSearchPayloadParser.HasSearchResult(html))
        {
            throw new UnrecognisedSearchPageException(UnrecognisedPayloadMessage);
        }

        return MercariSearchPayloadParser.Parse(html);
    }
}
