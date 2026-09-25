namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ClassificationReviewItem(
    int ListingId,
    string? Title,
    string? Url,
    decimal? Price,
    bool IsSold,
    string Question,
    string Instructions,
    IReadOnlyList<ClassificationReviewOption> Options,
    string Choice,
    double Confidence,
    double Agreement,
    IReadOnlyList<ClassificationProbability> Probabilities);
