namespace MarketMakerEtl.Core.Models.Classification;

public sealed record ListingClassificationRow(
    string Question,
    string Choice,
    string? ResolvedChoice,
    bool IsApplicable,
    double Confidence,
    double Agreement,
    string ProbabilitiesJson);
