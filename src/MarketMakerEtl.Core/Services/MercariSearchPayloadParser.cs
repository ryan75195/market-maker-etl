using System.Text.Json;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

internal static class MercariSearchPayloadParser
{
    private const string TradingStatus = "trading";
    private const string SoldOutStatus = "sold_out";
    private const string CurrencyCode = "USD";

    public static bool IsPayload(string content) =>
        content.AsSpan().TrimStart().StartsWith("{", StringComparison.Ordinal);

    public static SearchPageResult Parse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var totalCount = ReadTotalCount(document.RootElement);
        var summaries = new List<ListingSummary>();

        if (!TryGetItems(document.RootElement, out var items))
        {
            return new SearchPageResult(summaries, totalCount);
        }

        foreach (var item in items.EnumerateArray())
        {
            var listingId = ReadString(item, "id");

            if (!string.IsNullOrWhiteSpace(listingId))
            {
                summaries.Add(BuildSummary(listingId, item));
            }
        }

        return new SearchPageResult(summaries, totalCount);
    }

    public static bool IsEmptyResultSet(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return TryGetItems(document.RootElement, out var items) && items.GetArrayLength() == 0;
    }

    public static bool HasSearchResult(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return TryGetObject(document.RootElement, "data", out var data) && TryGetObject(data, "search", out _);
    }

    private static int? ReadTotalCount(JsonElement root) =>
        TryGetObject(root, "data", out var data)
        && TryGetObject(data, "search", out var search)
        && search.TryGetProperty("count", out var count)
        && count.ValueKind == JsonValueKind.Number
        && count.TryGetInt32(out var value)
            ? value
            : null;

    private static bool TryGetItems(JsonElement root, out JsonElement items)
    {
        items = default;
        return TryGetObject(root, "data", out var data)
            && TryGetObject(data, "search", out var search)
            && search.TryGetProperty("itemsList", out items)
            && items.ValueKind == JsonValueKind.Array;
    }

    private static ListingSummary BuildSummary(string listingId, JsonElement item)
    {
        var imageUrls = ReadImageUrls(item);

        return new(
            ListingId: listingId,
            Title: ReadString(item, "name"),
            Price: ReadCents(item, "price"),
            Currency: CurrencyCode,
            Url: MercariItemUrl.Build(listingId),
            IsSold: IsSoldStatus(ReadString(item, "status")),
            Condition: ReadNestedName(item, "itemCondition"),
            PrimaryImageUrl: imageUrls.Count > 0 ? imageUrls[0] : null,
            BuyingFormat: null,
            Brand: ReadNestedName(item, "brand"),
            OriginalPrice: ReadCents(item, "originalPrice"),
            Category: ReadNestedName(item, "itemCategory"),
            Likes: null,
            ImageUrls: imageUrls.Count > 0 ? imageUrls : null,
            CategoryId: ReadInt(item, "categoryId"),
            CategoryHierarchy: ReadCategoryHierarchy(item),
            BrandId: ReadNestedInt(item, "brand", "id"),
            ConditionId: ReadNestedInt(item, "itemCondition", "id"),
            SizeName: ReadNestedName(item, "itemSize"),
            ColorName: ReadString(item, "color"),
            ShippingPayer: ReadNestedString(item, "shippingPayer", "code"),
            SellerId: ReadNestedLong(item, "seller", "sellerId"),
            Attributes: ReadCustomFacets(item),
            RawJson: item.GetRawText());
    }

    private static MercariCategoryHierarchy? ReadCategoryHierarchy(JsonElement item)
    {
        if (!item.TryGetProperty("itemCategoryHierarchy", out var hierarchy)
            || hierarchy.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        int? level0Id = null;
        string? level0Name = null;
        int? level1Id = null;
        string? level1Name = null;
        int? level2Id = null;
        string? level2Name = null;

        foreach (var level in hierarchy.EnumerateArray())
        {
            var levelNumber = ReadInt(level, "level");
            var id = ReadInt(level, "id");
            var name = ReadString(level, "name");

            switch (levelNumber)
            {
                case 0:
                    level0Id = id;
                    level0Name = name;
                    break;
                case 1:
                    level1Id = id;
                    level1Name = name;
                    break;
                case 2:
                    level2Id = id;
                    level2Name = name;
                    break;
            }
        }

        return new MercariCategoryHierarchy(level0Id, level0Name, level1Id, level1Name, level2Id, level2Name);
    }

    private static IReadOnlyDictionary<string, string>? ReadCustomFacets(JsonElement item)
    {
        if (!item.TryGetProperty("customFacetsList", out var facets) || facets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        Dictionary<string, string>? attributes = null;

        foreach (var facet in facets.EnumerateArray())
        {
            var name = ReadString(facet, "facetName");
            var value = ReadString(facet, "value");

            if (name is null || value is null)
            {
                continue;
            }

            attributes ??= [];
            attributes[name] = value;
        }

        return attributes;
    }

    private static bool IsSoldStatus(string? status) =>
        string.Equals(status, TradingStatus, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, SoldOutStatus, StringComparison.OrdinalIgnoreCase);

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

    private static string? ReadNestedName(JsonElement item, string propertyName) =>
        TryGetObject(item, propertyName, out var nested) ? ReadString(nested, "name") : null;

    private static string? ReadNestedString(JsonElement item, string propertyName, string nestedPropertyName) =>
        TryGetObject(item, propertyName, out var nested) ? ReadString(nested, nestedPropertyName) : null;

    private static int? ReadNestedInt(JsonElement item, string propertyName, string nestedPropertyName) =>
        TryGetObject(item, propertyName, out var nested) ? ReadInt(nested, nestedPropertyName) : null;

    private static long? ReadNestedLong(JsonElement item, string propertyName, string nestedPropertyName) =>
        TryGetObject(item, propertyName, out var nested) ? ReadLong(nested, nestedPropertyName) : null;

    private static int? ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    private static long? ReadLong(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : null;

    private static decimal? ReadCents(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDecimal(out var cents)
            ? cents / 100m
            : null;

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out value)
            && value.ValueKind == JsonValueKind.Object;
    }
}
