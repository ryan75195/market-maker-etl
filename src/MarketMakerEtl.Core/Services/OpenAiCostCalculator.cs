using MarketMakerEtl.Core.Models.Classification;

namespace MarketMakerEtl.Core.Services;

internal static class OpenAiCostCalculator
{
    private const decimal TokensPerMillion = 1_000_000m;

    public static decimal Compute(OpenAiModelPricing pricing, int promptTokens, int completionTokens) =>
        promptTokens / TokensPerMillion * pricing.InputPerMillionUsd
            + completionTokens / TokensPerMillion * pricing.OutputPerMillionUsd;
}
