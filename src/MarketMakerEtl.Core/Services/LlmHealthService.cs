using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Health;

namespace MarketMakerEtl.Core.Services;

public sealed class LlmHealthService : ILlmHealthService
{
    private readonly IListingClassificationStore _classifications;
    private readonly OpenAiOptions _openAiOptions;
    private readonly ClassifierOptions _classifierOptions;

    public LlmHealthService(
        IListingClassificationStore classifications, OpenAiOptions openAiOptions, ClassifierOptions classifierOptions)
    {
        _classifications = classifications;
        _openAiOptions = openAiOptions;
        _classifierOptions = classifierOptions;
    }

    public async Task<LlmHealthView> GetLlmHealth(CancellationToken ct)
    {
        var sinceUtc = DateTime.UtcNow.AddHours(-1);
        var lastHour = await _classifications.GetBatchOutcomesSince(sinceUtc, ct);
        var recent = await _classifications.GetRecentBatchOutcomes(_classifierOptions.DegradedAfterFailedBatches, ct);

        var succeeded = lastHour.Count(run => run.Succeeded);
        var failed = lastHour.Count(run => !run.Succeeded);
        var degraded = recent.Count == _classifierOptions.DegradedAfterFailedBatches && recent.All(run => !run.Succeeded);

        return new LlmHealthView(_openAiOptions.Model, _openAiOptions.ApiKey.Length > 0, succeeded, failed, degraded);
    }
}
