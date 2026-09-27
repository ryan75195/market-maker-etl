using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Onboarding;

namespace MarketMakerEtl.Core.Services;

public sealed class FamilyOnboardingPreviewService : IFamilyOnboardingPreviewService
{
    private readonly IFamilyOnboardingStore _onboarding;
    private readonly IListingClassifierClient _classifier;
    private readonly IProductFamilyStore _families;

    public FamilyOnboardingPreviewService(
        IFamilyOnboardingStore onboarding, IListingClassifierClient classifier, IProductFamilyStore families)
    {
        _onboarding = onboarding;
        _classifier = classifier;
        _families = families;
    }

    public async Task RunPreview(int familyId, CancellationToken ct)
    {
        var family = await _families.GetFamily(familyId, ct);
        var snapshot = await _onboarding.GetSnapshot(familyId, ct);
        if (family?.LatestTaxonomyVersion is null || snapshot is null || snapshot.Sample.Count == 0)
        {
            return;
        }

        var taxonomy = TaxonomyDocumentParser.Parse(family.LatestTaxonomyVersion.QuestionsJson);
        var questions = taxonomy.Questions.ToDictionary(question => question.Key, ClassifyQuestionBuilder.Build);
        var states = snapshot.Sample
            .Select(listing => new ClassifyListingState(
                listing.Id, listing.Title, listing.Description, listing.Category, listing.Brand, listing.Sold))
            .ToList();

        var request = new ClassifyRequest(family.ModelName, questions, states, taxonomy.Guidance);
        var response = await _classifier.Classify(request, OpenAiUsagePurpose.OnboardingPreview, ct);
        var preview = OnboardingPreviewBuilder.Build(questions.Keys, snapshot.Sample, response);
        await _onboarding.SavePreview(familyId, preview, ct);
    }

    public async Task<FamilyOnboardingView?> GetOnboarding(int familyId, CancellationToken ct)
    {
        var family = await _families.GetFamily(familyId, ct);
        var snapshot = await _onboarding.GetSnapshot(familyId, ct);
        if (family is null || snapshot is null)
        {
            return null;
        }

        var taxonomyVersion = family.LatestTaxonomyVersion?.Version ?? 0;
        var taxonomyJson = family.LatestTaxonomyVersion?.QuestionsJson ?? string.Empty;
        return new FamilyOnboardingView(
            family.Id,
            family.Key,
            family.Name,
            family.State,
            snapshot.SearchTerm,
            taxonomyVersion,
            taxonomyJson,
            family.DealGroupBy,
            family.DealMinDiscount,
            family.DealMinSold,
            snapshot.Preview,
            snapshot.Sample.Count,
            snapshot.PromptTokens,
            snapshot.CompletionTokens,
            snapshot.CostUsd,
            snapshot.LastFeedback,
            snapshot.UpdatedUtc);
    }
}
