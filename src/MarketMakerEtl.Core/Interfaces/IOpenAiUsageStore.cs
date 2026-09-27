using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Interfaces;

public interface IOpenAiUsageStore
{
    Task Record(
        string model, OpenAiUsagePurpose purpose, int promptTokens, int completionTokens, decimal costUsd, CancellationToken ct);

    Task<decimal> GetSpendSince(DateTime sinceUtc, CancellationToken ct);
}
