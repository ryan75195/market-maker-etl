using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

public sealed class EbaySearchParser : ISearchPageParser
{
    private const string ItemMarker = "/itm/";

    private static readonly HtmlParser Parser = new();

    public IReadOnlyList<ListingSummary> Parse(string html)
    {
        var document = Parser.ParseDocument(html);
        var items = SelectItems(document);
        var summaries = new List<ListingSummary>();

        foreach (var item in items)
        {
            var summary = BuildSummary(item);
            if (summary is not null)
            {
                summaries.Add(summary);
            }
        }

        return summaries;
    }

    private static IHtmlCollection<IElement> SelectItems(IDocument document)
    {
        var cards = document.QuerySelectorAll("li.s-card[data-viewport]");
        return cards.Length > 0 ? cards : document.QuerySelectorAll("li.s-item");
    }

    private static ListingSummary? BuildSummary(IElement item)
    {
        var url = ExtractUrl(item);
        var id = ExtractId(url);

        if (id is null)
        {
            return null;
        }

        return new ListingSummary(
            ListingId: id,
            Title: ExtractTitle(item),
            Price: ExtractPrice(item),
            Currency: ExtractCurrency(item),
            Url: url,
            IsSold: IsSold(item),
            Condition: ExtractCondition(item),
            PrimaryImageUrl: ExtractPrimaryImageUrl(item),
            BuyingFormat: ExtractBuyingFormat(item));
    }

    private static string? ExtractUrl(IElement item)
    {
        var href = item.QuerySelector("a.s-card__link")?.GetAttribute("href")
            ?? item.QuerySelector("a[href*='/itm/']")?.GetAttribute("href")
            ?? item.QuerySelector(".s-item__link")?.GetAttribute("href");

        return href?.Split('?')[0];
    }

    private static string? ExtractId(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.Contains(ItemMarker, StringComparison.Ordinal))
        {
            return null;
        }

        var id = url.Split(ItemMarker)[1];
        return id.Length >= 10 && id.All(char.IsDigit) ? id : null;
    }

    private static string? ExtractTitle(IElement item)
    {
        var title = item.QuerySelector(".s-card__title")?.TextContent
            ?? item.QuerySelector(".s-item__title [role=\"heading\"]")?.TextContent;

        return title?.Replace("Opens in a new window or tab", string.Empty, StringComparison.Ordinal)
            .Replace("New listing", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static bool IsSold(IElement item)
    {
        var tag = item.QuerySelector(".s-item__title--tagblock, .POSITIVE, [class*='sold']");
        return tag is not null
            && tag.TextContent.Contains("Sold", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractCondition(IElement item)
    {
        var text = item.QuerySelector(".s-card__condition")?.TextContent
            ?? item.QuerySelector(".s-item__condition")?.TextContent;

        return NormaliseOptional(text);
    }

    private static string? ExtractPrimaryImageUrl(IElement item)
    {
        var src = item.QuerySelector("img.s-card__image")?.GetAttribute("src")
            ?? item.QuerySelector("img.s-item__image-img")?.GetAttribute("src");

        return NormaliseOptional(src);
    }

    private static string? ExtractBuyingFormat(IElement item)
    {
        var text = item.QuerySelector(".s-card__buying-format")?.TextContent
            ?? item.QuerySelector(".s-item__buying-format")?.TextContent;

        return NormaliseOptional(text);
    }

    private static string? NormaliseOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static decimal? ExtractPrice(IElement item)
    {
        var text = item.QuerySelector(".s-card__price")?.TextContent
            ?? item.QuerySelector(".s-item__price")?.TextContent;

        var digits = text is null
            ? string.Empty
            : new string(text.Where(c => char.IsDigit(c) || c is '.' or ',').ToArray());

        if (digits.Length == 0)
        {
            return null;
        }

        var normalised = Normalise(digits);
        return decimal.TryParse(normalised, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string Normalise(string amount)
    {
        if (amount.Count(c => c == ',') == 1 && !amount.Contains('.', StringComparison.Ordinal))
        {
            return amount.Replace(',', '.');
        }

        return amount.Replace(",", string.Empty, StringComparison.Ordinal);
    }

    private static string? ExtractCurrency(IElement item)
    {
        var text = item.QuerySelector(".s-card__price")?.TextContent
            ?? item.QuerySelector(".s-item__price")?.TextContent;

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var prefix = new string(text.TakeWhile(c => !char.IsDigit(c)).ToArray()).Trim();
        return prefix.Length == 0 ? null : prefix;
    }
}
