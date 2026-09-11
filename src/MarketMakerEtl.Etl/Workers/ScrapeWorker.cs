using MarketMakerEtl.Core.Interfaces;

namespace MarketMakerEtl.Etl.Workers;

public sealed class ScrapeWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);

    private readonly IScrapeStore _store;
    private readonly IScrapeRunService _runs;
    private readonly ILogger<ScrapeWorker> _logger;

    public ScrapeWorker(IScrapeStore store, IScrapeRunService runs, ILogger<ScrapeWorker> logger)
    {
        _store = store;
        _runs = runs;
        _logger = logger;
    }

    public async Task<bool> RunOnce(CancellationToken ct)
    {
        var work = await _store.ClaimNextQueuedRun(ct);

        if (work is null)
        {
            return false;
        }

        _logger.LogInformation("Running scrape run {RunId} for {SearchTerm}", work.RunId, work.SearchTerm);
        await _runs.Run(work, ct);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = await RunOnce(stoppingToken);

            if (!processed)
            {
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
