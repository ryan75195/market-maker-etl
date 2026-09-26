using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Deals;
using MarketMakerEtl.Core.Models.PriceGroups;

namespace MarketMakerEtl.Core.Services;

public sealed class DealSignalBacktestService : IDealSignalBacktestService
{
    private const int MinForwardSoldCountForFinalEvaluation = 3;

    private readonly IDealSignalBacktestStore _store;
    private readonly IPriceGroupQueryService _priceGroups;
    private readonly BacktestOptions _options;
    private readonly TimeProvider _timeProvider;

    public DealSignalBacktestService(
        IDealSignalBacktestStore store,
        IPriceGroupQueryService priceGroups,
        BacktestOptions options,
        TimeProvider timeProvider)
    {
        _store = store;
        _priceGroups = priceGroups;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<DealBacktestTickResult> EvaluateSignals(CancellationToken ct)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var pending = await _store.GetSignalsPendingEvaluation(
            nowUtc, _options.HorizonDays, MinForwardSoldCountForFinalEvaluation, ct);

        var results = new List<DealSignalEvaluationResult>();
        foreach (var candidate in pending)
        {
            results.Add(await Evaluate(candidate, ct));
        }

        if (results.Count > 0)
        {
            await _store.ApplyEvaluations(results, ct);
        }

        var finalized = results.Count(r => r.ForwardSoldCount >= MinForwardSoldCountForFinalEvaluation);
        return new DealBacktestTickResult(results.Count, finalized);
    }

    private async Task<DealSignalEvaluationResult> Evaluate(
        DealSignalEvaluationCandidate candidate, CancellationToken ct)
    {
        var windowDays = candidate.EvaluationWindowDays is null ? _options.HorizonDays : _options.HorizonDays * 2;
        var windowQuery = new PriceGroupForwardWindowQuery(
            candidate.TaxonomyVersionId,
            candidate.GroupKey,
            candidate.CreatedUtc,
            candidate.CreatedUtc.AddDays(windowDays),
            true);
        var windowStats = await _priceGroups.GetForwardWindowStats(windowQuery, ct);
        var saleInfo = await _store.GetListingSaleInfo(candidate.ListingEntityId, ct);

        var soldWithinHours = ComputeSoldWithinHours(candidate.CreatedUtc, saleInfo);
        var usedEstimatedDates = windowStats.UsedEstimatedDates || IsListingSaleDateEstimated(saleInfo);
        var realisedMargin = windowStats.NetMedian - candidate.LandedPrice;

        return new DealSignalEvaluationResult(
            candidate.Id,
            windowStats.NetMedian,
            windowStats.SoldCount,
            realisedMargin,
            soldWithinHours,
            usedEstimatedDates,
            windowDays);
    }

    private static bool IsListingSaleDateEstimated(DealSignalListingSaleInfo? saleInfo) =>
        saleInfo is { IsSold: true, SoldDateIsEstimated: true };

    private static double? ComputeSoldWithinHours(DateTime createdUtc, DealSignalListingSaleInfo? saleInfo)
    {
        if (saleInfo is not { IsSold: true } info)
        {
            return null;
        }

        var hours = (info.EffectiveSoldDateUtc - createdUtc).TotalHours;
        return hours < 0 ? 0 : hours;
    }
}
