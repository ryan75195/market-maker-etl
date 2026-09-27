namespace MarketMakerEtl.Core.Models.Onboarding;

public sealed record OnboardingOptions(
    string DraftModel,
    string DraftReasoningEffort,
    decimal DraftInputCostPerMillionUsd,
    decimal DraftOutputCostPerMillionUsd,
    int MaxSampleListings,
    int MaxDescriptionChars,
    int DraftTimeoutSeconds);
