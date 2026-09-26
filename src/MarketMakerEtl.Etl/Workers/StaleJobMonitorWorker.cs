using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Etl.Workers;

public sealed class StaleJobMonitorWorker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly IJobHealthService _jobHealth;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StaleJobMonitorWorker> _logger;

    public StaleJobMonitorWorker(
        IJobHealthService jobHealth,
        TimeProvider timeProvider,
        ILogger<StaleJobMonitorWorker> logger)
    {
        _jobHealth = jobHealth;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RunOnce(CancellationToken ct)
    {
        try
        {
            var jobs = await _jobHealth.GetJobHealth(ct);
            LogStaleJobs(jobs.Count(job => job.IsStale));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Stale job check failed");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnce(stoppingToken);
            await Task.Delay(CheckInterval, _timeProvider, stoppingToken);
        }
    }

    private void LogStaleJobs(int staleCount)
    {
        if (staleCount == 0)
        {
            return;
        }

        _logger.LogWarning(
            "{StaleCount} enabled job(s) have no completed run within their staleness window", staleCount);
    }
}
