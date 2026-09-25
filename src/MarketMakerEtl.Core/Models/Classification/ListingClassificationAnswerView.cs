namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ListingClassificationAnswerView(
    string Question,
    string Choice,
    string? ResolvedChoice,
    bool IsApplicable,
    double Confidence,
    double Agreement,
    ClassificationSource Source);
