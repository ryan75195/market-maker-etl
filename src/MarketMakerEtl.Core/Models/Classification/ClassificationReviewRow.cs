namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassificationReviewRow(
    int ListingEntityId,
    string? Title,
    string? Url,
    decimal? Price,
    bool IsSold,
    string Question,
    string Choice,
    double Confidence,
    double Agreement,
    string ProbabilitiesJson);
