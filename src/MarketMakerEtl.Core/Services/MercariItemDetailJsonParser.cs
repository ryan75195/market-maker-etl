using System.Globalization;
using System.Text.Json;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

internal static class MercariItemDetailJsonParser
{
    private const string CurrencyCode = "USD";
    private const string ActiveState = "on_sale";
    private const string TradingState = "trading";
    private const string SoldOutState = "sold_out";
    private const string ActiveStatus = "Active";
    private const string SoldStatus = "Sold";
    private const string EndedStatus = "Ended";
    private const string SellerPayerCode = "seller";
    private const string SoldDateFormat = "yyyy-MM-ddTHH:mm:ssZ";
    private const string RefPropertyName = "__ref";

    public static bool TryParse(string apiJson, out ItemPageListing listing)
    {
        listing = null!;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(apiJson);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (!TryFindItem(document.RootElement, out var item))
            {
                return false;
            }

            listing = BuildListing(item);
            return true;
        }
    }

    private static bool TryFindItem(JsonElement root, out JsonElement item)
    {
        item = default;

        return root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("item", out item)
            && item.ValueKind == JsonValueKind.Object;
    }

    private static ItemPageListing BuildListing(JsonElement item)
    {
        var status = MapStatus(ReadString(item, "status"));
        var isSold = status == SoldStatus;
        var price = ReadCents(item, "price");
        var imageUrls = ReadImageUrls(item);

        return new ItemPageListing(
            ListingId: null,
            Title: ReadString(item, "name"),
            Price: price,
            Currency: CurrencyCode,
            Condition: ReadRefName(item, "itemCondition"),
            BuyingFormat: null,
            Status: status,
            SoldPrice: isSold ? price : null,
            SoldDate: isSold ? ReadDateText(item, "lastSoldAt") : null,
            Seller: ReadRefName(item, "seller"),
            PrimaryImageUrl: imageUrls.Count > 0 ? imageUrls[0] : null,
            Brand: ReadRefName(item, "brand"),
            Description: ReadString(item, "description"),
            ImageUrls: imageUrls,
            ShippingCost: ReadShippingCost(item),
            OriginalPrice: ReadCents(item, "originalPrice"),
            PostedUtc: ReadDateTimeOffset(item, "created"),
            Likes: ReadInt(item, "numLikes"),
            CategoryId: MercariItemDetailSegmentationReader.ReadRefInt(item, "itemCategory", "id"),
            CategoryHierarchy: MercariItemDetailSegmentationReader.ReadCategoryHierarchy(item),
            BrandId: MercariItemDetailSegmentationReader.ReadRefInt(item, "brand", "id"),
            ConditionId: MercariItemDetailSegmentationReader.ReadRefInt(item, "itemCondition", "id"),
            SizeName: ReadRefName(item, "itemSize"),
            ColorName: null,
            ShippingPayer: MercariItemDetailSegmentationReader.ReadRefString(item, "shippingPayer", "code"),
            ShipsFromState: ReadRefName(item, "shippingFromArea"),
            DiscountRatio: ReadInt(item, "discountRatio"),
            SellerProfile: MercariItemDetailSegmentationReader.ReadSellerProfile(item),
            Attributes: MercariItemDetailSegmentationReader.ReadAdditionalAttributes(item),
            RawJson: item.GetRawText());
    }

    private static string? MapStatus(string? state) =>
        state switch
        {
            null => null,
            ActiveState => ActiveStatus,
            TradingState or SoldOutState => SoldStatus,
            _ => EndedStatus,
        };

    private static decimal? ReadShippingCost(JsonElement item)
    {
        if (!TryResolveRef(item, "shippingPayer", out var payer))
        {
            return null;
        }

        if (string.Equals(ReadString(payer, "code"), SellerPayerCode, StringComparison.Ordinal))
        {
            return 0m;
        }

        return TryResolveRef(item, "shippingClass", out var shippingClass)
            ? ReadCents(shippingClass, "fee")
            : null;
    }

    private static string? ReadRefName(JsonElement item, string propertyName) =>
        TryResolveRef(item, propertyName, out var resolved) ? ReadString(resolved, "name") : null;

    internal static bool TryResolveRef(JsonElement item, string propertyName, out JsonElement resolved)
    {
        resolved = default;

        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (IsUnresolvableReference(value))
        {
            return false;
        }

        resolved = value;
        return true;
    }

    private static bool IsUnresolvableReference(JsonElement value) =>
        value.TryGetProperty(RefPropertyName, out var refProperty) && refProperty.ValueKind == JsonValueKind.String;

    private static IReadOnlyList<string> ReadImageUrls(JsonElement item)
    {
        if (!item.TryGetProperty("photos", out var photos) || photos.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var urls = new List<string>();
        foreach (var photo in photos.EnumerateArray())
        {
            var url = ReadString(photo, "imageUrl");
            if (url is not null)
            {
                urls.Add(url);
            }
        }

        return urls;
    }

    private static string? ReadDateText(JsonElement item, string propertyName)
    {
        var seconds = ReadUnixSeconds(item, propertyName);

        return seconds is { } value
            ? DateTimeOffset.FromUnixTimeSeconds(value).ToString(SoldDateFormat, CultureInfo.InvariantCulture)
            : null;
    }

    internal static DateTimeOffset? ReadDateTimeOffset(JsonElement item, string propertyName)
    {
        var seconds = ReadUnixSeconds(item, propertyName);

        return seconds is { } value ? DateTimeOffset.FromUnixTimeSeconds(value) : null;
    }

    private static long? ReadUnixSeconds(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var seconds)
            ? seconds
            : null;

    private static decimal? ReadCents(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDecimal(out var cents)
            ? cents / 100m
            : null;

    internal static int? ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var count)
            ? count
            : null;

    internal static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
