using System.Text.Json.Serialization;

namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassifyResponse(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("members")] int Members,
    [property: JsonPropertyName("results")] IReadOnlyList<ClassifyResult> Results);
