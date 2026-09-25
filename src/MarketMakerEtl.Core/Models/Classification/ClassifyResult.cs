using System.Text.Json.Serialization;

namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassifyResult(
    [property: JsonPropertyName("answers")] IReadOnlyDictionary<string, ClassifyAnswer> Answers);
