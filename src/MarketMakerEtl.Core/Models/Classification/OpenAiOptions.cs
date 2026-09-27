namespace MarketMakerEtl.Core.Models.Classification;

public sealed record OpenAiOptions(
    string ApiKey,
    string Model,
    string ReasoningEffort,
    int BatchSize,
    int MaxConcurrency,
    int TimeoutSeconds);
