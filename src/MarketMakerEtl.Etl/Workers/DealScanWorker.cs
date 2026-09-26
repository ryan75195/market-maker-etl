using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Deals;

namespace MarketMakerEtl.Etl.Workers;

public sealed class DealScanWorker : BackgroundService
{
    private readonly IDealSignalService _deals;
    private readonly DealsOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DealScanWorker> _logger;

    public DealScanWorker(
        IDealSignalService deals,
        DealsOptions options,
        TimeProvider timeProvider,
        ILogger<DealScanWorker> logger)
    {
        _deals = deals;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RunOnce(CancellationToken ct)
    {
        try
        {
            var result = await _deals.ScanForDeals(ct);
            LogSummary(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Deal scan tick failed");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.TickMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnce(stoppingToken);
            await Task.Delay(interval, _timeProvider, stoppingToken);
        }
    }

    private void LogSummary(DealScanTickResult result)
    {
        if (result.SignalsCreated == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Deal scan tick: families {FamiliesScanned}, groups {GroupsEvaluated}, signals {SignalsCreated}",
            result.FamiliesScanned,
            result.GroupsEvaluated,
            result.SignalsCreated);
    }
}
