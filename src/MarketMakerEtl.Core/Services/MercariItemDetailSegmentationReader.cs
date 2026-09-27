using System.Text.Json;
using MarketMakerEtl.Core.Models.Ebay;

namespace MarketMakerEtl.Core.Services;

internal static class MercariItemDetailSegmentationReader
{
    private const string AttributeKeyPrefix = "Attribute";

    public static MercariCategoryHierarchy? ReadCategoryHierarchy(JsonElement item)
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
            var levelNumber = MercariItemDetailJsonParser.ReadInt(level, "level");
            var id = MercariItemDetailJsonParser.ReadInt(level, "id");
            var name = MercariItemDetailJsonParser.ReadString(level, "name");

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

    public static MercariSellerProfile? ReadSellerProfile(JsonElement item)
    {
        if (!MercariItemDetailJsonParser.TryResolveRef(item, "seller", out var seller))
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
        var index = 0;

        foreach (var attribute in attributes.EnumerateArray())
        {
            var text = MercariItemDetailJsonParser.ReadString(attribute, "text");

            if (text is not null)
            {
                result ??= [];
                result[$"{AttributeKeyPrefix}{index}"] = text;
            }

            index++;
        }

        return result;
    }

    public static int? ReadRefInt(JsonElement item, string propertyName, string fieldName) =>
        MercariItemDetailJsonParser.TryResolveRef(item, propertyName, out var resolved)
            ? MercariItemDetailJsonParser.ReadInt(resolved, fieldName)
            : null;

    public static string? ReadRefString(JsonElement item, string propertyName, string fieldName) =>
        MercariItemDetailJsonParser.TryResolveRef(item, propertyName, out var resolved)
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
