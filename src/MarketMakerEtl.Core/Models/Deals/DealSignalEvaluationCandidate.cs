namespace MarketMakerEtl.Core.Models.Deals;

public sealed record DealSignalEvaluationCandidate(
    int Id,
    int ListingEntityId,
    int TaxonomyVersionId,
    IReadOnlyDictionary<string, string> GroupKey,
    decimal LandedPrice,
    DateTime CreatedUtc,
    int? EvaluationWindowDays);
