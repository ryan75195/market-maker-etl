using System.Text.Json.Serialization;

namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassifyRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("questions")] IReadOnlyDictionary<string, ClassifyQuestion> Questions,
    [property: JsonPropertyName("states")] IReadOnlyList<ClassifyListingState> States);
