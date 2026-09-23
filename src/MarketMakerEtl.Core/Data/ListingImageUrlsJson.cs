using System.Text.Json;

namespace MarketMakerEtl.Core.Data;

internal static class ListingImageUrlsJson
{
    public static string? Serialize(IReadOnlyList<string>? imageUrls) =>
        imageUrls is null || imageUrls.Count == 0
            ? null
            : JsonSerializer.Serialize(imageUrls);

    public static IReadOnlyList<string>? Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<List<string>>(json);
}
