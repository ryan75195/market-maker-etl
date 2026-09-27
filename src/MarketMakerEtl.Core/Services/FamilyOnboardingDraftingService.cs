using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Jobs;
using MarketMakerEtl.Core.Models.Marketplaces;
using MarketMakerEtl.Core.Models.Onboarding;

namespace MarketMakerEtl.Core.Services;

public sealed class FamilyOnboardingDraftingService : IFamilyOnboardingDraftingService
{
    private const int DefaultIntervalHours = 24;

    private readonly IProductFamilyStore _families;
    private readonly IJobStore _jobs;
    private readonly IFamilySampleFetchService _sampleFetch;
    private readonly IFamilyDraftClient _draftClient;
    private readonly IFamilyOnboardingStore _onboarding;

    public FamilyOnboardingDraftingService(
        IProductFamilyStore families,
        IJobStore jobs,
        IFamilySampleFetchService sampleFetch,
        IFamilyDraftClient draftClient,
        IFamilyOnboardingStore onboarding)
    {
        _families = families;
        _jobs = jobs;
        _sampleFetch = sampleFetch;
        _draftClient = draftClient;
        _onboarding = onboarding;
    }

    public async Task<ProductFamilyView?> StartOnboarding(
        string name, string searchTerm, string? key, CancellationToken ct)
    {
        var resolvedKey = string.IsNullOrWhiteSpace(key) ? FamilyKeySlug.From(name) : key;
        var family = await _families.CreateFamily(resolvedKey, name, resolvedKey, ct, FamilyState.Draft);
        if (family is null)
        {
            return null;
        }

        var job = await _jobs.CreateJob(
            new JobDetails(searchTerm, Marketplace.Mercari, null, DefaultIntervalHours, false, []), ct);
        await _families.SetJobFamily(job.Id, family.Id, ct);

        var sample = await _sampleFetch.FetchSample(searchTerm, ct);
        var prompt = new FamilyDraftPrompt(name, searchTerm, sample, null, null);
        var result = await _draftClient.DraftTaxonomy(prompt, ct);

        await _families.AddTaxonomyVersion(family.Id, result.TaxonomyJson, ct);
        await _onboarding.Create(
            family.Id, job.Id, searchTerm, sample, result.PromptTokens, result.CompletionTokens, result.CostUsd, ct);
        await ApplyProposedDealGroupBy(family.Id, result.DealGroupBy, ct);

        return await _families.GetFamily(family.Id, ct);
    }

    public async Task<ProductFamilyView?> Regenerate(int familyId, string feedback, CancellationToken ct)
    {
        var family = await _families.GetFamily(familyId, ct);
        if (family is null || family.State != FamilyState.Draft)
        {
            return null;
        }

        var snapshot = await _onboarding.GetSnapshot(familyId, ct);
        if (snapshot is null)
        {
            return null;
        }

        var prompt = new FamilyDraftPrompt(
            family.Name, snapshot.SearchTerm, snapshot.Sample, family.LatestTaxonomyVersion?.QuestionsJson, feedback);
        var result = await _draftClient.DraftTaxonomy(prompt, ct);

        await _families.AddTaxonomyVersion(familyId, result.TaxonomyJson, ct);
        await _onboarding.RecordRedraft(
            familyId, feedback, result.PromptTokens, result.CompletionTokens, result.CostUsd, ct);
        await ApplyProposedDealGroupBy(familyId, result.DealGroupBy, ct);

        return await _families.GetFamily(familyId, ct);
    }

    private async Task ApplyProposedDealGroupBy(int familyId, string? dealGroupBy, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(dealGroupBy))
        {
            await _families.UpdateFamily(familyId, null, null, dealGroupBy, null, null, ct);
        }
    }
}
