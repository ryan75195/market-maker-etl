using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Interfaces;

public interface IFetchOutcomeMonitor
{
    void Record(FetchOutcomeKind kind);

    FetchOutcomeSnapshot GetRecentOutcomes(TimeSpan window);
}
