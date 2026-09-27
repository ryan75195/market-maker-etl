using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Etl.Workers;

public sealed class ClassificationWorker : BackgroundService
{
    private readonly IListingClassificationService _classification;
    private readonly IOpenAiBudgetService _budget;
    private readonly ClassifierOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClassificationWorker> _logger;

    public ClassificationWorker(
        IListingClassificationService classification,
        IOpenAiBudgetService budget,
        ClassifierOptions options,
        TimeProvider timeProvider,
        ILogger<ClassificationWorker> logger)
    {
        _classification = classification;
        _budget = budget;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RunOnce(CancellationToken ct)
    {
        try
        {
            if (await _budget.IsExhausted(ct))
            {
                LogBudgetExhausted();
                return;
            }

            var result = await _classification.ClassifyPending(LogFailure, ct);
            LogSummary(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Classification tick failed");
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

    private void LogBudgetExhausted()
    {
        _logger.LogWarning(
            "Classification tick paused: OpenAI monthly budget of ${MonthlyBudgetUsd:F2} exhausted.",
            _budget.MonthlyBudgetUsd);
    }

    private void LogFailure(ClassificationBatchFailure failure)
    {
        _logger.LogWarning(
            "Classification batch failed for job {JobId} ({ListingCount} listings): {ErrorMessage}",
            failure.JobId,
            failure.ListingCount,
            failure.ErrorMessage);
    }

    private void LogSummary(ClassificationTickResult result)
    {
        if (result.ListingsSelected == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Classification tick: jobs {JobsProcessed}, selected {Selected}, classified {Classified}, failures {Failures}",
            result.JobsProcessed,
            result.ListingsSelected,
            result.ListingsClassified,
            result.Failures.Count);
    }
}
