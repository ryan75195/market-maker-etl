using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scheduling;

namespace MarketMakerEtl.Etl.Workers;

public sealed class ListingRefreshWorker : BackgroundService
{
    private readonly IListingRefreshSchedulingService _listingRefreshScheduling;
    private readonly ScheduleOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ListingRefreshWorker> _logger;

    public ListingRefreshWorker(
        IListingRefreshSchedulingService listingRefreshScheduling,
        ScheduleOptions options,
        TimeProvider timeProvider,
        ILogger<ListingRefreshWorker> logger)
    {
        _listingRefreshScheduling = listingRefreshScheduling;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RunOnce(CancellationToken ct)
    {
        try
        {
            await _listingRefreshScheduling.RefreshListingsIfDue(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Listing refresh failed");
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
}
