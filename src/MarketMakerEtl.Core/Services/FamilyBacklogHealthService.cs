using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Families;
using MarketMakerEtl.Core.Models.Health;
using MarketMakerEtl.Core.Models.Jobs;

namespace MarketMakerEtl.Core.Services;

public sealed class FamilyBacklogHealthService : IFamilyBacklogHealthService
{
    private readonly IProductFamilyStore _families;
    private readonly IJobStore _jobs;
    private readonly IListingClassificationStore _classifications;
    private readonly IClassificationReviewStore _reviews;
    private readonly ClassificationReviewOptions _reviewOptions;

    public FamilyBacklogHealthService(
        IProductFamilyStore families,
        IJobStore jobs,
        IListingClassificationStore classifications,
        IClassificationReviewStore reviews,
        ClassificationReviewOptions reviewOptions)
    {
        _families = families;
        _jobs = jobs;
        _classifications = classifications;
        _reviews = reviews;
        _reviewOptions = reviewOptions;
    }

    public async Task<IReadOnlyList<FamilyBacklogHealthView>> GetFamilyBacklogHealth(CancellationToken ct)
    {
        var families = await _families.GetFamilies(ct);
        if (families.Count == 0)
        {
            return [];
        }

        var jobsByFamily = await LoadJobsByFamily(ct);
        var views = new List<FamilyBacklogHealthView>(families.Count);

        foreach (var family in families)
        {
            views.Add(await BuildFamilyHealth(family, jobsByFamily, ct));
        }

        return views;
    }

    private async Task<IReadOnlyDictionary<int, List<JobView>>> LoadJobsByFamily(CancellationToken ct)
    {
        var jobs = await _jobs.GetEffectivelyEnabledJobs(ct);
        return jobs
            .Where(job => job.ProductFamilyId.HasValue)
            .GroupBy(job => job.ProductFamilyId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    private async Task<FamilyBacklogHealthView> BuildFamilyHealth(
        ProductFamilyView family, IReadOnlyDictionary<int, List<JobView>> jobsByFamily, CancellationToken ct)
    {
        if (family.LatestTaxonomyVersion is null)
        {
            return new FamilyBacklogHealthView(family.Id, family.Key, 0, 0);
        }

        var taxonomyVersionId = family.LatestTaxonomyVersion.Id;
        var pending = await CountPendingClassification(family.Id, taxonomyVersionId, jobsByFamily, ct);
        var reviewCounts = await _reviews.GetReviewSummary(
            family.Id, taxonomyVersionId, _reviewOptions.ReviewThreshold, ct);

        return new FamilyBacklogHealthView(family.Id, family.Key, pending, reviewCounts.Sum(c => c.Count));
    }

    private async Task<int> CountPendingClassification(
        int familyId, int taxonomyVersionId, IReadOnlyDictionary<int, List<JobView>> jobsByFamily, CancellationToken ct)
    {
        if (!jobsByFamily.TryGetValue(familyId, out var jobs))
        {
            return 0;
        }

        var total = 0;
        foreach (var job in jobs)
        {
            total += await _classifications.CountListingsNeedingClassification(job.Id, taxonomyVersionId, ct);
        }

        return total;
    }
}
