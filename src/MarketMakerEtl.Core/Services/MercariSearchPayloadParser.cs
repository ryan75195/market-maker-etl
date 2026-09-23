using System.Text.Json;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

internal static class MercariSearchPayloadParser
{
    private const string SoldStatus = "trading";
    private const string CurrencyCode = "USD";

    public static bool IsPayload(string content) =>
        content.AsSpan().TrimStart().StartsWith("{", StringComparison.Ordinal);

    public static IReadOnlyList<ListingSummary> Parse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var summaries = new List<ListingSummary>();

        if (!TryGetItems(document.RootElement, out var items))
        {
            return summaries;
        }

        foreach (var item in items.EnumerateArray())
        {
            var listingId = ReadString(item, "id");

            if (!string.IsNullOrWhiteSpace(listingId))
            {
                summaries.Add(BuildSummary(listingId, item));
            }
        }

        return summaries;
    }

    public static bool IsEmptyResultSet(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return TryGetItems(document.RootElement, out var items) && items.GetArrayLength() == 0;
    }

    private static bool TryGetItems(JsonElement root, out JsonElement items)
    {
        items = default;
        return TryGetObject(root, "data", out var data)
            && TryGetObject(data, "search", out var search)
            && search.TryGetProperty("itemsList", out items)
            && items.ValueKind == JsonValueKind.Array;
    }

    private static ListingSummary BuildSummary(string listingId, JsonElement item) =>
        new(
            ListingId: listingId,
            Title: ReadString(item, "name"),
            Price: ReadCents(item, "price"),
            Currency: CurrencyCode,
            Url: MercariItemUrl.Build(listingId),
            IsSold: string.Equals(ReadString(item, "status"), SoldStatus, StringComparison.OrdinalIgnoreCase),
            Condition: ReadNestedName(item, "itemCondition"),
            PrimaryImageUrl: ReadPrimaryImageUrl(item),
            BuyingFormat: null,
            Brand: ReadNestedName(item, "brand"));

    private static string? ReadPrimaryImageUrl(JsonElement item)
    {
        if (!item.TryGetProperty("photos", out var photos)
            || photos.ValueKind != JsonValueKind.Array
            || photos.GetArrayLength() == 0)
        {
            return null;
        }

        return ReadString(photos[0], "imageUrl");
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
