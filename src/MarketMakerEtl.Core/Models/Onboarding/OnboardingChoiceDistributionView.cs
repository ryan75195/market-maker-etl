namespace MarketMakerEtl.Core.Models.Onboarding;

public sealed record OnboardingChoiceDistributionView(
    string Choice, int Count, IReadOnlyList<OnboardingExampleListingView> Examples);
