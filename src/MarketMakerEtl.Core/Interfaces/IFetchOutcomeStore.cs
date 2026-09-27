using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Interfaces;

public interface IFetchOutcomeStore
{
    Task RecordOutcome(FetchOutcomeKind kind, CancellationToken ct);

    Task<FetchOutcomeSnapshot> GetRecentOutcomes(TimeSpan window, CancellationToken ct);
}
