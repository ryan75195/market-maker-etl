namespace MarketMakerEtl.Core.Models.Onboarding;

public sealed record OnboardingQuestionDistributionView(
    string Question, IReadOnlyList<OnboardingChoiceDistributionView> Choices);
