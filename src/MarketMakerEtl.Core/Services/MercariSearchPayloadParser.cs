using System.Text.Json;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

internal static class MercariSearchPayloadParser
{
    private const string SoldStatus = "trading";
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
            IsSold: string.Equals(ReadString(item, "status"), SoldStatus, StringComparison.OrdinalIgnoreCase),
            Condition: ReadNestedName(item, "itemCondition"),
            PrimaryImageUrl: imageUrls.Count > 0 ? imageUrls[0] : null,
            BuyingFormat: null,
            Brand: ReadNestedName(item, "brand"),
            OriginalPrice: ReadCents(item, "originalPrice"),
            Category: ReadNestedName(item, "itemCategory"),
            Likes: null,
            ImageUrls: imageUrls.Count > 0 ? imageUrls : null);
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

    private static string? ReadNestedName(JsonElement item, string propertyName) =>
        TryGetObject(item, propertyName, out var nested) ? ReadString(nested, "name") : null;

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
