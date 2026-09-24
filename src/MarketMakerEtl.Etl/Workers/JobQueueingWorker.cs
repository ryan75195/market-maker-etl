using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scheduling;

namespace MarketMakerEtl.Etl.Workers;

public sealed class JobQueueingWorker : BackgroundService
{
    private readonly IJobSchedulingService _jobScheduling;
    private readonly ScheduleOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobQueueingWorker> _logger;

    public JobQueueingWorker(
        IJobSchedulingService jobScheduling,
        ScheduleOptions options,
        TimeProvider timeProvider,
        ILogger<JobQueueingWorker> logger)
    {
        _jobScheduling = jobScheduling;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> RunOnce(CancellationToken ct)
    {
        try
        {
            var queued = await _jobScheduling.QueueDueJobs(ct);
            if (queued > 0)
            {
                _logger.LogInformation("Queued {Count} due jobs", queued);
            }

            return queued;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Job queueing failed");
            return 0;
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
