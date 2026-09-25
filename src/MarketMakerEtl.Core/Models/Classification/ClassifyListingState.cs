using System.Text.Json.Serialization;

namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassifyListingState(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("mercari_category")] string? MercariCategory,
    [property: JsonPropertyName("brand")] string? Brand,
    [property: JsonPropertyName("description")] string? Description);
