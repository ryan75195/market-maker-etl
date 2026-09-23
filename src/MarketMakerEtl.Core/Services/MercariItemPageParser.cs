using AngleSharp.Html.Parser;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Services;

public sealed class MercariItemPageParser : IItemPageParser
{
    private const string NextDataSelector = "script#__NEXT_DATA__";

    private static readonly HtmlParser Parser = new();

    public Marketplace Marketplace => Marketplace.Mercari;

    public ItemPageListing? Parse(string html)
    {
        var document = Parser.ParseDocument(html);
        var nextDataJson = document.QuerySelector(NextDataSelector)?.TextContent;

        if (!string.IsNullOrWhiteSpace(nextDataJson)
            && MercariItemDetailJsonParser.TryParse(nextDataJson, out var listing))
        {
            return listing;
        }

        return MercariItemPageFallbackParser.Parse(document);
    }
}
