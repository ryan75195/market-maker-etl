using System.Globalization;
using AngleSharp.Dom;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

internal static class MercariItemPageFallbackParser
{
    private const string ActiveStatus = "Active";
    private const string SoldStatus = "Sold";
    private const string TitleSelector = "h1[data-testid='ItemName']";
    private const string PriceSelector = "[data-testid='ItemPrice']";
    private const string ConditionSelector = "[data-testid='ItemDetailsCondition']";
    private const string BrandSelector = "[data-testid='ItemDetailsBrand']";
    private const string SellerSelector = "[data-testid='ItemDetailsSellerName']";
    private const string ShippingSelector = "[data-testid='ItemDetailsShipping']";
    private const string DescriptionSelector = "[data-testid='ItemDetailsDescription']";
    private const string PostedSelector = "[data-testid='ItemDetailsPosted']";
    private const string SoldMarkerSelector = "[data-testid='SoldListing']";
    private const string PostedDateFormat = "MM/dd/yy";
    private const string FreeShippingMarker = "Free";

    public static ItemPageListing? Parse(IParentNode document)
    {
        var title = ExtractText(document, TitleSelector);
        if (title is null)
        {
            return null;
        }

        var priceText = ExtractText(document, PriceSelector);

        return new ItemPageListing(
            ListingId: null,
            Title: title,
            Price: ParseAmount(priceText),
            Currency: ExtractCurrency(priceText),
            Condition: ExtractText(document, ConditionSelector),
            BuyingFormat: null,
            Status: document.QuerySelector(SoldMarkerSelector) is null ? ActiveStatus : SoldStatus,
            SoldPrice: null,
            SoldDate: null,
            Seller: ExtractText(document, SellerSelector),
            PrimaryImageUrl: null,
            Brand: ExtractText(document, BrandSelector),
            Description: ExtractText(document, DescriptionSelector),
            ImageUrls: null,
            ShippingCost: ExtractShippingCost(document),
            OriginalPrice: null,
            PostedUtc: ExtractPostedDate(document),
            Likes: null);
    }

    private static decimal? ExtractShippingCost(IParentNode document)
    {
        var text = ExtractText(document, ShippingSelector);
        if (text is null)
        {
            return null;
        }

        if (text.Contains(FreeShippingMarker, StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        var firstToken = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return ParseAmount(firstToken);
    }

    private static DateTimeOffset? ExtractPostedDate(IParentNode document)
    {
        var text = ExtractText(document, PostedSelector);
        if (text is null)
        {
            return null;
        }

        var parsed = DateTime.TryParseExact(
            text, PostedDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);

        return parsed ? new DateTimeOffset(date, TimeSpan.Zero) : null;
    }

    private static string? ExtractText(IParentNode parent, string selector) =>
        NormaliseOptional(parent.QuerySelector(selector)?.TextContent);

    private static decimal? ParseAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var digits = new string(text.Where(c => char.IsDigit(c) || c == '.').ToArray());

        return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
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

    private static string? NormaliseOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
