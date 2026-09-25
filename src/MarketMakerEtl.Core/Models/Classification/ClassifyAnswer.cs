using System.Text.Json.Serialization;

namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassifyAnswer(
    [property: JsonPropertyName("choice")] string Choice,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("agreement")] double Agreement,
    [property: JsonPropertyName("probabilities")] IReadOnlyDictionary<string, double> Probabilities);
