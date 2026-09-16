using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Ebay;
using MarketMakerEtl.Core.Models.Marketplaces;

namespace MarketMakerEtl.Core.Services;

public sealed class MercariItemPageParser : IItemPageParser
{
    private const string ActiveStatus = "Active";
    private const string SoldStatus = "Sold";
    private const string TitleSelector = "[data-testid='ItemName']";
    private const string PriceSelector = "[data-testid='ItemPrice']";
    private const string ConditionSelector = "[data-testid='ItemCondition']";
    private const string BrandSelector = "[data-testid='ItemBrand']";
    private const string SellerSelector = "[data-testid='ItemSeller']";
    private const string ImageSelector = "[data-testid='ItemImage']";
    private const string SoldBannerSelector = "[data-testid='ItemSoldBanner']";
    private const string SoldBadgeSelector = "[data-testid='ItemSoldBadge']";

    private static readonly HtmlParser Parser = new();

    public Marketplace Marketplace => Marketplace.Mercari;

    public ItemPageListing? Parse(string html)
    {
        var document = Parser.ParseDocument(html);
        var title = ExtractText(document, TitleSelector);

        if (title is null)
        {
            return null;
        }

        var priceText = ExtractText(document, PriceSelector);

        return new ItemPageListing(
            ListingId: null,
            Title: title,
            Price: ParsePrice(priceText),
            Currency: ExtractCurrency(priceText),
            Condition: ExtractText(document, ConditionSelector),
            BuyingFormat: null,
            Status: ExtractStatus(document),
            SoldPrice: null,
            SoldDate: null,
            Seller: ExtractText(document, SellerSelector),
            PrimaryImageUrl: ExtractImageUrl(document),
            Brand: ExtractText(document, BrandSelector));
    }

    private static string ExtractStatus(IParentNode parent)
    {
        var sold = parent.QuerySelector(SoldBannerSelector) is not null
            || parent.QuerySelector(SoldBadgeSelector) is not null;

        return sold ? SoldStatus : ActiveStatus;
    }

    private static string? ExtractText(IParentNode parent, string selector) =>
        NormaliseOptional(parent.QuerySelector(selector)?.TextContent);

    private static string? ExtractImageUrl(IParentNode parent) =>
        NormaliseOptional(parent.QuerySelector(ImageSelector)?.GetAttribute("src"));

    private static decimal? ParsePrice(string? text)
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
