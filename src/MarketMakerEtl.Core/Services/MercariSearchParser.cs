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
    private const string TitleSelector = "[data-testid=\"ItemName\"]";
    private const string SoldStatus = "trading";
    private const string CurrencyCode = "USD";

    private static readonly HtmlParser Parser = new();

    public Marketplace Marketplace => Marketplace.Mercari;

    public bool ContainsListingMarkup(string html) =>
        html.Contains("ItemContainer", StringComparison.Ordinal);

    public IReadOnlyList<ListingSummary> Parse(string html)
    {
        var document = Parser.ParseDocument(html);
        var cards = document.QuerySelectorAll(CardSelector);
        var summaries = new List<ListingSummary>();

        foreach (var card in cards)
        {
            summaries.Add(BuildSummary(card));
        }

        return summaries;
    }

    private static ListingSummary BuildSummary(IElement card) =>
        new(
            ListingId: card.GetAttribute("data-productid") ?? string.Empty,
            Title: ExtractTitle(card),
            Price: ExtractPrice(card),
            Currency: CurrencyCode,
            Url: card.QuerySelector("a[href]")?.GetAttribute("href"),
            IsSold: IsSold(card),
            Condition: null,
            PrimaryImageUrl: card.QuerySelector("img")?.GetAttribute("src"),
            BuyingFormat: null,
            Brand: card.GetAttribute("data-brand"));

    private static string? ExtractTitle(IElement card)
    {
        var title = card.QuerySelector(TitleSelector)?.TextContent.Trim();
        return string.IsNullOrEmpty(title) ? null : title;
    }

    private static decimal? ExtractPrice(IElement card)
    {
        var raw = card.GetAttribute("data-itemprice");
        return decimal.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minorUnits)
            ? minorUnits / 100m
            : null;
    }

    private static bool IsSold(IElement card) =>
        string.Equals(card.GetAttribute("data-itemstatus"), SoldStatus, StringComparison.OrdinalIgnoreCase);
}
