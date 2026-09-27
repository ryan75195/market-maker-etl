using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Interfaces;

public interface IDetailBacklogThrottleService
{
    bool IsBackingOffInfrastructureFailures();

    int RemainingHourlyBudget();

    void RecordFetchAttempt();

    void ObserveTickResult(DetailBacklogTickResult result);
}
