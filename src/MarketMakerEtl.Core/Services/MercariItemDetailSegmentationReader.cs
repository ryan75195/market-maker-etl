using System.Text.Json;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

internal static class MercariItemDetailSegmentationReader
{
    public static MercariCategoryHierarchy? ReadCategoryHierarchy(JsonElement serverState, JsonElement item)
    {
        if (!MercariItemDetailJsonParser.TryResolveRef(serverState, item, "itemCategory", out var category))
        {
            return null;
        }

        var level = MercariItemDetailJsonParser.ReadInt(category, "level");
        var id = MercariItemDetailJsonParser.ReadInt(category, "id");
        var name = MercariItemDetailJsonParser.ReadString(category, "name");

        return level switch
        {
            0 => new MercariCategoryHierarchy(Level0Id: id, Level0Name: name),
            1 => new MercariCategoryHierarchy(Level1Id: id, Level1Name: name),
            2 => new MercariCategoryHierarchy(Level2Id: id, Level2Name: name),
            _ => null
        };
    }

    public static MercariSellerProfile? ReadSellerProfile(JsonElement serverState, JsonElement item)
    {
        if (!MercariItemDetailJsonParser.TryResolveRef(serverState, item, "seller", out var seller))
        {
            return null;
        }

        var sellerId = ReadLong(seller, "id");
        if (sellerId is null)
        {
            return null;
        }

        var hasRatings = seller.TryGetProperty("ratings", out var ratings) && ratings.ValueKind == JsonValueKind.Object;

        return new MercariSellerProfile(
            SellerId: sellerId.Value,
            Name: MercariItemDetailJsonParser.ReadString(seller, "name"),
            NumSales: MercariItemDetailJsonParser.ReadInt(seller, "numSales"),
            NumSellItems: MercariItemDetailJsonParser.ReadInt(seller, "numSellItems"),
            RatingCount: hasRatings ? MercariItemDetailJsonParser.ReadInt(ratings, "count") : null,
            RatingAverage: hasRatings ? ReadDouble(ratings, "average") : null,
            IsProSeller: ReadBool(seller, "isProSeller"),
            AccountCreatedUtc: MercariItemDetailJsonParser.ReadDateTimeOffset(seller, "created"));
    }

    public static IReadOnlyDictionary<string, string>? ReadAdditionalAttributes(JsonElement item)
    {
        if (!item.TryGetProperty("additionalAttributes", out var attributes)
            || attributes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        Dictionary<string, string>? result = null;

        foreach (var attribute in attributes.EnumerateArray())
        {
            var name = MercariItemDetailJsonParser.ReadString(attribute, "name")
                ?? MercariItemDetailJsonParser.ReadString(attribute, "key");
            var value = MercariItemDetailJsonParser.ReadString(attribute, "value");

            if (name is null || value is null)
            {
                continue;
            }

            result ??= [];
            result[name] = value;
        }

        return result;
    }

    public static int? ReadRefInt(JsonElement serverState, JsonElement item, string propertyName, string fieldName) =>
        MercariItemDetailJsonParser.TryResolveRef(serverState, item, propertyName, out var resolved)
            ? MercariItemDetailJsonParser.ReadInt(resolved, fieldName)
            : null;

    public static string? ReadRefString(
        JsonElement serverState, JsonElement item, string propertyName, string fieldName) =>
        MercariItemDetailJsonParser.TryResolveRef(serverState, item, propertyName, out var resolved)
            ? MercariItemDetailJsonParser.ReadString(resolved, fieldName)
            : null;

    private static long? ReadLong(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : null;

    private static double? ReadDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
            ? number
            : null;

    private static bool? ReadBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
