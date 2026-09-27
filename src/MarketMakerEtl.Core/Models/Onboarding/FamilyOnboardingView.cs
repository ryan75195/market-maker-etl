using MarketMakerEtl.Core.Models.Families;

namespace MarketMakerEtl.Core.Models.Onboarding;

public sealed record FamilyOnboardingView(
    int FamilyId,
    string Key,
    string Name,
    FamilyState State,
    string SearchTerm,
    int TaxonomyVersion,
    string TaxonomyJson,
    string? DealGroupBy,
    decimal DealMinDiscount,
    int DealMinSold,
    IReadOnlyList<OnboardingQuestionDistributionView> Preview,
    int SampleSize,
    int PromptTokens,
    int CompletionTokens,
    decimal CostUsd,
    string? LastFeedback,
    DateTime UpdatedUtc);
