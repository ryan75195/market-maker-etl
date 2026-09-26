using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Scraper;

namespace MarketMakerEtl.Etl.Workers;

public sealed class DetailBacklogWorker : BackgroundService
{
    private readonly IDetailBacklogService _detailBacklog;
    private readonly DetailBacklogOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DetailBacklogWorker> _logger;

    public DetailBacklogWorker(
        IDetailBacklogService detailBacklog,
        DetailBacklogOptions options,
        TimeProvider timeProvider,
        ILogger<DetailBacklogWorker> logger)
    {
        _detailBacklog = detailBacklog;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RunOnce(CancellationToken ct)
    {
        try
        {
            var result = await _detailBacklog.RunTick(ct);
            LogFailures(result);
            LogSummary(result);
            LogFamilyProgress(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Detail backlog tick failed");
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

    private void LogFailures(DetailBacklogTickResult result)
    {
        foreach (var failure in result.Failures)
        {
            _logger.LogWarning(
                "Detail backlog fetch failed for listing {ListingId}: {ErrorMessage}",
                failure.ListingId,
                failure.ErrorMessage);
        }
    }

    private void LogSummary(DetailBacklogTickResult result)
    {
        if (result.Attempted == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Detail backlog tick: selected {Selected}, attempted {Attempted}, succeeded {Succeeded}, failed {Failed}",
            result.Selected,
            result.Attempted,
            result.Succeeded,
            result.Failures.Count);
    }

    private void LogFamilyProgress(DetailBacklogTickResult result)
    {
        if (result.FamilyAttempted == 0 && result.FamilyRemaining == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Detail backlog tick: family-priority fetches ran {FamilyAttempted}, remaining {FamilyRemaining}",
            result.FamilyAttempted,
            result.FamilyRemaining);
    }
}
