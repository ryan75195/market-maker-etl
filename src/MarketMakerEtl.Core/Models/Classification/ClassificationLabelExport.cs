using System.Text.Json.Serialization;

namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassificationLabelExport(
    [property: JsonPropertyName("listingId")] string ListingId,
    [property: JsonPropertyName("taxonomyVersion")] int TaxonomyVersion,
    [property: JsonPropertyName("state")] ClassifyListingState State,
    [property: JsonPropertyName("answers")] IReadOnlyDictionary<string, string> Answers);
