using System.Text.Json;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Deals;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class DealSignalBacktestStore : IDealSignalBacktestStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;

    public DealSignalBacktestStore(IDbContextFactory<EtlDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<DealSignalEvaluationCandidate>> GetSignalsPendingEvaluation(
        DateTime nowUtc, int horizonDays, int minForwardSoldCount, CancellationToken ct)
    {
        var baseCutoffUtc = nowUtc.AddDays(-horizonDays);
        var extendedCutoffUtc = nowUtc.AddDays(-(2 * horizonDays));

        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.DealSignals
            .Where(s =>
                (s.EvaluationWindowDays == null && s.CreatedUtc <= baseCutoffUtc)
                || (s.EvaluationWindowDays == horizonDays
                    && s.ForwardSoldCount < minForwardSoldCount
                    && s.CreatedUtc <= extendedCutoffUtc))
            .ToListAsync(ct);

        return rows.Select(BuildCandidate).ToList();
    }

    public async Task<DealSignalListingSaleInfo?> GetListingSaleInfo(int listingEntityId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == listingEntityId, ct);
        if (listing is null)
        {
            return null;
        }

        var effectiveSoldDate = listing.SoldDate ?? listing.UpdatedUtc ?? listing.CreatedUtc;
        return new DealSignalListingSaleInfo(listing.IsSold, effectiveSoldDate, listing.SoldDate is null);
    }

    public async Task ApplyEvaluations(IReadOnlyList<DealSignalEvaluationResult> results, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var ids = results.Select(r => r.SignalId).ToList();
        var entities = await db.DealSignals.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);

        foreach (var result in results)
        {
            if (entities.TryGetValue(result.SignalId, out var entity))
            {
                ApplyEvaluation(entity, result);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static void ApplyEvaluation(DealSignalEntity entity, DealSignalEvaluationResult result)
    {
        entity.ForwardNetMedian = result.ForwardNetMedian;
        entity.ForwardSoldCount = result.ForwardSoldCount;
        entity.RealisedMargin = result.RealisedMargin;
        entity.ListingSoldWithinHours = result.ListingSoldWithinHours;
        entity.UsedEstimatedDates = result.UsedEstimatedDates;
        entity.EvaluationWindowDays = result.EvaluationWindowDays;
    }

    private static DealSignalEvaluationCandidate BuildCandidate(DealSignalEntity entity)
    {
        var groupKey = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.GroupKeyJson)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        return new(
            entity.Id,
            entity.ListingEntityId,
            entity.TaxonomyVersionId,
            groupKey,
            entity.LandedPrice,
            entity.CreatedUtc,
            entity.EvaluationWindowDays);
    }
}
