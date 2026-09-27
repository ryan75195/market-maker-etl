using System.Text.Json;
using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.PriceGroups;
using MarketMakerEtl.Core.Models.Trades;
using MarketMakerEtl.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class TradeStore : ITradeStore
{
    private readonly IDbContextFactory<EtlDbContext> _factory;
    private readonly PriceGroupOptions _priceGroupOptions;
    private readonly TimeProvider _timeProvider;

    public TradeStore(IDbContextFactory<EtlDbContext> factory, PriceGroupOptions priceGroupOptions, TimeProvider timeProvider)
    {
        _factory = factory;
        _priceGroupOptions = priceGroupOptions;
        _timeProvider = timeProvider;
    }

    public async Task<TradeView> CreateTrade(NewTrade trade, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var buyShipping = trade.BuyShipping ?? 0m;
        var entity = new TradeEntity
        {
            DealSignalId = trade.DealSignalId,
            ListingEntityId = trade.ListingEntityId,
            ProductFamilyId = trade.ProductFamilyId,
            PriceGroupKeyJson = SerializeGroupKey(trade.PriceGroupKey),
            BoughtUtc = trade.BoughtUtc,
            BuyPrice = trade.BuyPrice,
            BuyShipping = buyShipping,
            BuyFees = TradeProfitCalculator.ComputeBuyFees(trade.BuyPrice, buyShipping, trade.BuyFees, _priceGroupOptions),
            Status = TradeStatus.Open,
            Notes = trade.Notes,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc
        };

        db.Trades.Add(entity);
        await db.SaveChangesAsync(ct);
        return await MapToView(db, entity, ct);
    }

    public async Task<TradeView?> RecordSale(int id, TradeSale sale, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.Trades.FindAsync([id], ct);
        if (entity is null)
        {
            return null;
        }

        entity.SoldUtc = sale.SoldUtc;
        entity.SellPrice = sale.SellPrice;
        entity.SellShipping = sale.SellShipping ?? 0m;
        entity.SellFees = TradeProfitCalculator.ComputeSellFees(sale.SellPrice, sale.SellFees, _priceGroupOptions);
        entity.Status = sale.Status ?? TradeStatus.Sold;
        entity.Notes = sale.Notes ?? entity.Notes;
        entity.UpdatedUtc = _timeProvider.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);
        return await MapToView(db, entity, ct);
    }

    public async Task<IReadOnlyList<TradeView>> GetTrades(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entities = await db.Trades.OrderByDescending(t => t.CreatedUtc).ToListAsync(ct);
        return await MapToViews(db, entities, ct);
    }

    public async Task<TradeView?> GetTrade(int id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.Trades.FindAsync([id], ct);
        return entity is null ? null : await MapToView(db, entity, ct);
    }

    public async Task<TradeSummary> GetSummary(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var soldTrades = await db.Trades.Where(t => t.SellPrice != null).ToListAsync(ct);
        var signalIds = soldTrades
            .Where(t => t.DealSignalId != null)
            .Select(t => t.DealSignalId!.Value)
            .Distinct()
            .ToList();
        var signals = await db.DealSignals.Where(s => signalIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        var outcomes = soldTrades.Select(t => BuildOutcome(t, signals)).ToList();
        return TradeSummaryCalculator.Build(outcomes);
    }

    private static TradeOutcome BuildOutcome(TradeEntity trade, IReadOnlyDictionary<int, DealSignalEntity> signals)
    {
        var realisedProfit = TradeProfitCalculator.ComputeRealisedProfit(
            trade.SellPrice!.Value,
            trade.SellShipping ?? 0m,
            trade.SellFees ?? 0m,
            trade.BuyPrice,
            trade.BuyShipping,
            trade.BuyFees);
        var daysToSell = trade.SoldUtc.HasValue
            ? TradeProfitCalculator.ComputeDaysToSell(trade.BoughtUtc, trade.SoldUtc.Value)
            : (double?)null;
        var predictedMargin = ResolvePredictedMargin(trade.DealSignalId, signals);

        return new TradeOutcome(realisedProfit, daysToSell, predictedMargin);
    }

    private static decimal? ResolvePredictedMargin(
        int? dealSignalId, IReadOnlyDictionary<int, DealSignalEntity> signals)
    {
        if (dealSignalId is not int id || !signals.TryGetValue(id, out var signal) || signal.SoldNetMedian is null)
        {
            return null;
        }

        return signal.SoldNetMedian.Value - signal.LandedPrice;
    }

    private static async Task<TradeView> MapToView(EtlDbContext db, TradeEntity entity, CancellationToken ct)
    {
        var listing = entity.ListingEntityId is int listingId
            ? await db.Listings.FindAsync([listingId], ct)
            : null;
        var signal = entity.DealSignalId is int signalId
            ? await db.DealSignals.FindAsync([signalId], ct)
            : null;
        return BuildView(entity, listing, signal);
    }

    private static async Task<IReadOnlyList<TradeView>> MapToViews(
        EtlDbContext db, IReadOnlyList<TradeEntity> entities, CancellationToken ct)
    {
        var listingIds = entities.Where(t => t.ListingEntityId != null).Select(t => t.ListingEntityId!.Value).Distinct().ToList();
        var listings = await db.Listings.Where(l => listingIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var signalIds = entities.Where(t => t.DealSignalId != null).Select(t => t.DealSignalId!.Value).Distinct().ToList();
        var signals = await db.DealSignals.Where(s => signalIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        return entities
            .Select(e => BuildView(
                e,
                e.ListingEntityId is int listingId ? listings.GetValueOrDefault(listingId) : null,
                e.DealSignalId is int signalId ? signals.GetValueOrDefault(signalId) : null))
            .ToList();
    }

    private static TradeView BuildView(TradeEntity entity, ListingEntity? listing, DealSignalEntity? signal)
    {
        var realisedProfit = entity.SellPrice is decimal sellPrice
            ? TradeProfitCalculator.ComputeRealisedProfit(
                sellPrice, entity.SellShipping ?? 0m, entity.SellFees ?? 0m,
                entity.BuyPrice, entity.BuyShipping, entity.BuyFees)
            : (decimal?)null;
        var daysToSell = entity.SoldUtc is DateTime soldUtc
            ? TradeProfitCalculator.ComputeDaysToSell(entity.BoughtUtc, soldUtc)
            : (double?)null;
        var predictedMargin = signal?.SoldNetMedian is decimal median ? median - signal.LandedPrice : (decimal?)null;

        return new TradeView(
            entity.Id,
            entity.DealSignalId,
            entity.ListingEntityId,
            listing?.Title,
            listing?.Url,
            entity.ProductFamilyId,
            DeserializeGroupKey(entity.PriceGroupKeyJson),
            entity.BoughtUtc,
            entity.BuyPrice,
            entity.BuyShipping,
            entity.BuyFees,
            entity.SoldUtc,
            entity.SellPrice,
            entity.SellShipping,
            entity.SellFees,
            entity.Status,
            entity.Notes,
            entity.CreatedUtc,
            entity.UpdatedUtc,
            realisedProfit,
            daysToSell,
            predictedMargin);
    }

    private static string? SerializeGroupKey(IReadOnlyDictionary<string, string>? groupKey) =>
        groupKey is null ? null : JsonSerializer.Serialize(groupKey);

    private static IReadOnlyDictionary<string, string>? DeserializeGroupKey(string? groupKeyJson) =>
        groupKeyJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(groupKeyJson);
}
