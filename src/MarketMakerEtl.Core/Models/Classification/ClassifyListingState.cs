using System.Text.Json.Serialization;

namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassifyListingState(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("brand")] string? Brand,
    [property: JsonPropertyName("sold")] bool Sold);
