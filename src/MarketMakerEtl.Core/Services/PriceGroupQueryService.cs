using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.PriceGroups;

namespace MarketMakerEtl.Core.Services;

public sealed class PriceGroupQueryService : IPriceGroupQueryService
{
    private readonly IPriceGroupListingStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly PriceGroupOptions _options;

    public PriceGroupQueryService(IPriceGroupListingStore store, TimeProvider timeProvider, PriceGroupOptions options)
    {
        _store = store;
        _timeProvider = timeProvider;
        _options = options;
    }

    public async Task<IReadOnlyList<PriceGroupSummary>> GetPriceGroups(PriceGroupQuery query, CancellationToken ct)
    {
        var questions = query.Where.Keys.Union(query.By, StringComparer.Ordinal).ToList();
        var candidates = await _store.GetCandidates(query.TaxonomyVersionId, questions, ct);
        var soldCutoff = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-query.SoldDays);

        var groups = new Dictionary<string, PriceGroupAccumulator>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            AddToGroup(candidate, query, soldCutoff, groups);
        }

        return groups.Values
            .Select(g => g.ToSummary(query.TrimIqr))
            .Where(summary => summary.SoldCount >= query.MinSold)
            .OrderByDescending(summary => summary.SoldCount)
            .ToList();
    }

    public async Task<IReadOnlyList<PriceGroupListingResult>> GetGroupListings(
        PriceGroupListingsQuery query, CancellationToken ct)
    {
        var candidates = await _store.GetCandidates(query.TaxonomyVersionId, query.Where.Keys.ToList(), ct);
        var soldCutoff = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-query.SoldDays);
        var matching = candidates
            .Where(c => MatchesWhere(c, query.TaxonomyVersionId, query.Where, query.IncludeUncertain))
            .ToList();

        var soldMedian = ComputeSoldMedian(matching, soldCutoff);
        var soldNetMedian = ComputeSoldNetMedian(matching, soldCutoff, _options);
        var results = query.Status == PriceGroupListingStatus.Sold
            ? BuildSoldResults(matching, soldCutoff, soldMedian, soldNetMedian, _options)
            : BuildActiveResults(matching, soldMedian, soldNetMedian, _options);

        return results.Take(query.Take).ToList();
    }

    public async Task<IReadOnlyList<PriceGroupHistoryBucket>> GetPriceGroupHistory(
        PriceGroupHistoryQuery query, CancellationToken ct)
    {
        var candidates = await _store.GetCandidates(query.TaxonomyVersionId, query.Where.Keys.ToList(), ct);
        var matching = candidates
            .Where(c => MatchesWhere(c, query.TaxonomyVersionId, query.Where, includeUncertain: false))
            .ToList();

        return PriceGroupHistoryCalculator.Build(matching, query, _timeProvider.GetUtcNow().UtcDateTime, _options);
    }

    private void AddToGroup(
        PriceGroupListingCandidate candidate,
        PriceGroupQuery query,
        DateTime soldCutoff,
        Dictionary<string, PriceGroupAccumulator> groups)
    {
        if (!MatchesWhere(candidate, query.TaxonomyVersionId, query.Where, query.IncludeUncertain))
        {
            return;
        }

        var groupKey = TryBuildGroupKey(candidate, query);
        if (groupKey is null)
        {
            return;
        }

        var canonicalKey = string.Join('\u001F', query.By.Select(q => $"{q}={groupKey[q]}"));
        if (!groups.TryGetValue(canonicalKey, out var accumulator))
        {
            accumulator = new PriceGroupAccumulator(groupKey, _options);
            groups[canonicalKey] = accumulator;
        }

        accumulator.Add(candidate, soldCutoff);
    }

    private static bool MatchesWhere(
        PriceGroupListingCandidate candidate,
        int taxonomyVersionId,
        IReadOnlyDictionary<string, string> where,
        bool includeUncertain)
    {
        foreach (var (question, option) in where)
        {
            var answer = FindAnswer(candidate, question, taxonomyVersionId);
            if (answer is null || !answer.IsApplicable || answer.ResolvedChoice != option)
            {
                return false;
            }

            if (!includeUncertain && answer.NeedsReview)
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyDictionary<string, string>? TryBuildGroupKey(
        PriceGroupListingCandidate candidate, PriceGroupQuery query)
    {
        var key = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var question in query.By)
        {
            var answer = FindAnswer(candidate, question, query.TaxonomyVersionId);
            if (answer is null || !answer.IsApplicable || answer.ResolvedChoice is null)
            {
                return null;
            }

            if (!query.IncludeUncertain && answer.NeedsReview)
            {
                return null;
            }

            key[question] = answer.ResolvedChoice;
        }

        return key;
    }

    private static PriceGroupAnswer? FindAnswer(
        PriceGroupListingCandidate candidate, string question, int taxonomyVersionId) =>
        candidate.Answers.FirstOrDefault(a => a.Question == question && a.TaxonomyVersionId == taxonomyVersionId);

    private static decimal? ComputeSoldMedian(
        IReadOnlyList<PriceGroupListingCandidate> matching, DateTime soldCutoff)
    {
        var soldPrices = matching
            .Where(c => IsSoldWithinWindow(c, soldCutoff))
            .Select(c => c.SoldPrice!.Value)
            .ToList();

        return PriceGroupPercentileCalculator.Percentile(soldPrices, 0.5);
    }

    private static decimal? ComputeSoldNetMedian(
        IReadOnlyList<PriceGroupListingCandidate> matching, DateTime soldCutoff, PriceGroupOptions options)
    {
        var netProceeds = matching
            .Where(c => IsSoldWithinWindow(c, soldCutoff))
            .Select(c => PriceGroupNetCalculator.ComputeNetProceeds(
                c.SoldPrice!.Value, c.ShippingPayer, c.ShippingCost, options))
            .ToList();

        return PriceGroupPercentileCalculator.Percentile(netProceeds, 0.5);
    }

    private static bool IsSoldWithinWindow(PriceGroupListingCandidate candidate, DateTime soldCutoff) =>
        candidate.IsSold && candidate.SoldPrice.HasValue && candidate.EffectiveSoldDate >= soldCutoff;

    private static List<PriceGroupListingResult> BuildSoldResults(
        IReadOnlyList<PriceGroupListingCandidate> matching,
        DateTime soldCutoff,
        decimal? soldMedian,
        decimal? soldNetMedian,
        PriceGroupOptions options) =>
        matching
            .Where(c => IsSoldWithinWindow(c, soldCutoff))
            .OrderByDescending(c => c.EffectiveSoldDate)
            .Select(c => ToResult(c, c.SoldPrice, c.SoldDate, c.SoldDate is null, soldMedian, soldNetMedian, options))
            .ToList();

    private static List<PriceGroupListingResult> BuildActiveResults(
        IReadOnlyList<PriceGroupListingCandidate> matching,
        decimal? soldMedian,
        decimal? soldNetMedian,
        PriceGroupOptions options) =>
        matching
            .Where(c => !c.IsSold && c.Price.HasValue)
            .Select(c => ToResult(c, c.Price, null, false, soldMedian, soldNetMedian, options))
            .OrderBy(r => r.DeltaFromSoldMedian ?? decimal.MaxValue)
            .ToList();

    private static PriceGroupListingResult ToResult(
        PriceGroupListingCandidate candidate,
        decimal? price,
        DateTime? soldDate,
        bool soldDateIsEstimated,
        decimal? soldMedian,
        decimal? soldNetMedian,
        PriceGroupOptions options)
    {
        var landedPrice = price.HasValue
            ? PriceGroupNetCalculator.ComputeLandedPrice(price.Value, candidate.ShippingPayer, candidate.ShippingCost)
            : (decimal?)null;
        var netProceeds = price.HasValue
            ? PriceGroupNetCalculator.ComputeNetProceeds(price.Value, candidate.ShippingPayer, candidate.ShippingCost, options)
            : (decimal?)null;

        return new(
            candidate.ListingId,
            candidate.Title,
            candidate.Url,
            price,
            soldDate,
            soldDateIsEstimated,
            price.HasValue && soldMedian.HasValue ? price - soldMedian : null,
            landedPrice,
            netProceeds,
            landedPrice.HasValue && soldNetMedian.HasValue ? landedPrice - soldNetMedian : null);
    }
}
