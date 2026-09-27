using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Health;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Core.Services;

public sealed class FetcherHealthService : IFetcherHealthService
{
    private readonly IFetcherHealthClient _client;
    private readonly IFetchOutcomeStore _outcomeStore;
    private readonly FetcherHealthOptions _options;

    public FetcherHealthService(
        IFetcherHealthClient client, IFetchOutcomeStore outcomeStore, FetcherHealthOptions options)
    {
        _client = client;
        _outcomeStore = outcomeStore;
        _options = options;
    }

    public async Task<FetcherHealthView> GetFetcherHealth(CancellationToken ct)
    {
        var reachable = await _client.CheckSidecarReachable(ct);
        var window = TimeSpan.FromMinutes(_options.RecentWindowMinutes);
        var outcomes = await _outcomeStore.GetRecentOutcomes(window, ct);
        var isDegraded = !reachable || HasOnlyFailedRecently(outcomes);

        return new FetcherHealthView(
            reachable,
            _options.RecentWindowMinutes,
            outcomes.SuccessCount,
            outcomes.InfrastructureFailureCount,
            outcomes.NotFoundFailureCount,
            outcomes.OtherFailureCount,
            isDegraded);
    }

    private bool HasOnlyFailedRecently(FetchOutcomeSnapshot outcomes) =>
        outcomes.TotalCount >= _options.MinRecentAttemptsForDegraded && outcomes.SuccessCount == 0;
}
