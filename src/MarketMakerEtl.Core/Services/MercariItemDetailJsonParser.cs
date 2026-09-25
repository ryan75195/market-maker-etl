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
    private const string ItemDetailTypeName = "ItemDetail";
    private const string SoldDateFormat = "yyyy-MM-ddTHH:mm:ssZ";
    private const string RefPropertyName = "__ref";

    public static bool TryParse(string nextDataJson, out ItemPageListing listing)
    {
        listing = null!;

        using var document = JsonDocument.Parse(nextDataJson);

        if (!TryFindServerState(document.RootElement, out var serverState)
            || !TryFindItemDetail(serverState, out var item))
        {
            return false;
        }

        listing = BuildListing(serverState, item);
        return true;
    }

    private static bool TryFindServerState(JsonElement root, out JsonElement serverState)
    {
        serverState = default;

        return root.TryGetProperty("props", out var props)
            && props.TryGetProperty("pageProps", out var pageProps)
            && pageProps.TryGetProperty("serverState", out serverState)
            && serverState.ValueKind == JsonValueKind.Object;
    }

    private static bool TryFindItemDetail(JsonElement serverState, out JsonElement item)
    {
        foreach (var property in serverState.EnumerateObject())
        {
            if (IsTypeName(property.Value, ItemDetailTypeName))
            {
                item = property.Value;
                return true;
            }
        }

        item = default;
        return false;
    }

    private static bool IsTypeName(JsonElement value, string typeName) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty("__typename", out var typeProperty)
        && typeProperty.ValueKind == JsonValueKind.String
        && typeProperty.GetString() == typeName;

    private static ItemPageListing BuildListing(JsonElement serverState, JsonElement item)
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
            Condition: ReadRefName(serverState, item, "itemCondition"),
            BuyingFormat: null,
            Status: status,
            SoldPrice: isSold ? price : null,
            SoldDate: isSold ? ReadDateText(item, "lastSoldAt") : null,
            Seller: ReadRefName(serverState, item, "seller"),
            PrimaryImageUrl: imageUrls.Count > 0 ? imageUrls[0] : null,
            Brand: ReadRefName(serverState, item, "brand"),
            Description: ReadString(item, "description"),
            ImageUrls: imageUrls,
            ShippingCost: ReadShippingCost(serverState, item),
            OriginalPrice: ReadCents(item, "originalPrice"),
            PostedUtc: ReadDateTimeOffset(item, "created"),
            Likes: ReadInt(item, "numLikes"),
            CategoryId: MercariItemDetailSegmentationReader.ReadRefInt(serverState, item, "itemCategory", "id"),
            CategoryHierarchy: MercariItemDetailSegmentationReader.ReadCategoryHierarchy(serverState, item),
            BrandId: MercariItemDetailSegmentationReader.ReadRefInt(serverState, item, "brand", "id"),
            ConditionId: MercariItemDetailSegmentationReader.ReadRefInt(serverState, item, "itemCondition", "id"),
            SizeName: ReadRefName(serverState, item, "itemSize"),
            ColorName: null,
            ShippingPayer: MercariItemDetailSegmentationReader.ReadRefString(
                serverState, item, "shippingPayer", "code"),
            ShipsFromState: ReadRefName(serverState, item, "shippingFromArea"),
            DiscountRatio: ReadInt(item, "discountRatio"),
            SellerProfile: MercariItemDetailSegmentationReader.ReadSellerProfile(serverState, item),
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

    private static decimal? ReadShippingCost(JsonElement serverState, JsonElement item)
    {
        if (!TryResolveRef(serverState, item, "shippingPayer", out var payer))
        {
            return null;
        }

        if (string.Equals(ReadString(payer, "code"), SellerPayerCode, StringComparison.Ordinal))
        {
            return 0m;
        }

        return TryResolveRef(serverState, item, "shippingClass", out var shippingClass)
            ? ReadCents(shippingClass, "fee")
            : null;
    }

    private static string? ReadRefName(JsonElement serverState, JsonElement item, string propertyName) =>
        TryResolveRef(serverState, item, propertyName, out var resolved) ? ReadString(resolved, "name") : null;

    internal static bool TryResolveRef(
        JsonElement serverState, JsonElement item, string propertyName, out JsonElement resolved)
    {
        resolved = default;

        if (!item.TryGetProperty(propertyName, out var reference) || reference.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!reference.TryGetProperty(RefPropertyName, out var refProperty)
            || refProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var key = refProperty.GetString();
        return key is not null && serverState.TryGetProperty(key, out resolved);
    }

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
