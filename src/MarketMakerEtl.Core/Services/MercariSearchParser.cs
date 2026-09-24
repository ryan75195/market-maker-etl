using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Services;

public sealed class MercariSearchParser : ISearchPageParser
{
    private const string CardSelector = "div[data-testid=\"ItemContainer\"]";
    private const string TileSelector = "[data-itemstatus], [data-itemprice]";
    private const string TitleSelector = "[data-testid=\"ItemName\"]";
    private const string TradingStatus = "trading";
    private const string SoldOutStatus = "sold_out";
    private const string CurrencyCode = "USD";

    private static readonly HtmlParser Parser = new();

    public Marketplace Marketplace => Marketplace.Mercari;

    public bool ContainsListingMarkup(string html) =>
        MercariSearchPayloadParser.IsPayload(html)
            ? !MercariSearchPayloadParser.IsEmptyResultSet(html)
            : html.Contains("ItemContainer", StringComparison.Ordinal);

    public SearchPageResult Parse(string html) =>
        MercariSearchPayloadParser.IsPayload(html)
            ? MercariSearchPayloadParser.Parse(html)
            : new SearchPageResult(ParseRenderedCards(html), TotalCount: null);

    private static List<ListingSummary> ParseRenderedCards(string html)
    {
        var document = Parser.ParseDocument(html);
        var cards = document.QuerySelectorAll(CardSelector);
        var summaries = new List<ListingSummary>();

        foreach (var card in cards)
        {
            summaries.Add(BuildSummary(card, card.Closest(TileSelector) ?? card));
        }

        return summaries;
    }

    private static ListingSummary BuildSummary(IElement card, IElement tile)
    {
        var listingId = card.GetAttribute("data-productid") ?? string.Empty;

        return new(
            ListingId: listingId,
            Title: ExtractTitle(card),
            Price: ExtractPrice(tile),
            Currency: CurrencyCode,
            Url: listingId.Length == 0 ? null : MercariItemUrl.Build(listingId),
            IsSold: IsSold(tile),
            Condition: null,
            PrimaryImageUrl: card.QuerySelector("img")?.GetAttribute("src"),
            BuyingFormat: null,
            Brand: card.GetAttribute("data-brand"));
    }

    private static string? ExtractTitle(IElement card)
    {
        var title = card.QuerySelector(TitleSelector)?.TextContent.Trim();
        return string.IsNullOrEmpty(title) ? null : title;
    }

    private static decimal? ExtractPrice(IElement tile)
    {
        var raw = tile.GetAttribute("data-itemprice");
        return decimal.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minorUnits)
            ? minorUnits / 100m
            : null;
    }

    private static bool IsSold(IElement tile)
    {
        var status = tile.GetAttribute("data-itemstatus");
        return string.Equals(status, TradingStatus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, SoldOutStatus, StringComparison.OrdinalIgnoreCase);
    }
}
