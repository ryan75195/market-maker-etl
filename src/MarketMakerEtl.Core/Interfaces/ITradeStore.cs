using MarketMakerEtl.Core.Models.Trades;

namespace MarketMakerEtl.Core.Interfaces;

public interface ITradeStore
{
    Task<TradeView> CreateTrade(NewTrade trade, CancellationToken ct);

    Task<TradeView?> RecordSale(int id, TradeSale sale, CancellationToken ct);

    Task<IReadOnlyList<TradeView>> GetTrades(CancellationToken ct);

    Task<TradeView?> GetTrade(int id, CancellationToken ct);

    Task<TradeSummary> GetSummary(CancellationToken ct);
}
