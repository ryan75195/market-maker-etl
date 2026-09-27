using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Trades;

namespace MarketMakerEtl.Api;

public static class TradeEndpoints
{
    public static WebApplication MapTradeEndpoints(this WebApplication app)
    {
        app.MapPost("/api/trades", CreateTrade);
        app.MapPut("/api/trades/{id:int}/sale", RecordSale);
        app.MapGet("/api/trades", GetTrades);
        app.MapGet("/api/trades/summary", GetSummary);

        return app;
    }

    private static async Task<IResult> CreateTrade(
        CreateTradeRequest request, ITradeStore trades, CancellationToken ct)
    {
        var trade = await trades.CreateTrade(request.ToNewTrade(), ct);
        return Results.Created($"/api/trades/{trade.Id}", trade);
    }

    private static async Task<IResult> RecordSale(
        int id, RecordTradeSaleRequest request, ITradeStore trades, CancellationToken ct)
    {
        var trade = await trades.RecordSale(id, request.ToTradeSale(), ct);
        return trade is null ? Results.NotFound() : Results.Ok(trade);
    }

    private static async Task<IResult> GetTrades(ITradeStore trades, CancellationToken ct) =>
        Results.Ok(await trades.GetTrades(ct));

    private static async Task<IResult> GetSummary(ITradeStore trades, CancellationToken ct) =>
        Results.Ok(await trades.GetSummary(ct));
}

public sealed record CreateTradeRequest(
    int? DealSignalId,
    int? ListingEntityId,
    int? ProductFamilyId,
    IReadOnlyDictionary<string, string>? PriceGroupKey,
    DateTime BoughtUtc,
    decimal BuyPrice,
    decimal? BuyShipping,
    decimal? BuyFees,
    string? Notes)
{
    public NewTrade ToNewTrade() => new(
        DealSignalId, ListingEntityId, ProductFamilyId, PriceGroupKey, BoughtUtc, BuyPrice, BuyShipping, BuyFees, Notes);
}

public sealed record RecordTradeSaleRequest(
    DateTime SoldUtc,
    decimal SellPrice,
    decimal? SellShipping,
    decimal? SellFees,
    TradeStatus? Status,
    string? Notes)
{
    public TradeSale ToTradeSale() => new(SoldUtc, SellPrice, SellShipping, SellFees, Status, Notes);
}
