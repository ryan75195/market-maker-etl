using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

internal static class EbayItemPageParser
{
    private const string TitleSelector = "h1.x-item-title__mainTitle";
    private const string StatusBadgeSelector = "div.x-photos-cvip > span.ux-textspans";
    private const string PrimaryPriceSelector = ".x-price-primary__price";
    private const string AuctionSelector = ".x-bid-price";
    private const string BuyItNowSelector = ".x-bin-price";
    private const string ConditionSelector = ".x-item-condition-text";
    private const string SoldPriceSelector = ".x-item-condensed-card__sold-price";
    private const string StatusMessageSelector = ".d-top-panel-message";
    private const string SellerSelector = ".x-seller-info, [data-testid='x-seller-info']";
    private const string ImageSelector = "#icImg, .ux-image-carousel-item img, .x-photos-cvip img";
    private const string ItemMarker = "/itm/";

    private static readonly HtmlParser Parser = new();

    public static ItemPageListing? Parse(string html)
    {
        var document = Parser.ParseDocument(html);
        var title = ExtractText(document, TitleSelector);

        if (title is null)
        {
            return null;
        }

        var priceText = ExtractText(document, PrimaryPriceSelector);

        return new ItemPageListing(
            ListingId: ExtractListingId(document),
            Title: title,
            Price: ExtractPositivePrice(priceText),
            Currency: ExtractCurrency(priceText),
            Condition: ExtractText(document, ConditionSelector),
            BuyingFormat: ExtractBuyingFormat(document),
            Status: ExtractStatus(document),
            SoldPrice: ExtractPositivePrice(ExtractText(document, SoldPriceSelector)),
            SoldDate: ExtractSoldDate(document),
            Seller: ExtractSeller(document),
            PrimaryImageUrl: ExtractImageUrl(document));
    }

    private static string? ExtractText(IParentNode parent, string selector)
    {
        return NormaliseOptional(parent.QuerySelector(selector)?.TextContent);
    }

    private static string? ExtractBuyingFormat(IParentNode parent)
    {
        if (parent.QuerySelector(AuctionSelector) is not null)
        {
            return "Auction";
        }

        return parent.QuerySelector(BuyItNowSelector) is not null ? "Buy It Now" : null;
    }

    private static string? ExtractStatus(IParentNode parent)
    {
        var badge = ExtractText(parent, StatusBadgeSelector)?.ToUpperInvariant();

        return badge switch
        {
            "SOLD" => "Sold",
            "ENDED" => "Ended",
            _ => "Active",
        };
    }

    private static decimal? ExtractPositivePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var digits = new string(text.Where(c => char.IsDigit(c) || c is '.' or ',').ToArray());
        if (digits.Length == 0)
        {
            return null;
        }

        var normalised = Normalise(digits);
        if (!decimal.TryParse(normalised, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return value == 0m ? null : value;
    }

    private static string Normalise(string amount)
    {
        if (amount.Count(c => c == ',') == 1 && !amount.Contains('.', StringComparison.Ordinal))
        {
            return amount.Replace(',', '.');
        }

        return amount.Replace(",", string.Empty, StringComparison.Ordinal);
    }

    private static string? ExtractCurrency(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var prefix = new string(text.TakeWhile(c => !char.IsDigit(c)).ToArray()).Trim();
        return prefix.Length == 0 ? null : prefix;
    }

    private static string? ExtractSoldDate(IParentNode parent)
    {
        var message = ExtractText(parent, StatusMessageSelector);
        if (message is null)
        {
            return null;
        }

        var marker = "sold on ";
        var index = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var date = index >= 0 ? message[(index + marker.Length)..] : message;

        return NormaliseOptional(date.TrimEnd('.'));
    }

    private static string? ExtractListingId(IDocument document)
    {
        var href = document.QuerySelector("link[rel='canonical']")?.GetAttribute("href")
            ?? document.QuerySelector("a[href*='/itm/']")?.GetAttribute("href");

        if (href is null)
        {
            return null;
        }

        var markerIndex = href.IndexOf(ItemMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var id = href[(markerIndex + ItemMarker.Length)..].Split('?')[0].Trim('/');
        return id.Length >= 10 && id.All(char.IsDigit) ? id : null;
    }

    private static string? ExtractSeller(IParentNode parent)
    {
        var text = ExtractText(parent, SellerSelector);
        if (text is null)
        {
            return null;
        }

        var marker = "Sold by:";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return NormaliseOptional(index >= 0 ? text[(index + marker.Length)..] : text);
    }

    private static string? ExtractImageUrl(IParentNode parent)
    {
        var src = parent.QuerySelector(ImageSelector)?.GetAttribute("src")
            ?? parent.QuerySelector(ImageSelector)?.GetAttribute("data-src");

        return NormaliseOptional(src);
    }

    private static string? NormaliseOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
