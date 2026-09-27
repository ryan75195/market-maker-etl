namespace MarketMakerEtl.Core.Models.Onboarding;

public sealed record FamilyOnboardingSnapshot(
    int ProductFamilyId,
    int JobId,
    string SearchTerm,
    IReadOnlyList<FamilySampleListing> Sample,
    IReadOnlyList<OnboardingQuestionDistributionView> Preview,
    int PromptTokens,
    int CompletionTokens,
    decimal CostUsd,
    string? LastFeedback,
    DateTime UpdatedUtc);
