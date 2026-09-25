using System.Text.Json;

namespace MarketMakerEtl.Core.Data;

internal static class ListingAttributesJson
{
    public static string? Serialize(IReadOnlyDictionary<string, string>? attributes) =>
        attributes is null || attributes.Count == 0
            ? null
            : JsonSerializer.Serialize(attributes);

    public static IReadOnlyDictionary<string, string>? Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json);
}
