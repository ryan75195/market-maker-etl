namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassificationReviewRow(
    int ListingEntityId,
    string? Title,
    string? Url,
    decimal? Price,
    bool IsSold,
    string? PrimaryImageUrl,
    string? ImageUrlsJson,
    string? Description,
    string? Condition,
    string? Category0Name,
    string? Category1Name,
    string? Category2Name,
    string Question,
    string Choice,
    double Confidence,
    double Agreement,
    string ProbabilitiesJson);
