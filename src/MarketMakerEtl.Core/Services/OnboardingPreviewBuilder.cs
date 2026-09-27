using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Onboarding;

namespace MarketMakerEtl.Core.Services;

internal static class OnboardingPreviewBuilder
{
    private const int MaxExamplesPerChoice = 3;

    public static IReadOnlyList<OnboardingQuestionDistributionView> Build(
        IReadOnlyCollection<string> questionKeys,
        IReadOnlyList<FamilySampleListing> sample,
        ClassifyResponse response)
    {
        var resultsById = BuildResultsById(sample, response);
        return questionKeys
            .Select(questionKey => BuildQuestionDistribution(questionKey, sample, resultsById))
            .ToList();
    }

    private static Dictionary<string, ClassifyResult> BuildResultsById(
        IReadOnlyList<FamilySampleListing> sample, ClassifyResponse response)
    {
        var resultsById = new Dictionary<string, ClassifyResult>(StringComparer.Ordinal);
        for (var i = 0; i < sample.Count && i < response.Results.Count; i++)
        {
            resultsById[sample[i].Id] = response.Results[i];
        }

        return resultsById;
    }

    private static OnboardingQuestionDistributionView BuildQuestionDistribution(
        string questionKey,
        IReadOnlyList<FamilySampleListing> sample,
        IReadOnlyDictionary<string, ClassifyResult> resultsById)
    {
        var choiceGroups = GroupByChoice(questionKey, sample, resultsById);
        var choices = choiceGroups
            .Select(pair => BuildChoiceDistribution(pair.Key, pair.Value))
            .OrderByDescending(choice => choice.Count)
            .ToList();
        return new OnboardingQuestionDistributionView(questionKey, choices);
    }

    private static Dictionary<string, List<FamilySampleListing>> GroupByChoice(
        string questionKey,
        IReadOnlyList<FamilySampleListing> sample,
        IReadOnlyDictionary<string, ClassifyResult> resultsById)
    {
        var choiceGroups = new Dictionary<string, List<FamilySampleListing>>(StringComparer.Ordinal);
        foreach (var listing in sample)
        {
            if (!resultsById.TryGetValue(listing.Id, out var result)
                || !result.Answers.TryGetValue(questionKey, out var answer))
            {
                continue;
            }

            if (!choiceGroups.TryGetValue(answer.Choice, out var listings))
            {
                listings = [];
                choiceGroups[answer.Choice] = listings;
            }

            listings.Add(listing);
        }

        return choiceGroups;
    }

    private static OnboardingChoiceDistributionView BuildChoiceDistribution(
        string choice, IReadOnlyList<FamilySampleListing> listings) =>
        new(
            choice,
            listings.Count,
            listings.Take(MaxExamplesPerChoice)
                .Select(listing => new OnboardingExampleListingView(listing.Id, listing.Title, listing.Description, listing.Sold))
                .ToList());
}
