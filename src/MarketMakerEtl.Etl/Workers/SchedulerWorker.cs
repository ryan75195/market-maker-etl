using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scheduling;

namespace MarketMakerEtl.Etl.Workers;

public sealed class SchedulerWorker : BackgroundService
{
    private readonly IJobSchedulingService _jobScheduling;
    private readonly IListingRefreshSchedulingService _listingRefreshScheduling;
    private readonly ScheduleOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SchedulerWorker> _logger;

    public SchedulerWorker(
        IJobSchedulingService jobScheduling,
        IListingRefreshSchedulingService listingRefreshScheduling,
        ScheduleOptions options,
        TimeProvider timeProvider,
        ILogger<SchedulerWorker> logger)
    {
        _jobScheduling = jobScheduling;
        _listingRefreshScheduling = listingRefreshScheduling;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> RunOnce(CancellationToken ct)
    {
        var queued = await _jobScheduling.QueueDueJobs(ct);
        if (queued > 0)
        {
            _logger.LogInformation("Queued {Count} due jobs", queued);
        }

        try
        {
            await _listingRefreshScheduling.RefreshListingsIfDue(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Listing refresh failed");
        }

        return queued;
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
