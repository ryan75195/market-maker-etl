namespace MarketMakerEtl.Core.Models.PriceGroups;

public sealed record PriceGroupAnswer(
    string Question,
    string? ResolvedChoice,
    bool IsApplicable,
    bool NeedsReview,
    int TaxonomyVersionId);
