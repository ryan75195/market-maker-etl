namespace MarketMakerEtl.Core.Models.Onboarding;

public sealed record OnboardingOptions(
    string DraftModel,
    string DraftReasoningEffort,
    int MaxSampleListings,
    int MaxDescriptionChars,
    int DraftTimeoutSeconds);
